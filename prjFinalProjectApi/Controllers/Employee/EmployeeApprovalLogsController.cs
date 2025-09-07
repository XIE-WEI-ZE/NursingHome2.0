using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Helpers;                // User.EmployeeId()
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;            // EmployeeApprovalInboxItemDto / EmployeeMyRequestItemDto / EmployeeApprovalDetailDto / EmployeeApprovalLogDto / EmployeeApprovalActionDto
using prjFinalProjectApi.Services;              // EmployeeApprovalFlowService

namespace prjFinalProjectApi.Controllers.Employee
{
    [Route("api/[controller]")]
    [ApiController]
    [Produces("application/json")]
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeeApprovalLogsController : ControllerBase
    {
        private readonly DbNursingHomeContext _ctx;
        private readonly EmployeeApprovalFlowService _flow;

        public EmployeeApprovalLogsController(DbNursingHomeContext ctx, EmployeeApprovalFlowService flow)
        {
            _ctx = ctx;
            _flow = flow;
        }

        // ─────────────────── 對外統一狀態碼（含舊值容錯）
        private const string SWaiting = "Waiting";
        private const string SApproved = "Approved";
        private const string SRejected = "Rejected";
        private const string STerminated = "Terminated";
        private const string SCancelled = "Cancelled";
        private const string SCompleted = "Completed";

        private const string DB_Waiting = "待簽核";
        private const string DB_Queued = "排隊中";
        private const string DB_Review = "審核中";

        private static string ToCode(string? raw)
        {
            var s = (raw ?? "").Trim();
            return s switch
            {
                "Approved" => SApproved,
                "Completed" => SCompleted,
                "Rejected" => SRejected,
                "Terminated" => STerminated,
                "Cancelled" => SCancelled,

                DB_Waiting => SWaiting,
                DB_Queued => SWaiting,
                DB_Review => SWaiting,

                "" => SWaiting,
                _ => SWaiting
            };
        }

        // ─────────────────── 1) 待我簽核（只回 Waiting）
        // GET: api/EmployeeApprovalLogs/inbox?formType=Leave|MissingPunch
        [HttpGet("inbox")]
        public async Task<ActionResult<IEnumerable<EmployeeApprovalInboxItemDto>>> Inbox([FromQuery] string? formType)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized("找不到員工身分");

            var q = _ctx.EmployeeApprovalLogs.AsNoTracking()
                .Where(l => l.ApproverId == me &&
                            (l.ApproveStatus == DB_Waiting || l.ApproveStatus == SWaiting));

            if (!string.IsNullOrWhiteSpace(formType))
                q = q.Where(l => l.FormType == formType);

            var triples = await q
                .OrderBy(l => l.StepNumber)
                .Select(l => new { l.FormType, FormId = l.FormId ?? 0, l.ApprovalId })
                .ToListAsync();

            var items = await BuildInboxLikeRowsAsync(triples, isInbox: true);
            return Ok(items.OrderByDescending(r => r.ReceivedAt ?? DateTime.MinValue).ToList());
        }

        // ─────────────────── 2) 我處理過的（Waiting/Approved/Rejected…）
        // GET: api/EmployeeApprovalLogs/mine?formType=Leave|MissingPunch
        [HttpGet("mine")]
        public async Task<ActionResult<IEnumerable<EmployeeApprovalInboxItemDto>>> Mine([FromQuery] string? formType)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized("找不到員工身分");

            var q = _ctx.EmployeeApprovalLogs.AsNoTracking().Where(l => l.ApproverId == me);

            if (!string.IsNullOrWhiteSpace(formType))
                q = q.Where(l => l.FormType == formType);

            var triples = await q
                .OrderByDescending(l => l.ApproveDate)
                .ThenBy(l => l.StepNumber)
                .Select(l => new { l.FormType, FormId = l.FormId ?? 0, l.ApprovalId })
                .ToListAsync();

