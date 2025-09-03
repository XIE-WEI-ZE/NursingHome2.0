using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Text;
using System.Text.Json;
using static System.Net.WebRequestMethods;

namespace prjFinalProjectApi.Controllers
{
    [ApiController]
    [Route("api/EventRegistration")] // => /api/EventTemplate

    public class EventRegistrationController : Controller
    {
        private readonly DbNursingHomeContext _db;
        private readonly IHttpClientFactory _http;
        private readonly IConfiguration _cfg;

        public EventRegistrationController(DbNursingHomeContext db, IHttpClientFactory http, IConfiguration cfg)
        {
            _db = db;
            _http = http;
            _cfg = cfg;
        }

        /// <summary>建立報名明細（回傳 RegistrationId / RegistrationNum）</summary>
        [HttpPost]
        public async Task<ActionResult<RegistrationResDto>> Create([FromBody] RegistrationCreateDto dto)
        {
            if (dto is null) return BadRequest(new { message = "payload required" });
            if (dto.EventBatchId <= 0 || dto.MemberId <= 0)
                return BadRequest(new { message = "EventBatchId 與 MemberId 為必填且需大於 0" });
            if (dto.AmountDue > 0 && dto.Payment is null)
                return BadRequest(new { message = "金額大於 0 時必須提供付款資料" });

            var now = dto.RegistrationDateTime ?? DateTime.Now;

            await using var tx = await _db.Database.BeginTransactionAsync();

            // 先組報名 entity（不含 RegistrationNum）
            var reg = new RegistrationDetail
            {
                EventBatchId = dto.EventBatchId,
                MemberId = dto.MemberId,
                AmountDue = dto.AmountDue,
                RegistrationDateTime = now,
                CurrentStatus = dto.CurrentStatus <= 0 ? 1 : dto.CurrentStatus,
                InternalRemarks = dto.InternalRemarks
            };

            // ===== 產生 RegistrationNum：REG + yyyyMMdd + 3 位流水（保留你的做法，含重試） =====
            var ymd = now.ToString("yyyyMMdd");
            var prefix = $"REG{ymd}";
            const int maxAttempts = 3;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var todayCount = await _db.RegistrationDetails
                    .CountAsync(x => x.RegistrationNum.StartsWith(prefix));

                reg.RegistrationNum = $"{prefix}{(todayCount + 1):000}";
                _db.RegistrationDetails.Add(reg);

                try
                {
                    await _db.SaveChangesAsync();
                    break; // 建立成功就跳出
                }
                catch (DbUpdateException)
                {
                    // 可能撞到唯一索引（建議對 RegistrationNum 建唯一索引）
                    _db.Entry(reg).State = EntityState.Detached;

                    if (attempt == maxAttempts)
                    {
                        await tx.RollbackAsync();
                        return StatusCode(409, new { message = "產生報名編號失敗，請稍後再試" });
                    }

                    await Task.Delay(Random.Shared.Next(10, 50));
                    reg = new RegistrationDetail
                    {
                        EventBatchId = dto.EventBatchId,
                        MemberId = dto.MemberId,
                        AmountDue = dto.AmountDue,
                        RegistrationDateTime = now,
                        CurrentStatus = dto.CurrentStatus <= 0 ? 1 : dto.CurrentStatus,
                        InternalRemarks = dto.InternalRemarks
                    };
                }
            }

            // ===== 有金額且有帶付款資料：建立 EventPaymentDetails =====
            string? paymentUrl = null;     // ⬅ 讓回傳可以帶給前端
            string? orderId = null;
            if (dto.AmountDue > 0 && dto.Payment is not null)
            {
                var pay = new EventPaymentDetail   // 你的 EF 實體名稱可能是 EventPaymentDetail 或 EventPaymentDetails
                {
                    RegistrationId = reg.RegistrationId,  //報名編號
                    PaymentMethod = dto.Payment.PaymentMethod, //付款方式
                    PaymentItem = dto.Payment.PaymentItem,  //繳費項目
                    PaymentAmount = dto.Payment.PaymentAmount, //繳費金額
                    InvoiceType = dto.Payment.InvoiceType,
                    InvoiceTitle = dto.Payment.InvoiceTitle,
                    TaxId = dto.Payment.TaxId,
                    EinvoiceCarrier = dto.Payment.EInvoiceCarrier, //發票載具
                    Status = 0,                    // 0=已開單/未入帳（依你的定義）
                    Note = dto.Payment.note,  //備註
                };

                _db.EventPaymentDetails.Add(pay);
                await _db.SaveChangesAsync();


                // ✅ 只有 LINEPAY 才打 _test/request
                if (string.Equals(pay.PaymentMethod, "LINEPAY", StringComparison.OrdinalIgnoreCase))
                {
                    // LINE Pay 僅接受整數金額（TWD）
                    var amountInt = (int)Math.Round(pay.PaymentAmount, 0, MidpointRounding.AwayFromZero);
                    if ((decimal)amountInt != pay.PaymentAmount)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { message = "LINE Pay 需整數金額（TWD）。" });
                    }

