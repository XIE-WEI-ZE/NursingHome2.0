using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;

namespace prjFinalProjectApi.Controllers
{
    [ApiController]
    [Route("api/EventRegistration")] // => /api/EventTemplate

    public class EventRegistrationController : Controller
    {
        private readonly DbNursingHomeContext _db;

        public EventRegistrationController(DbNursingHomeContext db) => _db = db;

        /// <summary>建立報名明細（回傳 RegistrationId / RegistrationNum）</summary>
        [HttpPost]
        public async Task<ActionResult<RegistrationResDto>> Create([FromBody] RegistrationCreateDto dto)
        {
            if (dto.EventBatchId <= 0 || dto.MemberId <= 0)
                return BadRequest(new { message = "EventBatchId 與 MemberId 為必填且需大於 0" });

            var now = dto.RegistrationDateTime ?? DateTime.Now;

            // 準備 entity（不含 RegistrationNum，待產生）
            var entity = new RegistrationDetail
            {
                EventBatchId = dto.EventBatchId,
                MemberId = dto.MemberId,
                AmountDue = dto.AmountDue,
                RegistrationDateTime = now,
                CurrentStatus = dto.CurrentStatus,
                InternalRemarks = dto.InternalRemarks
            };

            // 產生 RegistrationNum：REG + yyyyMMdd + 3位流水（001 起）
            var ymd = now.ToString("yyyyMMdd");
            var prefix = $"REG{ymd}";

            const int maxAttempts = 3; // 撞號重試（需配合唯一索引更安全）
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                // 今日筆數 + 1（簡易法）
                var todayCount = await _db.RegistrationDetails
                    .CountAsync(x => x.RegistrationNum.StartsWith(prefix));

                entity.RegistrationNum = $"{prefix}{(todayCount + 1):000}";
                _db.RegistrationDetails.Add(entity);

                try
                {
                    await _db.SaveChangesAsync();
                    return Ok(new RegistrationResDto
                    {
                        RegistrationId = entity.RegistrationId,
                        RegistrationNum = entity.RegistrationNum
                    });
                }
                catch (DbUpdateException)
                {
                    // 可能撞到唯一索引（高併發），重試
                    _db.Entry(entity).State = EntityState.Detached;

                    if (attempt == maxAttempts)
                        return StatusCode(409, new { message = "產生報名編號失敗，請稍後再試。" });

                    await Task.Delay(Random.Shared.Next(10, 50));
                    // 重新 new，避免追蹤狀態殘留
                    entity = new RegistrationDetail
                    {
                        EventBatchId = dto.EventBatchId,
                        MemberId = dto.MemberId,
                        AmountDue = dto.AmountDue,
                        RegistrationDateTime = now,
                        CurrentStatus = dto.CurrentStatus,
                        InternalRemarks = dto.InternalRemarks
                    };
                }
            }

            return StatusCode(500, new { message = "未知錯誤" });
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

        //// 顯示指定會員的報名資料
        //// GET /api/EventRegistration/memberid/{memberId}
        //[HttpGet("memberId/{memberId:int}")]
        //public async Task<IActionResult> List(int memberId)
        //{
        //    var query = from r in _db.RegistrationDetails.AsNoTracking()
        //                join b in _db.EventBatches.AsNoTracking()
        //                    on r.EventBatchId equals b.BatchId
        //                join e in _db.EventTemplates.AsNoTracking()
        //                    on b.EventId equals e.EventId
        //                where r.MemberId == memberId     
        //                orderby r.RegistrationId
        //                select new RegistrationListDto
        //                {
        //                    RegistrationId = r.RegistrationId,
        //                    RegistrationNum = r.RegistrationNum,
        //                    EventBatchId = r.EventBatchId,
        //                    MemberId = r.MemberId,
        //                    AmountDue = r.AmountDue,
        //                    RegistrationDateTime = r.RegistrationDateTime,
        //                    CurrentStatus = r.CurrentStatus,
        //                    InternalRemarks = r.InternalRemarks,
        //                    EventName = e.EventName,               // 活動名稱
        //                    EventDateTimeStart = b.EventDateTimeStart, // 活動時間
        //                    EventLocation = e.EventLocation         // 活動地點
        //                };

        //    var items = await query.ToListAsync();
        //    return Ok(items);
        //}

        // 顯示報名資料 (需給予使用者id，可選批次id與狀態)
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

    }
}


//測試資料
//    {
//  "eventBatchId": 1001,
//  "memberId": 15,
//  "amountDue": 300,
//  "registrationDateTime": "2025-08-17T10:00:00",
//  "currentStatus": 1,
//  "internalRemarks": "前台報名"
//}