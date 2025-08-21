using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dtos;

namespace prjFinalProjectApi.Controllers.Backend
{
    [Route("api/backend/health-record")]
    [ApiController]
    public class BackendHealthRecordController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public BackendHealthRecordController(DbNursingHomeContext context)
        {
            _context = context;
        }

        //  新增一筆健康紀錄
        [HttpPost]
        public async Task<IActionResult> CreateRecord([FromBody] HealthRecordDto dto, [FromQuery] int memberId)
        {
            try
            {
                if (dto.RecordDate == null)
                    return BadRequest(new { message = "請提供紀錄日期" });

                var memberExists = await _context.Members.AnyAsync(m => m.FMemberId == memberId);
                if (!memberExists)
                    return NotFound(new { message = "找不到此會員" });

                var record = new MemberDailyHealthRecord
                {
                    FMemberId = memberId,
                    FRecordDate = DateOnly.FromDateTime(dto.RecordDate.Value),
                    FSystolic = dto.Systolic,
                    FDiastolic = dto.Diastolic,
                    FPulse = dto.Pulse,
                    FIorecord = dto.IORecord,        // 
                    FCheckPeriod = dto.CheckPeriod,
                    FNotes = dto.Notes,
                    FCreatedAt = DateTime.Now
                };

                _context.MemberDailyHealthRecords.Add(record);
                await _context.SaveChangesAsync();

                return Ok(new { message = "新增健康紀錄成功" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "新增失敗", error = ex.Message });
            }
        }

        //  查詢近 7 天健康紀錄（依會員 ID）
        [HttpGet("by-member/{memberId}")]
        public async Task<IActionResult> GetRecentRecords(int memberId)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var startDate = today.AddDays(-6); // 含今天共七天

            var records = await _context.MemberDailyHealthRecords
                .Where(r => r.FMemberId == memberId && r.FRecordDate >= startDate)
                .OrderByDescending(r => r.FRecordDate)
                .Select(r => new
                {
                    Id = r.FId,
                    RecordDate = r.FRecordDate.HasValue
                    ? r.FRecordDate.Value.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd")
                    : null,
                    Systolic = r.FSystolic,
                    Diastolic = r.FDiastolic,
                    Pulse = r.FPulse,
                    IORecord = r.FIorecord,
                    CheckPeriod = r.FCheckPeriod,
                    Notes = r.FNotes
                })
                .ToListAsync();

            return Ok(records);
        }


        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRecord(int id, [FromBody] HealthRecordDto dto)
        {
            try
            {
                var record = await _context.MemberDailyHealthRecords.FirstOrDefaultAsync(r => r.FId == id);
                if (record == null)
                    return NotFound(new { message = "查無此紀錄" });
                if (dto.RecordDate != null)
                    record.FRecordDate = DateOnly.FromDateTime(dto.RecordDate.Value);
                record.FSystolic = dto.Systolic;
                record.FDiastolic = dto.Diastolic;
                record.FPulse = dto.Pulse;
                record.FIorecord = dto.IORecord;
                record.FCheckPeriod = dto.CheckPeriod;
                record.FNotes = dto.Notes;

                await _context.SaveChangesAsync();

                return Ok(new { message = "健康紀錄已經更新" });
            }
            catch (Exception ex) {
                return StatusCode(500, new { message = "更新失敗", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRecord(int id)
        {
            var record = await _context.MemberDailyHealthRecords.FindAsync(id);
            if (record == null)
                return NotFound(new { message = "找不到資料" });


            _context.MemberDailyHealthRecords.Remove(record);
            await _context.SaveChangesAsync();


            return Ok(new { message = "資料已刪除" });
        }

        // GET: /api/backend/health-record/list?memberId=7&page=1&pageSize=10&dateFrom=2025-08-01&dateTo=2025-08-31
        [HttpGet("list")]
        public async Task<IActionResult> List(
            [FromQuery] int memberId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? dateFrom = null,
            [FromQuery] string? dateTo = null)
        {
            if (memberId <= 0) return BadRequest(new { message = "memberId 必填" });

            var q = _context.MemberDailyHealthRecords.AsQueryable()
                .Where(r => r.FMemberId == memberId);

            // 日期區間（可選）
            if (!string.IsNullOrWhiteSpace(dateFrom) && DateOnly.TryParse(dateFrom, out var df))
                q = q.Where(r => r.FRecordDate >= df);

            if (!string.IsNullOrWhiteSpace(dateTo) && DateOnly.TryParse(dateTo, out var dt))
                q = q.Where(r => r.FRecordDate <= dt);

            var totalCount = await q.CountAsync();

            var items = await q
                .OrderByDescending(r => r.FRecordDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    id = r.FId,
                    recordDate = r.FRecordDate.HasValue
                        ? r.FRecordDate.Value.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd")
                        : null,
                    systolic = r.FSystolic,
                    diastolic = r.FDiastolic,
                    pulse = r.FPulse,
                    ioRecord = r.FIorecord,
                    checkPeriod = r.FCheckPeriod,
                    notes = r.FNotes
                })
                .ToListAsync();

            return Ok(new { totalCount, page, pageSize, items });
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetOne(int id)
        {
            var r = await _context.MemberDailyHealthRecords
                .FirstOrDefaultAsync(x => x.FId == id);
            if (r == null) return NotFound(new { message = "查無資料" });

            return Ok(new
            {
                id = r.FId,
                recordDate = r.FRecordDate?.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd"),
                systolic = r.FSystolic,
                diastolic = r.FDiastolic,
                pulse = r.FPulse,
                ioRecord = r.FIorecord,
                checkPeriod = r.FCheckPeriod,
                notes = r.FNotes
            });
        }

        [HttpGet("by-member/{memberId:int}/by-date/{date}")]
        public async Task<IActionResult> GetByMemberAndDate(int memberId, string date)
        {
            if (!DateOnly.TryParse(date, out var d))
                return BadRequest(new { message = "日期格式需 yyyy-MM-dd" });

            var r = await _context.MemberDailyHealthRecords
                .FirstOrDefaultAsync(x => x.FMemberId == memberId && x.FRecordDate == d);

            if (r == null) return NotFound(new { message = "當日無紀錄" });

            return Ok(new
            {
                id = r.FId,
                recordDate = r.FRecordDate?.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd"),
                systolic = r.FSystolic,
                diastolic = r.FDiastolic,
                pulse = r.FPulse,
                ioRecord = r.FIorecord,
                checkPeriod = r.FCheckPeriod,
                notes = r.FNotes
            });
        }

    }
}