                    // 產生唯一 orderId（用 RegistrationId + ticks）
                    orderId = reg.RegistrationNum;
                    //orderId = $"REG{reg.RegistrationId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                    // 組 _test/request 需要的 JSON
                    var bodyObj = new
                    {
                        orderId = orderId,
                        amount = amountInt,
                        currency = "TWD",
                        packages = new[] {
                    new {
                        id = "pkg1",
                        amount = amountInt,
                        products = new[] {
                            new { name = pay.PaymentItem, quantity = 1, price = amountInt }
                        }
                    }
                },
                        redirectUrls = new
                        {
                            confirmUrl = _cfg["LinePay:ConfirmUrl"],
                            cancelUrl = _cfg["LinePay:CancelUrl"]
                        }
                    };
                    var json = JsonSerializer.Serialize(bodyObj);

                    // 呼叫本機的 _test/request
                    var url = _cfg["LinePay:TestRequestUrl"] ?? "https://localhost:7124/api/pay/line/_test/request";
                    var http = _http.CreateClient();
                    var httpResp = await http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

                    var respText = await httpResp.Content.ReadAsStringAsync();
                    if (!httpResp.IsSuccessStatusCode)
                    {
                        var raw = await httpResp.Content.ReadAsStringAsync();
                        await tx.RollbackAsync();
                        return StatusCode(502, new { message = "呼叫 LINE Pay 測試端點失敗",
                            raw = respText,
                            url,
                            sent = bodyObj
                        });
                    }

                    // 解析回傳 paymentUrl
                    using var doc = JsonDocument.Parse(await httpResp.Content.ReadAsStringAsync());
                    paymentUrl = doc.RootElement.TryGetProperty("paymentUrl", out var p) ? p.GetString() : null;

