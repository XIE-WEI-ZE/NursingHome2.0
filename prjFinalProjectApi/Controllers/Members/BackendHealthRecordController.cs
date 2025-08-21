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
    }
}
