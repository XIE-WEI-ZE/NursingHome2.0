using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Helpers;     // User.EmployeeId()
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;  // EmployeeApprovalDetailDto, EmployeeApprovalLogDto, EmployeeApproveDecisionDto
using prjFinalProjectApi.Services;    // EmployeeApprovalFlowService

namespace prjFinalProjectApi.Controllers.Employee
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeeApprovalsController : ControllerBase
    {
        private readonly DbNursingHomeContext _ctx;
        private readonly EmployeeApprovalFlowService _flow;

        public EmployeeApprovalsController(DbNursingHomeContext ctx, EmployeeApprovalFlowService flow)
        {
            _ctx = ctx;
            _flow = flow;
        }

        // 對外統一碼
        private const string SWaiting = "Waiting";
        private const string SApproved = "Approved";
        private const string SRejected = "Rejected";
        private const string STerminated = "Terminated";
        private const string SCompleted = "Completed";
        private const string SCancelled = "Cancelled";

        // DB 可能存在的舊值
        private const string DB_Waiting = "待簽核";
        private const string DB_Queued = "排隊中";
        private const string DB_Review = "審核中";

        private static string MapToCode(string? raw)
        {
            var s = (raw ?? "").Trim();
            return s switch
            {
                SApproved => SApproved,
                SRejected => SRejected,
                STerminated => STerminated,
                SCompleted => SCompleted,
                SCancelled => SCancelled,
                DB_Waiting => SWaiting,
                DB_Queued => SWaiting,
                DB_Review => SWaiting,
                _ => string.IsNullOrEmpty(s) ? SWaiting : s
            };
        }

        // 1) 我的待辦（可用 formType 過濾）
        [HttpGet("my-todo")]
        public async Task<ActionResult<IEnumerable<object>>> MyTodo([FromQuery] string? formType)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized();

            var q = _ctx.EmployeeApprovalLogs.AsNoTracking()
                    .Where(l => l.ApproverId == me && MapToCode(l.ApproveStatus) == SWaiting);

            if (!string.IsNullOrWhiteSpace(formType))
                q = q.Where(l => l.FormType == formType);

            var rows = await q
                .OrderBy(l => l.StepNumber)
                .ThenBy(l => l.ApproveDate)
                .Select(l => new
                {
                    l.ApprovalId,
                    l.FormType,
                    FormId = l.FormId ?? 0,
                    l.StepNumber,
                    l.StepName
                })
                .ToListAsync();

            return Ok(rows);
        }

        // 2) 決策：Approved / Rejected
        [HttpPost("{approvalId:int}/decide")]
        public async Task<IActionResult> Decide(int approvalId, [FromBody] EmployeeApproveDecisionDto dto)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized();
            if (dto == null || string.IsNullOrWhiteSpace(dto.Decision))
                return BadRequest("decision 必填");

            var decision = dto.Decision.Equals(SApproved, StringComparison.OrdinalIgnoreCase) ? SApproved
                         : dto.Decision.Equals(SRejected, StringComparison.OrdinalIgnoreCase) ? SRejected : "";

            if (string.IsNullOrEmpty(decision))
                return BadRequest("decision 僅支援 Approved / Rejected");

            await _flow.DecideAsync(approvalId, me, decision, dto.Comment);
            return Ok();
        }

        // 3) 明細（Leave / MissingPunch）
        [HttpGet("{formType}/{formId:int}")]
        public async Task<ActionResult<EmployeeApprovalDetailDto>> GetDetail(string formType, int formId)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized();

            var header = await GetHeaderAsync(formType, formId);
            if (header == null) return NotFound("找不到申請單");

            var logs = await _ctx.EmployeeApprovalLogs
                .AsNoTracking()
                .Where(x => x.FormType == formType && x.FormId == formId)
                .OrderBy(x => x.StepNumber)
                .Select(x => new EmployeeApprovalLogDto
                {
                    ApprovalId = x.ApprovalId,
                    StepName = x.StepName ?? "",
                    Role = _ctx.EmployeeApprovalFlowTemplates
                                       .Where(t => t.FormType == x.FormType && t.StepNumber == x.StepNumber)
                                       .Select(t => t.Role).FirstOrDefault() ?? "",
                    ApproverName = _ctx.Employees
                                       .Where(e => e.EmployeeId == x.ApproverId)
                                       .Select(e => e.Name).FirstOrDefault() ?? "",
                    ApproveStatus = MapToCode(x.ApproveStatus),
                    ApproveComment = x.ApproveComment,
                    ApproveDate = x.ApproveDate
                })
                .ToListAsync();

            var myApprovalId = await _ctx.EmployeeApprovalLogs
                .AsNoTracking()
                .Where(x => x.FormType == formType
                         && x.FormId == formId
                         && x.ApproverId == me
                         && MapToCode(x.ApproveStatus) == SWaiting)
                .OrderBy(x => x.StepNumber)
                .Select(x => x.ApprovalId)
                .FirstOrDefaultAsync();

            bool hasPending = logs.Any(l => l.ApproveStatus == SWaiting);
            bool canCancel = (me == (header.ApplicantEmployeeId ?? 0)) && hasPending;

            var (st, et, cleanReason) = ExtractTimes(header.Reason);

            var dto = new EmployeeApprovalDetailDto
            {
                ApprovalId = myApprovalId,
                FormType = formType,
                FormId = formId,
                ApplicantName = header.ApplicantName ?? "",
                ApplyDate = header.ApplyDate,

                LeaveTypeName = header.LeaveTypeName,
                StartDate = header.StartDate,
                EndDate = header.EndDate,
                StartTime = st,
                EndTime = et,
                LeaveHours = header.LeaveHours,

                Reason = cleanReason ?? header.Reason,
                Logs = logs,
                CanCancel = canCancel
            };

            return Ok(dto);
        }

        // 4) 申請人抽單
        [HttpPut("cancel/{formType}/{formId:int}")]
        public async Task<IActionResult> Cancel(string formType, int formId)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized();

            var header = await GetHeaderAsync(formType, formId);
            if (header == null) return NotFound("找不到申請單");
            if ((header.ApplicantEmployeeId ?? 0) != me) return Forbid("僅申請人可抽單");

            var anyWaiting = await _ctx.EmployeeApprovalLogs.AsNoTracking()
                .AnyAsync(l => l.FormType == formType && l.FormId == formId &&
                              (MapToCode(l.ApproveStatus) == SWaiting));
            if (!anyWaiting) return BadRequest("流程已結案，無法抽單");

            await SetHeaderStatusAsync(formType, formId, SCancelled);

            var remains = await _ctx.EmployeeApprovalLogs
                .Where(l => l.FormType == formType && l.FormId == formId &&
                           (MapToCode(l.ApproveStatus) == SWaiting))
                .ToListAsync();

            foreach (var r in remains)
            {
                r.ApproveStatus = STerminated;
                r.ApproveComment = "(流程已抽單)";
                r.ApproveDate = DateTime.Now;
            }

            await _ctx.SaveChangesAsync();
            return NoContent();
        }

        // ── 工具：抓各表單 header ─────────────────────────────
        private async Task<FormHeader?> GetHeaderAsync(string formType, int formId)
        {
            if (formType.Equals("Leave", StringComparison.OrdinalIgnoreCase))
            {
                return await (
                    from h in _ctx.EmployeeLeaveApplications.AsNoTracking()
                    where h.LeaveId == formId
                    let emp = _ctx.Employees.FirstOrDefault(e => e.EmployeeId == h.EmployeeId)
                    let lt = _ctx.EmployeeLeaveTypes.FirstOrDefault(t => t.LeaveTypeId == h.LeaveTypeId)
                    select new FormHeader
                    {
                        ApplicantEmployeeId = h.EmployeeId,
                        ApplicantName = emp != null ? (emp.Name ?? "") : "",
                        ApplyDate = h.ApplyDate,
                        LeaveTypeName = lt != null ? lt.TypeName : null,
                        StartDate = h.StartDate.HasValue ? h.StartDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                        EndDate = h.EndDate.HasValue ? h.EndDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                        LeaveHours = h.LeaveHours,
                        Reason = h.Reason
                    }
                ).FirstOrDefaultAsync();
            }

            if (formType.Equals("MissingPunch", StringComparison.OrdinalIgnoreCase))
            {
                return await (
                    from a in _ctx.EmployeeMissingPunchApplications.AsNoTracking()
                    where a.ApplicationId == formId
                    let emp = _ctx.Employees.FirstOrDefault(e => e.EmployeeId == a.EmployeeId)
                    select new FormHeader
                    {
                        ApplicantEmployeeId = a.EmployeeId,
                        ApplicantName = emp != null ? (emp.Name ?? "") : "",
                        ApplyDate = a.ApplyDate,
                        // MissingPunch 沒有 LeaveType/Hours/Start/End
                        Reason = a.ApplyReason
                    }
                ).FirstOrDefaultAsync();
            }

            return null;
        }

        private async Task SetHeaderStatusAsync(string formType, int formId, string finalStatus)
        {
            if (formType.Equals("Leave", StringComparison.OrdinalIgnoreCase))
            {
                var h = await _ctx.EmployeeLeaveApplications.FirstOrDefaultAsync(x => x.LeaveId == formId);
                if (h != null)
                {
                    h.Status = finalStatus;
                    if (finalStatus is SApproved or SCompleted) h.ApprovedDate = DateTime.Now;
                    await _ctx.SaveChangesAsync();
                }
                return;
            }

            if (formType.Equals("MissingPunch", StringComparison.OrdinalIgnoreCase))
            {
                var a = await _ctx.EmployeeMissingPunchApplications.FirstOrDefaultAsync(x => x.ApplicationId == formId);
                if (a != null)
                {
                    a.Status = finalStatus;
                    if (finalStatus is SApproved or SCompleted) a.ApprovedDate = DateTime.Now;
                    await _ctx.SaveChangesAsync();
                }
            }
        }

        // 解析 [T:hh:mm-hh:mm]
        private static readonly Regex TimeTag =
            new(@"\[\s*T:(\d{2}):(\d{2})-(\d{2}):(\d{2})\]\s*", RegexOptions.Compiled);

        private static (string? st, string? et, string? clean) ExtractTimes(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return (null, null, null);
            var m = TimeTag.Match(reason!);
            if (!m.Success) return (null, null, reason!.Trim());
            var st = $"{m.Groups[1].Value}:{m.Groups[2].Value}";
            var et = $"{m.Groups[3].Value}:{m.Groups[4].Value}";
            var clean = TimeTag.Replace(reason!, "").Trim();
            return (st, et, clean);
        }

        private sealed class FormHeader
        {
            public int? ApplicantEmployeeId { get; set; }
            public string? ApplicantName { get; set; }
            public DateTime? ApplyDate { get; set; }

            public string? LeaveTypeName { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public decimal? LeaveHours { get; set; }

            public string? Reason { get; set; }
        }
    }
}