                    // 把 orderId 記在 Note（或你有 MerchantOrderId 欄位就寫那裡）
                    pay.Note = (string.IsNullOrEmpty(pay.Note) ? "" : pay.Note + " | ")
                               + $"orderId={orderId}";
                    await _db.SaveChangesAsync();
                }
            }

            await tx.CommitAsync();

            var res = new RegistrationResDto
            {
                RegistrationId = reg.RegistrationId,
                RegistrationNum = reg.RegistrationNum,
                RequiresPayment = dto.AmountDue > 0
            };

            return Ok(new
            {
                registration = res,
                linePay = paymentUrl != null ? new
                {
                    orderId = orderId,       // e.g. "REG12-17243xxxxxxx"
                    paymentUrl = paymentUrl  // e.g. "https://sandbox-web-pay.line.me/..."
                } : null
            });
        }


        //顯示全部的報名資料
        [HttpGet("list")]
        public async Task<IActionResult> List()
        {
            var items = await (
                from r in _db.RegistrationDetails.AsNoTracking()//減少記憶體負擔
                join b in _db.EventBatches.AsNoTracking()
                    on r.EventBatchId equals b.BatchId
                join e in _db.EventTemplates.AsNoTracking()
                    on b.EventId equals e.EventId
                orderby r.RegistrationId
                select new RegistrationListDto
                {
                    RegistrationId = r.RegistrationId,
                    RegistrationNum = r.RegistrationNum,
                    EventBatchId = r.EventBatchId,
                    MemberId = r.MemberId,
                    AmountDue = r.AmountDue,
                    RegistrationDateTime = r.RegistrationDateTime,
                    CurrentStatus = r.CurrentStatus,
                    InternalRemarks = r.InternalRemarks,
                    EventName = e.EventName,   //活動名稱
                    EventDateTimeStart=b.EventDateTimeStart,//活動時間
                    EventLocation=e.EventLocation, //活動地點
                }
            ).ToListAsync();

            return Ok(items);
        }

        // 顯示報名資料 (需給予使用者id，可選批次id與狀態)
        //GET /api/EventRegistration/memberId/15?batchId=3&status=2
        [HttpGet("memberId/{memberId:int}")]
        public async Task<IActionResult> List(
            int memberId,
            [FromQuery] int? batchId = null,
            [FromQuery] int? status = null   // ✅ 新增：狀態參數
        )
        {
            var query = from r in _db.RegistrationDetails.AsNoTracking()
                        join b in _db.EventBatches.AsNoTracking()
                            on r.EventBatchId equals b.BatchId
                        join e in _db.EventTemplates.AsNoTracking()
                            on b.EventId equals e.EventId
                        where r.MemberId == memberId
                        orderby r.RegistrationId
                        select new RegistrationListDto
                        {
                            RegistrationId = r.RegistrationId,
                            RegistrationNum = r.RegistrationNum,
                            EventBatchId = r.EventBatchId,
                            MemberId = r.MemberId,
                            AmountDue = r.AmountDue,
                            RegistrationDateTime = r.RegistrationDateTime,
                            CurrentStatus = r.CurrentStatus,
                            InternalRemarks = r.InternalRemarks,
                            EventName = e.EventName,                   // 活動名稱
                            EventDateTimeStart = b.EventDateTimeStart, // 活動時間
                            EventLocation = e.EventLocation            // 活動地點
                        };

            // ✅ 批次篩選
            if (batchId.HasValue)
                query = query.Where(x => x.EventBatchId == batchId.Value);

            // ✅ 狀態篩選
            if (status.HasValue)
                query = query.Where(x => x.CurrentStatus == status.Value);

            var items = await query.ToListAsync();

            // ✅ 判斷是否有資料
            if (!items.Any())
                return NotFound($"查無會員 {memberId} 的報名資料 (批次={batchId?.ToString() ?? "全部"}, 狀態={status?.ToString() ?? "全部"})");

            return Ok(items);
        }

        //活動取消 將狀態改為0
        [HttpPut("cancel")]
        public async Task<IActionResult> Cancel([FromBody] CancelRegistrationDto dto)
        {
            if (dto is null) return BadRequest(new { message = "payload required" });
            if (dto.MemberId <= 0 || dto.EventBatchId <= 0)
                return BadRequest(new { message = "MemberId 與 EventBatchId 為必填且需大於 0" });

            // 找「最新一筆」該會員在該批次的報名
            var reg = await _db.RegistrationDetails
                .Where(r => r.MemberId == dto.MemberId && r.EventBatchId == dto.EventBatchId)
                .OrderByDescending(r => r.RegistrationDateTime) // 先比時間
                .ThenByDescending(r => r.RegistrationId)        // 再比流水，防止 null 或同秒
                .FirstOrDefaultAsync();

            if (reg == null)
                return NotFound(new { message = $"找不到會員 {dto.MemberId} 在批次 {dto.EventBatchId} 的報名紀錄" });

            // 已取消就不重複動作
            if (reg.CurrentStatus == 0)
            {
                return Ok(new
                {
                    message = "該報名已是取消狀態（CurrentStatus=0）",
                    registrationId = reg.RegistrationId,
                    registrationNum = reg.RegistrationNum,
                    currentStatus = reg.CurrentStatus
                });
            }

            // 寫入取消
            reg.CurrentStatus = 0; // 0=取消
            // 可選：附註取消原因與時間戳
            var stamp = $"[cancel {DateTime.Now:yyyy-MM-dd HH:mm:ss}]";
            reg.InternalRemarks = string.IsNullOrWhiteSpace(reg.InternalRemarks)
                ? $"{stamp} {dto.Reason}".Trim()
                : $"{reg.InternalRemarks} | {stamp} {dto.Reason}".Trim();

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "取消成功",
                registrationId = reg.RegistrationId,
                registrationNum = reg.RegistrationNum,
                currentStatus = reg.CurrentStatus
            });
        }

    }
}


//測試資料
//{
//    "eventBatchId": 1,
//  "memberId": 15,
//  "amountDue": 0,
//  "registrationDateTime": "2025-08-20T10:00:00",
//  "currentStatus": 1,
//  "internalRemarks": "免費活動測試"
//}


//{
//    "eventBatchId": 1,
//  "memberId": 15,
//  "amountDue": 300.00,
//  "registrationDateTime": "2025-08-20T10:05:00",
//  "currentStatus": 1,
//  "internalRemarks": "付費活動測試",
//  "payment": {
//        "paymentMethod": "CASH",
//    "paymentItem": "活動報名費",
//    "paymentAmount": 300.00,
//    "invoiceType": "二聯式",
//    "invoiceTitle": "個人",
//    "taxId": "",
//    "eInvoiceCarrier": "/AB12345",
//    "transactionId": "TEST-20250820-0001"
//  }
//}