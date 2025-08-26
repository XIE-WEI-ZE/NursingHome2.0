// Controllers/EmployeeAttendanceLogsController.cs
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class EmployeeAttendanceLogsController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public EmployeeAttendanceLogsController(DbNursingHomeContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 讀取今天的打卡彙總：上班＝「最新一次ClockIn」、下班＝「最新一次ClockOut」
        /// </summary>
        [HttpGet("today")]
        public async Task<ActionResult<EmployeeAttendanceDto>> GetToday()
        {
            var employeeId = GetEmployeeIdFromClaims();
            var today = DateOnly.FromDateTime(DateTime.Now.Date);

            var logs = await _context.EmployeeAttendanceLogs
                .Where(x => x.EmployeeId == employeeId && x.WorkDate == today)
                .ToListAsync();

            // 取「最新一次」而不是最早
            DateTime? latestIn = logs.Where(x => x.ClockInTime.HasValue)
                                     .OrderByDescending(x => x.ClockInTime)
                                     .Select(x => x.ClockInTime)
                                     .FirstOrDefault();

            DateTime? latestOut = logs.Where(x => x.ClockOutTime.HasValue)
                                      .OrderByDescending(x => x.ClockOutTime)
                                      .Select(x => x.ClockOutTime)
                                      .FirstOrDefault();

            var dto = new EmployeeAttendanceDto
            {
                AttendanceId = null,
                EmployeeId = employeeId,
                WorkDate = today.ToDateTime(TimeOnly.MinValue), // DTO 用 DateTime
                ClockInTime = latestIn,
                ClockOutTime = latestOut,
                Status = BuildStatus(latestIn, latestOut),
                // 你前端不再用這兩個控制按鈕，就全部開放
                CanClockIn = true,
                CanClockOut = true
            };

            return Ok(dto);
        }

        /// <summary>
        /// 上班打卡（每按一次就新增一筆）
        /// </summary>
        [HttpPost("clock-in")]
        public async Task<ActionResult<EmployeeAttendanceDto>> ClockIn()
        {
            var employeeId = GetEmployeeIdFromClaims();
            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now.Date);

            var row = new EmployeeAttendanceLog
            {
                EmployeeId = employeeId,
                WorkDate = today,
                ClockInTime = now,
                ClockOutTime = null,
                Status = "上班"
            };

            _context.EmployeeAttendanceLogs.Add(row);
            await _context.SaveChangesAsync();

            return await GetToday();
        }

        /// <summary>
        /// 下班打卡（每按一次就新增一筆）
        /// </summary>
        [HttpPost("clock-out")]
        public async Task<ActionResult<EmployeeAttendanceDto>> ClockOut()
        {
            var employeeId = GetEmployeeIdFromClaims();
            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now.Date);

            var row = new EmployeeAttendanceLog
            {
                EmployeeId = employeeId,
                WorkDate = today,
                ClockInTime = null,
                ClockOutTime = now,
                Status = "下班"
            };

            _context.EmployeeAttendanceLogs.Add(row);
            await _context.SaveChangesAsync();

            return await GetToday();
        }

        private static string? BuildStatus(DateTime? clockIn, DateTime? clockOut)
        {
            if (clockIn == null && clockOut == null) return null;
            if (clockIn != null && clockOut == null) return "Working";
            if (clockIn == null && clockOut != null) return "ClockedOutOnly";
            return "Completed";
        }

        // 從 JWT Claims 取出 EmployeeId
        private int GetEmployeeIdFromClaims()
        {
            var id =
                User.FindFirstValue("employeeid") ??
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub");

            if (int.TryParse(id, out var empId))
                return empId;

            throw new UnauthorizedAccessException("缺少 EmployeeId claims。");
        }
    }
}
