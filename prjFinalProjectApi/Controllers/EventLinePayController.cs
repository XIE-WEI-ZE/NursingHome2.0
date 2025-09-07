using Humanizer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Services;
using System;
using System.Linq; 
using System.Text.Json; // 若要處理 JsonElement

namespace prjFinalProjectApi.Controllers
{
    [ApiController]
    [Route("api/pay/line")]
    public class EventLinePayController : ControllerBase
    {
        private readonly DbNursingHomeContext _db;
        private readonly LinePayService _linePay;

        public EventLinePayController(DbNursingHomeContext db, LinePayService linePay)
        {
            _db = db;
            _linePay = linePay;
        }

        // POST /api/pay/line/start?registrationId=123
        [HttpPost("start")]
        public async Task<IActionResult> Start([FromQuery] int registrationId)
        {
            var reg = await _db.RegistrationDetails.FindAsync(registrationId);
            if (reg == null) return NotFound("找不到報名");
            if (reg.AmountDue is null || reg.AmountDue <= 0) return BadRequest("此報名無需付款");

            // 取最新未繳，沒有就建立一筆
            var pay = await _db.EventPaymentDetails
                .OrderByDescending(p => p.SendTime)
                .FirstOrDefaultAsync(p => p.RegistrationId == registrationId && p.Status == 0);

            if (pay == null)
            {
                pay = new EventPaymentDetail
                {
                    RegistrationId = registrationId,
                    PaymentMethod = "LINEPAY",
                    PaymentItem = "活動報名費",
                    PaymentAmount = reg.AmountDue!.Value,
                    Status = 0,
                    // SendTime 建議由 DB Default(SYSDATETIME)自動填；若沒有，可加：
                    // SendTime = DateTime.UtcNow
                };
                _db.EventPaymentDetails.Add(pay);
                await _db.SaveChangesAsync();
            }

            // 產生唯一 orderId 並（若有欄位）存回以便 confirm 精準匹配
            var orderId = $"REG{registrationId}-{DateTime.UtcNow.Ticks}";
            await _db.SaveChangesAsync();

            var result = await _linePay.RequestAsync(orderId, (int)pay.PaymentAmount, "活動報名費");

            // 失敗時把 code/raw 一起回給你看
            if (result.returnCode != "0000")
                return StatusCode(502, new { code = result.returnCode, message = result.returnMessage, raw = result.raw });
            pay.Note = (string.IsNullOrEmpty(pay.Note) ? "" : pay.Note + " | ") + $"orderId={orderId}";
            await _db.SaveChangesAsync();
            // 只在成功時取 paymentUrl
            var paymentUrl = result.info.GetProperty("paymentUrl").GetProperty("web").GetString();
            if (string.IsNullOrEmpty(paymentUrl))
                return StatusCode(502, new { message = "LINE Pay 回傳缺 paymentUrl", raw = result.raw });

            return Ok(new { paymentUrl });
        }

        public class ConfirmDto
        {
            public string TransactionId { get; set; } = default!;
            public string OrderId { get; set; } = default!;
        }

        // POST /api/pay/line/confirm
        [HttpPost("confirm")]
        public async Task<IActionResult> Confirm([FromBody] ConfirmDto dto)
        {
            var pay = await _db.EventPaymentDetails
                .OrderByDescending(p => p.SendTime)
                .FirstOrDefaultAsync(p => p.Status == 0);

            if (pay == null) return NotFound("找不到未繳付款紀錄");

            var result = await _linePay.ConfirmAsync(dto.TransactionId, (int)pay.PaymentAmount);

            if (result.returnCode == "0000")
            {
                pay.Status = 1; // 已繳
                pay.TransactionId = dto.TransactionId;
                pay.LinePayTime = DateOnly.FromDateTime(DateTime.UtcNow);

                // ⬇️ 把系統回傳資料寫到 note
                // 假設 note 是 NVARCHAR(MAX)，可直接存；若是短欄位可自行截斷
                var noteObj = new
                {
                    stage = "confirm",
                    orderId = dto.OrderId,
                    transactionId = dto.TransactionId,
                    lineReturnCode = result.returnCode,
                    lineReturnMessage = result.returnMessage,
                    lineRaw = result.raw     // 這是你在 Service 裡帶回的原始 JSON
                };
                pay.Note = JsonSerializer.Serialize(noteObj);

                await _db.SaveChangesAsync();
                return Ok(new { success = true });
            }

            // 失敗也建議寫入 note，方便查
            var failNote = new
            {
                stage = "confirm-failed",
                orderId = dto.OrderId,
                transactionId = dto.TransactionId,
                lineReturnCode = result.returnCode,
                lineReturnMessage = result.returnMessage,
                lineRaw = result.raw
            };
            pay.Status = 9;
            pay.Note = JsonSerializer.Serialize(failNote);

            await _db.SaveChangesAsync();
            return BadRequest(new { success = false, message = result.returnMessage, raw = result.raw });
        }