            var items = await BuildInboxLikeRowsAsync(triples, isInbox: false);
            return Ok(items.OrderByDescending(r => r.ReceivedAt ?? DateTime.MinValue).ToList());
        }

        // ─────────────────── 3) 我送出的申請（彙總狀態）
        // GET: api/EmployeeApprovalLogs/my?formType=Leave|MissingPunch
        [HttpGet("my")]
        public async Task<ActionResult<IEnumerable<EmployeeMyRequestItemDto>>> My([FromQuery] string? formType)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized("找不到員工身分");

            var rows = new List<EmployeeMyRequestItemDto>();

            async Task CollectLeaveAsync()
            {
                var headers = await (
                    from h in _ctx.EmployeeLeaveApplications.AsNoTracking()
                    where h.EmployeeId == me
                    select new
                    {
                        FormId = h.LeaveId,
                        AppliedAt = h.ApplyDate,
                        HeaderStatus = ToCode(h.Status ?? DB_Review),
                        DeptId = _ctx.Employees.Where(e => e.EmployeeId == h.EmployeeId).Select(e => e.DepartmentId).FirstOrDefault(),
                        JobTitleId = _ctx.Employees.Where(e => e.EmployeeId == h.EmployeeId).Select(e => e.JobTitleId).FirstOrDefault()
                    }
                ).ToListAsync();

                if (headers.Count == 0) return;

                var ids = headers.Select(x => x.FormId).ToList();
                var logs = await _ctx.EmployeeApprovalLogs.AsNoTracking()
                    .Where(l => l.FormType == "Leave" && ids.Contains(l.FormId ?? 0))
                    .Select(l => new { l.FormId, l.StepNumber, l.StepName, l.ApproveStatus, l.ApproverId })
                    .ToListAsync();

                foreach (var h in headers)
                {
                    var thisLogs = logs.Where(l => (l.FormId ?? 0) == h.FormId).ToList();

                    string overall =
                        h.HeaderStatus == SCancelled ? SCancelled :
                        thisLogs.Any(l => ToCode(l.ApproveStatus!) == SRejected) ? SRejected :
                        thisLogs.Any(l => ToCode(l.ApproveStatus!) == SWaiting) ? "Pending" :
                        SApproved;

                    var waitingStep = thisLogs
                        .Where(l => ToCode(l.ApproveStatus!) == SWaiting)
                        .OrderBy(l => l.StepNumber)
                        .FirstOrDefault();

                    var waitingApprovers = new List<string>();
                    if (waitingStep != null)
                    {
                        var ids2 = thisLogs
                            .Where(l => ToCode(l.ApproveStatus!) == SWaiting && l.StepNumber == waitingStep.StepNumber)
                            .Select(l => l.ApproverId ?? 0).Where(i => i != 0).Distinct().ToList();

                        if (ids2.Count > 0)
                            waitingApprovers = await _ctx.Employees.AsNoTracking()
                                .Where(e => ids2.Contains(e.EmployeeId))
                                .Select(e => e.Name ?? "")
                                .ToListAsync();
                    }

                    var deptName = await _ctx.EmployeeDepartments.AsNoTracking()
                        .Where(d => d.DepartmentId == h.DeptId).Select(d => d.DepartmentName ?? "").FirstOrDefaultAsync() ?? "";

                    var jobName = await _ctx.EmployeeJobTitles.AsNoTracking()
                        .Where(j => j.JobTitleId == h.JobTitleId).Select(j => j.TitleName ?? "").FirstOrDefaultAsync() ?? "";

                    rows.Add(new EmployeeMyRequestItemDto
                    {
                        FormType = "Leave",
                        FormId = h.FormId,
                        AppliedAt = h.AppliedAt,
                        Status = overall,
                        CurrentStepNumber = waitingStep?.StepNumber,
                        CurrentStepName = waitingStep?.StepName,
                        WaitingApprovers = waitingApprovers,
                        DepartmentName = deptName,
                        JobTitleName = jobName
                    });
                }
            }

            async Task CollectMissingPunchAsync()
            {
                var headers = await (
                    from a in _ctx.EmployeeMissingPunchApplications.AsNoTracking()
                    where (a.EmployeeId ?? 0) == me
                    select new
                    {
                        FormId = a.ApplicationId,
                        AppliedAt = a.ApplyDate,
                        HeaderStatus = ToCode(a.Status ?? DB_Review),
                        DeptId = _ctx.Employees.Where(e => e.EmployeeId == a.EmployeeId).Select(e => e.DepartmentId).FirstOrDefault(),
                        JobTitleId = _ctx.Employees.Where(e => e.EmployeeId == a.EmployeeId).Select(e => e.JobTitleId).FirstOrDefault()
                    }
                ).ToListAsync();

                if (headers.Count == 0) return;

                var ids = headers.Select(x => x.FormId).ToList();
                var logs = await _ctx.EmployeeApprovalLogs.AsNoTracking()
                    .Where(l => l.FormType == "MissingPunch" && ids.Contains(l.FormId ?? 0))
                    .Select(l => new { l.FormId, l.StepNumber, l.StepName, l.ApproveStatus, l.ApproverId })
                    .ToListAsync();

                foreach (var h in headers)
                {
                    var thisLogs = logs.Where(l => (l.FormId ?? 0) == h.FormId).ToList();

                    string overall =
                        h.HeaderStatus == SCancelled ? SCancelled :
                        thisLogs.Any(l => ToCode(l.ApproveStatus!) == SRejected) ? SRejected :
                        thisLogs.Any(l => ToCode(l.ApproveStatus!) == SWaiting) ? "Pending" :
                        SApproved;

                    var waitingStep = thisLogs
                        .Where(l => ToCode(l.ApproveStatus!) == SWaiting)
                        .OrderBy(l => l.StepNumber)
                        .FirstOrDefault();

                    var waitingApprovers = new List<string>();
                    if (waitingStep != null)
                    {
                        var ids2 = thisLogs
                            .Where(l => ToCode(l.ApproveStatus!) == SWaiting && l.StepNumber == waitingStep.StepNumber)
                            .Select(l => l.ApproverId ?? 0).Where(i => i != 0).Distinct().ToList();

                        if (ids2.Count > 0)
                            waitingApprovers = await _ctx.Employees.AsNoTracking()
                                .Where(e => ids2.Contains(e.EmployeeId))
                                .Select(e => e.Name ?? "")
                                .ToListAsync();
                    }

                    var deptName = await _ctx.EmployeeDepartments.AsNoTracking()
                        .Where(d => d.DepartmentId == h.DeptId).Select(d => d.DepartmentName ?? "").FirstOrDefaultAsync() ?? "";

                    var jobName = await _ctx.EmployeeJobTitles.AsNoTracking()
                        .Where(j => j.JobTitleId == h.JobTitleId).Select(j => j.TitleName ?? "").FirstOrDefaultAsync() ?? "";

                    rows.Add(new EmployeeMyRequestItemDto
                    {
                        FormType = "MissingPunch",
                        FormId = h.FormId,
                        AppliedAt = h.AppliedAt,
                        Status = overall,
                        CurrentStepNumber = waitingStep?.StepNumber,
                        CurrentStepName = waitingStep?.StepName,
                        WaitingApprovers = waitingApprovers,
                        DepartmentName = deptName,
                        JobTitleName = jobName
                    });
                }
            }

            if (string.IsNullOrWhiteSpace(formType) || formType.Equals("Leave", StringComparison.OrdinalIgnoreCase))
                await CollectLeaveAsync();
            if (string.IsNullOrWhiteSpace(formType) || formType.Equals("MissingPunch", StringComparison.OrdinalIgnoreCase))
                await CollectMissingPunchAsync();

            return Ok(rows.OrderByDescending(r => r.AppliedAt ?? DateTime.MinValue).ToList());
        }

        // ─────────────────── 4) 明細（同時支援 Leave / MissingPunch）
        // GET: api/EmployeeApprovalLogs/detail/{formType}/{formId}
        [HttpGet("detail/{formType}/{formId:int}")]
        public async Task<ActionResult<EmployeeApprovalDetailDto>> Detail(string formType, int formId)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized("找不到員工身分");

            var header = await GetHeaderAsync(formType, formId);
            if (header == null) return NotFound("找不到申請單");

            var logs = await _ctx.EmployeeApprovalLogs.AsNoTracking()
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
                        .Select(e => e.Name ?? "")
                        .FirstOrDefault() ?? "",
                    ApproveStatus = ToCode(x.ApproveStatus),
                    ApproveComment = x.ApproveComment,
                    ApproveDate = x.ApproveDate
                })
                .ToListAsync();

            var myApprovalId = await _ctx.EmployeeApprovalLogs.AsNoTracking()
                .Where(x => x.FormType == formType
                        && x.FormId == formId
                        && (x.ApproveStatus == DB_Waiting || x.ApproveStatus == SWaiting)
                        && x.ApproverId == me)
                .OrderBy(x => x.StepNumber)
                .Select(x => x.ApprovalId)
                .FirstOrDefaultAsync();

            var dto = new EmployeeApprovalDetailDto
            {
                ApprovalId = myApprovalId,
                FormType = formType,
                FormId = formId,
                ApplicantName = header.ApplicantName ?? "",
                ApplyDate = header.ApplyDate,

                // Leave 專屬
                LeaveTypeName = header.LeaveTypeName,
                StartDate = header.StartDate,
                EndDate = header.EndDate,
                StartTime = header.StartTime,
                EndTime = header.EndTime,
                LeaveHours = header.LeaveHours,

                // MissingPunch 專屬
                MissingDate = header.MissingDate,
                ActualInTime = header.ActualInTime,

                Reason = header.Reason,
                Logs = logs,
                CanCancel = header.CanCancel
            };

            return Ok(dto);
        }

        // ─────────────────── 5) 簽核通過 / 退回 / 抽單
        [HttpPut("{approvalId:int}/approve")]
        public async Task<IActionResult> Approve(int approvalId, [FromBody] EmployeeApprovalActionDto dto)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized("找不到員工身分");

            await _flow.DecideAsync(approvalId, me, SApproved, dto?.Comment);
            return Ok();
        }

        [HttpPut("{approvalId:int}/reject")]
        public async Task<IActionResult> Reject(int approvalId, [FromBody] EmployeeApprovalActionDto dto)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized("找不到員工身分");

            await _flow.DecideAsync(approvalId, me, SRejected, dto?.Comment);
            return Ok();
        }

        [HttpPut("cancel/{formType}/{formId:int}")]
        public async Task<IActionResult> Cancel(string formType, int formId)
        {
            var me = User.EmployeeId();
            if (me == 0) return Unauthorized("找不到員工身分");

            var anyWaiting = await _ctx.EmployeeApprovalLogs.AsNoTracking()
                .AnyAsync(l => l.FormType == formType && l.FormId == formId &&
                               (l.ApproveStatus == DB_Waiting || l.ApproveStatus == DB_Queued || l.ApproveStatus == SWaiting));

            var applicantId = await GetApplicantIdAsync(formType, formId);
            if (!anyWaiting || applicantId != me)
                return BadRequest("不可抽單");

            await _flow.MarkHeaderAsync(formType, formId, SCancelled);

            var nodes = await _ctx.EmployeeApprovalLogs
                .Where(l => l.FormType == formType && l.FormId == formId &&
                            (l.ApproveStatus == DB_Queued || l.ApproveStatus == DB_Waiting || l.ApproveStatus == SWaiting))
                .ToListAsync();

            foreach (var n in nodes)
            {
                n.ApproveStatus = STerminated;
                n.ApproveComment = "(申請人抽單)";
                n.ApproveDate = DateTime.Now;
            }
            await _ctx.SaveChangesAsync();
            return Ok();
        }

        // ─────────────────── 共用：表頭/申請人

        private sealed class FormHeader
        {
            public string? ApplicantName { get; set; }
            public DateTime? ApplyDate { get; set; }
            // Leave
            public string? LeaveTypeName { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public string? StartTime { get; set; }
            public string? EndTime { get; set; }
            public decimal? LeaveHours { get; set; }
            // 共用
            public string? Reason { get; set; }
            public bool CanCancel { get; set; }
            // MissingPunch
            public DateTime? MissingDate { get; set; } // DB: WorkDate (DateOnly?)
            public string? ActualInTime { get; set; } // DB: RequestedTime (TimeSpan?/DateTime?) → "HH:mm"
        }

        private static readonly Regex TimeTag =
            new(@"\[\s*T:(\d{2}):(\d{2})-(\d{2}):(\d{2})\]\s*", RegexOptions.Compiled);

        private static (string? st, string? et, string clean) ExtractTimes(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return (null, null, "");
            var m = TimeTag.Match(reason!);
            if (!m.Success) return (null, null, reason!.Trim());
            var st = $"{m.Groups[1].Value}:{m.Groups[2].Value}";
            var et = $"{m.Groups[3].Value}:{m.Groups[4].Value}";
            var clean = TimeTag.Replace(reason!, "").Trim();
            return (st, et, clean);
        }

        private async Task<FormHeader?> GetHeaderAsync(string formType, int formId)
        {
            if (formType.Equals("Leave", StringComparison.OrdinalIgnoreCase))
            {
                var h = await (
                    from l in _ctx.EmployeeLeaveApplications.AsNoTracking()
                    where l.LeaveId == formId
                    join e in _ctx.Employees.AsNoTracking() on l.EmployeeId equals e.EmployeeId
                    join t in _ctx.EmployeeLeaveTypes.AsNoTracking() on l.LeaveTypeId equals t.LeaveTypeId
                    select new
                    {
                        ApplicantName = e.Name,
                        l.ApplyDate,
                        l.StartDate,
                        l.EndDate,
                        l.LeaveHours,
                        l.Reason,
                        LeaveTypeName = t.TypeName,
                        HeaderStatus = l.Status,
                        ApplicantId = l.EmployeeId
                    }
                ).FirstOrDefaultAsync();

                if (h == null) return null;

                var (st, et, clean) = ExtractTimes(h.Reason);

                var hasPending = await _ctx.EmployeeApprovalLogs.AsNoTracking()
                    .AnyAsync(x => x.FormType == "Leave" && x.FormId == formId &&
                                   (x.ApproveStatus == DB_Waiting || x.ApproveStatus == DB_Queued || x.ApproveStatus == SWaiting));
                var me = User.EmployeeId();
                var canCancel = (me != 0 && (h.ApplicantId ?? 0) == me && hasPending);

                return new FormHeader
                {
                    ApplicantName = h.ApplicantName ?? "",
                    ApplyDate = h.ApplyDate,
                    LeaveTypeName = h.LeaveTypeName ?? "",
                    StartDate = h.StartDate?.ToDateTime(TimeOnly.MinValue),
                    EndDate = h.EndDate?.ToDateTime(TimeOnly.MinValue),
                    StartTime = st,
                    EndTime = et,
                    LeaveHours = h.LeaveHours,
                    Reason = clean,
                    CanCancel = canCancel
                };
            }

            if (formType.Equals("MissingPunch", StringComparison.OrdinalIgnoreCase))
            {
                var a = await (
                    from m in _ctx.EmployeeMissingPunchApplications.AsNoTracking()
                    where m.ApplicationId == formId
                    join e in _ctx.Employees.AsNoTracking() on m.EmployeeId equals e.EmployeeId
                    select new
                    {
                        ApplicantName = e.Name,
                        m.ApplyDate,
                        m.ApplyReason,
                        m.WorkDate,        // date -> DateOnly?
                        m.RequestedTime,   // datetime -> DateTime?
                        ApplicantId = m.EmployeeId
                    }
                ).FirstOrDefaultAsync();

                if (a == null) return null;

                var me = User.EmployeeId();
                var hasPending = await _ctx.EmployeeApprovalLogs.AsNoTracking()
                    .AnyAsync(x => x.FormType == "MissingPunch" && x.FormId == formId &&
                                   (x.ApproveStatus == DB_Waiting || x.ApproveStatus == DB_Queued || x.ApproveStatus == SWaiting));
                var canCancel = (me != 0 && (a.ApplicantId ?? 0) == me && hasPending);

                // DateOnly? -> DateTime?
                DateTime? missingDate = a.WorkDate.HasValue
                    ? a.WorkDate.Value.ToDateTime(TimeOnly.MinValue)
                    : (DateTime?)null;

                // DateTime? -> HH:mm
                string? actualInTime = a.RequestedTime.HasValue
                    ? a.RequestedTime.Value.ToString("HH:mm")
                    : null;

                return new FormHeader
                {
                    ApplicantName = a.ApplicantName ?? "",
                    ApplyDate = a.ApplyDate,
                    Reason = a.ApplyReason,
                    MissingDate = missingDate,
                    ActualInTime = actualInTime,
                    CanCancel = canCancel
                };
            }


            return null;
        }

        private async Task<int> GetApplicantIdAsync(string formType, int formId)
        {
            if (formType.Equals("Leave", StringComparison.OrdinalIgnoreCase))
                return await _ctx.EmployeeLeaveApplications.AsNoTracking()
                    .Where(x => x.LeaveId == formId).Select(x => x.EmployeeId ?? 0).FirstOrDefaultAsync();

            if (formType.Equals("MissingPunch", StringComparison.OrdinalIgnoreCase))
                return await _ctx.EmployeeMissingPunchApplications.AsNoTracking()
                    .Where(x => x.ApplicationId == formId).Select(x => x.EmployeeId ?? 0).FirstOrDefaultAsync();

            return 0;
        }

        // ─────────────────── 清單拼接（Leave / MissingPunch）
        private async Task<List<EmployeeApprovalInboxItemDto>> BuildInboxLikeRowsAsync(IEnumerable<dynamic> logTriples, bool isInbox)
        {
            var list = new List<EmployeeApprovalInboxItemDto>();

            foreach (var group in logTriples.GroupBy(x => (string)x.FormType))
            {
                var type = group.Key;
                var ids = group.Select(x => (int)x.FormId).Distinct().ToList();

                if (type.Equals("Leave", StringComparison.OrdinalIgnoreCase))
                {
                    var headers = await (
                        from h in _ctx.EmployeeLeaveApplications.AsNoTracking()
                        where ids.Contains(h.LeaveId)
                        join e in _ctx.Employees.AsNoTracking() on h.EmployeeId equals e.EmployeeId
                        join d in _ctx.EmployeeDepartments.AsNoTracking() on e.DepartmentId equals d.DepartmentId
                        join jt in _ctx.EmployeeJobTitles.AsNoTracking() on e.JobTitleId equals jt.JobTitleId into gj
                        from jt in gj.DefaultIfEmpty()
                        select new
                        {
                            FormId = h.LeaveId,
                            ReceivedAt = h.ApplyDate,
                            Status = h.Status,
                            DepartmentName = d.DepartmentName ?? "",
                            JobTitleName = jt != null ? (jt.TitleName ?? "") : "",
                            ApplicantName = e.Name ?? "",
                            FormTypeName = "請休假申請單"
                        }
                    ).ToListAsync();

                    foreach (var h in headers)
                    {
                        var anyLog = group.FirstOrDefault(x => (int)x.FormId == h.FormId);
                        list.Add(new EmployeeApprovalInboxItemDto
                        {
                            ApprovalId = anyLog?.ApprovalId ?? 0,
                            FormType = "Leave",
                            FormId = h.FormId,
                            ReceivedAt = h.ReceivedAt,
                            Status = isInbox ? SWaiting : ToCode(h.Status ?? DB_Review),
                            DepartmentName = h.DepartmentName,
                            JobTitleName = h.JobTitleName,
                            ApplicantName = h.ApplicantName,
                            FormTypeName = h.FormTypeName
                        });
                    }
                }
                else if (type.Equals("MissingPunch", StringComparison.OrdinalIgnoreCase))
                {
                    var headers = await (
                        from a in _ctx.EmployeeMissingPunchApplications.AsNoTracking()
                        where ids.Contains(a.ApplicationId)
                        join e in _ctx.Employees.AsNoTracking() on a.EmployeeId equals e.EmployeeId
                        join d in _ctx.EmployeeDepartments.AsNoTracking() on e.DepartmentId equals d.DepartmentId
                        join jt in _ctx.EmployeeJobTitles.AsNoTracking() on e.JobTitleId equals jt.JobTitleId into gj
                        from jt in gj.DefaultIfEmpty()
                        select new
                        {
                            FormId = a.ApplicationId,
                            ReceivedAt = a.ApplyDate,
                            Status = a.Status,
                            DepartmentName = d.DepartmentName ?? "",
                            JobTitleName = jt != null ? (jt.TitleName ?? "") : "",
                            ApplicantName = e.Name ?? "",
                            FormTypeName = "忘打卡申請單"
                        }
                    ).ToListAsync();

                    foreach (var h in headers)
                    {
                        var anyLog = group.FirstOrDefault(x => (int)x.FormId == h.FormId);
                        list.Add(new EmployeeApprovalInboxItemDto
                        {
                            ApprovalId = anyLog?.ApprovalId ?? 0,
                            FormType = "MissingPunch",
                            FormId = h.FormId,
                            ReceivedAt = h.ReceivedAt,
                            Status = isInbox ? SWaiting : ToCode(h.Status ?? DB_Review),
                            DepartmentName = h.DepartmentName,
                            JobTitleName = h.JobTitleName,
                            ApplicantName = h.ApplicantName,
                            FormTypeName = h.FormTypeName
                        });
                    }
                }
            }

            return list;
        }
    }
}
