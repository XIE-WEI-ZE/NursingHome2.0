using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Helpers;          // User.EmployeeId()
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;      // Employee Dto, MissingPunchDetailDto, EmployeeApprovalLogDto
using prjFinalProjectApi.Services;        // EmployeeApprovalFlowService

namespace prjFinalProjectApi.Controllers.Employee
{
    [Route("api/[controller]")]
    [ApiController]
    [Produces("application/json")]
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeeMissingPunchApplicationsController : ControllerBase
    {
        private readonly DbNursingHomeContext _ctx;
        private readonly EmployeeApprovalFlowService _flow;

        private const string FormType = "MissingPunch";
        private const string SWaiting = "Waiting";

        public EmployeeMissingPunchApplicationsController(
            DbNursingHomeContext ctx,
            EmployeeApprovalFlowService flow)
        {
            _ctx = ctx;
            _flow = flow;
        }

        /// <summary>新增忘記打卡單並啟動簽核流程</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] EmployeeMissingPunchDto dto)
        {
            if (dto == null) return BadRequest("Body is required.");

            var empId = User.EmployeeId();
            if (empId == 0) return Unauthorized();

            // 組 RequestedTime（容錯：若只傳了時間或日期是預設值，合併 WorkDate 的日期）
            var workDateOnly = DateOnly.FromDateTime(dto.WorkDate);
            var req = dto.RequestedTime;

            // 若 RequestedTime 的日期看起來是預設（0001-01-01）或與 WorkDate 不同，就用 WorkDate + 時間
            if (req.Date == DateTime.MinValue.Date || req.Date != dto.WorkDate.Date)
            {
                req = workDateOnly.ToDateTime(TimeOnly.FromTimeSpan(dto.RequestedTime.TimeOfDay));
            }

            using var tx = await _ctx.Database.BeginTransactionAsync();
            try
            {
                var app = new EmployeeMissingPunchApplication
                {
                    EmployeeId = empId,
                    WorkDate = workDateOnly,                       // DB: date (允許 null，但這裡給值)
                    MissingType = dto.MissingType,
                    ApplyReason = dto.ApplyReason,
                    RequestedTime = req,                           // DB: datetime
                    Status = SWaiting,
                    ApplyDate = DateTime.Now
                };

                _ctx.EmployeeMissingPunchApplications.Add(app);
                await _ctx.SaveChangesAsync();

                // 依樣板建立簽核節點（DeptSupervisor → HR）
                await _flow.CreateFlowForAsync(FormType, app.ApplicationId, empId);

                await tx.CommitAsync();

                // 201 + id
                return CreatedAtAction(nameof(GetDetail), new { id = app.ApplicationId }, new { applicationID = app.ApplicationId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Problem($"Create application failed: {ex.Message}");
            }
        }
        [HttpGet("me-name")]
        public async Task<ActionResult<string>> MeName()
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized();

            var name = await _ctx.Employees
        .Where(e => e.EmployeeId == me)              // 若是 nullable: e.EmployeeId.HasValue && e.EmployeeId.Value == me
        .Select(e => (e.Name ?? "").Trim())
        .FirstOrDefaultAsync();

            return Ok(name);
        }
        /// <summary>目前登入者的忘卡申請清單</summary>
        [HttpGet("my")]
        public async Task<IActionResult> GetMy()
        {
            var empId = User.EmployeeId();
            if (empId == 0) return Unauthorized();

            var list = await _ctx.EmployeeMissingPunchApplications
                .AsNoTracking()
                .Where(a => a.EmployeeId == empId)
                .OrderByDescending(a => a.ApplyDate)
                .Select(a => new
                {
                    a.ApplicationId,
                    // ★ WorkDate 可為 null → 回傳 DateTime?
                    WorkDate = a.WorkDate.HasValue
                        ? a.WorkDate.Value.ToDateTime(TimeOnly.MinValue)
                        : (DateTime?)null,
                    a.MissingType,
                    a.RequestedTime,
                    a.Status,
                    a.ApplyDate,
                    a.ApprovedDate
                })
                .ToListAsync();

            return Ok(list);
        }

        /// <summary>單筆明細 + 流程節點</summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<MissingPunchDetailDto>> GetDetail(int id)
        {
            var app = await _ctx.EmployeeMissingPunchApplications
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ApplicationId == id);

            if (app == null) return NotFound();

            var logs = await _ctx.EmployeeApprovalLogs
                .AsNoTracking()
                .Where(l => l.FormType == FormType && l.FormId == id)
                .OrderBy(l => l.StepNumber)
                .Select(l => new EmployeeApprovalLogDto
                {
                    ApprovalId = l.ApprovalId,
                    StepName = l.StepName ?? "",
                    Role = _ctx.EmployeeApprovalFlowTemplates
                                .Where(t => t.FormType == l.FormType && t.StepNumber == l.StepNumber)
                                .Select(t => t.Role).FirstOrDefault() ?? "",
                    ApproverName = _ctx.Employees
                                .Where(e => e.EmployeeId == l.ApproverId)
                                .Select(e => e.Name).FirstOrDefault() ?? "",
                    ApproveStatus = l.ApproveStatus ?? "Waiting",
                    ApproveComment = l.ApproveComment,
                    ApproveDate = l.ApproveDate
                })
                .ToListAsync();

            var result = new MissingPunchDetailDto
            {
                ApplicationID = app.ApplicationId,
                EmployeeID = app.EmployeeId ?? 0,
                // ★ 這裡也用 DateTime?
                WorkDate = app.WorkDate.HasValue
                    ? app.WorkDate.Value.ToDateTime(TimeOnly.MinValue)
                    : (DateTime?)null,
                MissingType = app.MissingType ?? "",
                ApplyReason = app.ApplyReason,
                RequestedTime = app.RequestedTime,
                Status = app.Status ?? "",
                ApplyDate = app.ApplyDate ?? DateTime.MinValue,
                ApprovedDate = app.ApprovedDate,
                Logs = logs
            };

            return Ok(result);
        }
    }
}