        // 直接代送你貼的 JSON 給 LINE Pay
        [HttpPost("_test/request")]
        public async Task<IActionResult> TestRequest([FromBody] JsonElement body)
        {
            // 取得你貼進來的原始 JSON 字串（保持字面一模一樣，避免簽章不一致）
            var bodyJson = body.GetRawText();

            var result = await _linePay.RequestRawAsync(bodyJson);

            if (result.returnCode != "0000")
                return StatusCode(502, new { code = result.returnCode, message = result.returnMessage, raw = result.raw });

            string? paymentUrl = null;
            if (result.info.ValueKind == JsonValueKind.Object &&
                result.info.TryGetProperty("paymentUrl", out var urlNode) &&
                urlNode.TryGetProperty("web", out var webNode))
            {
                paymentUrl = webNode.GetString();
            }

            return Ok(new { paymentUrl, raw = result.raw });
        }

        //line pay使用者付款完後 後續執行的部分
        [HttpGet("return")]
        public async Task<IActionResult> Return([FromQuery] string transactionId, [FromQuery] string orderId)
        {
            // 優先用 Note 內的 orderId 精準找到「未繳」的那筆
            var pay = await _db.EventPaymentDetails
                .Where(p => p.Status == 0 && p.Note != null && EF.Functions.Like(p.Note!, $"%{orderId}%"))
                .OrderByDescending(p => p.SendTime)
                .FirstOrDefaultAsync();

            // 找不到就退而求其次抓「最新未繳」，多人同時付款有風險，日後請加 MerchantOrderId 欄位
            pay ??= await _db.EventPaymentDetails
                .Where(p => p.Status == 0)
                .OrderByDescending(p => p.SendTime)
                .FirstOrDefaultAsync();

            bool ok = false;
            string msg = "no pending payment; maybe already processed";

            if (pay != null)
            {
                // 呼叫 LINE Pay Confirm
                var res = await _linePay.ConfirmAsync(transactionId, (int)pay.PaymentAmount);
                ok = (res.returnCode == "0000");
                msg = res.returnMessage;

                // 僅在仍為未繳時更新（冪等）
                if (pay.Status == 0)
                {
                    pay.Status = ok ? 1 : 9;
                    if (ok)
                    {
                        pay.TransactionId = transactionId; // ← 若你的欄位是 TransactionID，請改成 pay.TransactionID
                        pay.LinePayTime = DateOnly.FromDateTime(DateTime.UtcNow);
                    }

                    // 記錄回傳資訊到 Note（JSON）
                    var noteObj = new
                    {
                        stage = "return",
                        orderId,
                        transactionId,
                        lineReturnCode = res.returnCode,
                        lineReturnMessage = res.returnMessage,
                        at = DateTime.UtcNow
                    };
                    pay.Note = JsonSerializer.Serialize(noteObj);

                    await _db.SaveChangesAsync();
                }
            }

            // 回最小 HTML：postMessage 給主視窗，並自動關閉
            var safeOrderId = (orderId ?? "").Replace("'", "\\'");
            var safeTxId = (transactionId ?? "").Replace("'", "\\'");
            var safeMsg = JsonSerializer.Serialize(msg); // 讓字串安全進 JS


            var html = $@"<!doctype html>
                        <meta charset='utf-8'>
                        <title>LINE Pay</title>
                        <style>body{{font:14px/1.5 -apple-system,Segoe UI,Roboto,'Noto Sans TC',sans-serif;padding:24px;}}</style>
                        <div>付款{(ok ? "成功" : "未完成/失敗")}，本視窗將自動關閉…</div>
                        <script>
                        try {{
                          window.opener && window.opener.postMessage({{
                            type: 'LINEPAY_DONE',
                            ok: {(ok ? "true" : "false")},
                            orderId: '{safeOrderId}',
                            transactionId: '{safeTxId}',
                            message: {safeMsg}
                          }}, '*');
                        }} catch(e) {{}}
                        setTimeout(function(){{ window.close(); }}, 800);
                        </script>";
            return Content(html, "text/html; charset=utf-8");
        }


    }
}
