using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Helpers;          // User.EmployeeId()
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Globalization;
using System.Text.RegularExpressions;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeeLeaveApplicationsController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        public EmployeeLeaveApplicationsController(DbNursingHomeContext context) => _context = context;

        [HttpGet("me")]
        public async Task<ActionResult<EmployeeLeaveMeDto>> Me()
        {
            var meId = User.EmployeeId();
            if (meId == 0) return Unauthorized("找不到員工身分。");

            var name = await _context.Employees
                .Where(e => e.EmployeeId == meId)
                .Select(e => e.Name)
                .FirstOrDefaultAsync() ?? "";

            return Ok(new EmployeeLeaveMeDto { ApplicantName = name });
        }

        // 同部門、排除主管，且加上「自己（申請人）」選項
        [HttpGet("agents")]
        public async Task<ActionResult<IEnumerable<EmployeeAgentDto>>> Agents()
        {
            var meId = User.EmployeeId();
            if (meId == 0) return Unauthorized("找不到員工身分。");

            var my = await _context.Employees
                .Where(e => e.EmployeeId == meId)
                .Select(e => new { e.DepartmentId, e.JobTitleId })
                .FirstAsync();

            var others = await (
                from e in _context.Employees
                where e.DepartmentId == my.DepartmentId
                      && e.EmployeeId != meId
                      && e.IsSupervisor != true
                join jt in _context.Set<EmployeeJobTitle>()
                    on e.JobTitleId equals jt.JobTitleId into gj
                from jt in gj.DefaultIfEmpty()
                orderby (e.JobTitleId ?? 9999), e.Name
                select new EmployeeAgentDto
                {
                    EmployeeId = e.EmployeeId,
                    Name = e.Name,
                    JobTitleId = e.JobTitleId ?? 0,
                    JobTitleName = jt != null ? jt.TitleName! : "",
                    Display = (jt != null && !string.IsNullOrEmpty(jt.TitleName))
                                ? (jt.TitleName + "-" + e.Name)
                                : e.Name
                }
            ).ToListAsync();

            var meDisplay = await (from e in _context.Employees
                                   join jt in _context.EmployeeJobTitles on e.JobTitleId equals jt.JobTitleId into gj
                                   from jt in gj.DefaultIfEmpty()
                                   where e.EmployeeId == meId
                                   select new EmployeeAgentDto
                                   {
                                       EmployeeId = e.EmployeeId,
                                       Name = e.Name,
                                       JobTitleId = e.JobTitleId ?? 0,
                                       JobTitleName = jt != null ? jt.TitleName! : "",
                                       Display = "自己（申請人）"
                                   }).FirstAsync();

            var list = new List<EmployeeAgentDto> { meDisplay };
            list.AddRange(others);
            return Ok(list);
        }

        [HttpGet("types")]
        public async Task<ActionResult<IEnumerable<EmployeeLeaveTypeDto>>> GetTypes()
        {
            var items = await _context.EmployeeLeaveTypes
                .OrderBy(x => x.LeaveTypeId)
                .Select(x => new EmployeeLeaveTypeDto
                {
                    LeaveTypeId = x.LeaveTypeId,
                    TypeName = x.TypeName
                })
                .ToListAsync();

            return Ok(items);
        }

        // 帶部門/職稱回前端
        [HttpGet("my")]
        public async Task<ActionResult<IEnumerable<EmployeeLeaveDto>>> GetMy()
        {
            var meId = User.EmployeeId();
            if (meId == 0) return Unauthorized("找不到員工身分。");

            var rows = await (
                from h in _context.EmployeeLeaveApplications
                where h.EmployeeId == meId
                join lt in _context.EmployeeLeaveTypes on h.LeaveTypeId equals lt.LeaveTypeId into glt
                from lt in glt.DefaultIfEmpty()
                join e in _context.Employees on h.EmployeeId equals e.EmployeeId
                join d in _context.EmployeeDepartments on e.DepartmentId equals d.DepartmentId into gd
                from d in gd.DefaultIfEmpty()
                join jt in _context.EmployeeJobTitles on e.JobTitleId equals jt.JobTitleId into gjt
                from jt in gjt.DefaultIfEmpty()
                orderby h.ApplyDate descending
                select new
                {
                    h.LeaveId,
                    EmployeeId = h.EmployeeId ?? 0,
                    LeaveTypeId = h.LeaveTypeId ?? 0,
                    LeaveTypeName = lt != null ? lt.TypeName : "",
                    h.StartDate,
                    h.EndDate,
                    h.LeaveHours,
                    h.Status,
                    h.ApplyDate,
                    h.ApprovedDate,
                    h.Reason,
                    DepartmentName = d != null ? d.DepartmentName : "",
                    JobTitleName = jt != null ? jt.TitleName : ""
                }
            ).ToListAsync();

            var result = new List<EmployeeLeaveDto>();
            foreach (var r in rows)
            {
                var (st, et, clean) = ReasonCodec.Extract(r.Reason);

                result.Add(new EmployeeLeaveDto
                {
                    LeaveId = r.LeaveId,
                    EmployeeId = r.EmployeeId,
                    LeaveTypeId = r.LeaveTypeId,
                    LeaveTypeName = r.LeaveTypeName ?? "",
                    StartDate = r.StartDate.HasValue ? r.StartDate.Value.ToDateTime(TimeOnly.MinValue) : DateTime.MinValue,
                    EndDate = r.EndDate.HasValue ? r.EndDate.Value.ToDateTime(TimeOnly.MinValue) : DateTime.MinValue,
                    StartTime = st,
                    EndTime = et,
                    LeaveHours = r.LeaveHours ?? 0m,
                    Status = r.Status ?? "",
                    ApplyDate = r.ApplyDate,
                    ApprovedDate = r.ApprovedDate,
                    Reason = clean,
                    DepartmentName = r.DepartmentName ?? "",
                    JobTitleName = r.JobTitleName ?? ""
                });
            }

            return Ok(result);
        }

        // 建立請假 + 建立簽核流程（第一關一定是代理人）
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] EmployeeLeaveCreateDto dto)
        {
            var meId = User.EmployeeId();
            if (meId == 0) return Unauthorized("找不到員工身分。");

            if (!TimeSpan.TryParseExact(dto.StartTime, @"hh\:mm", CultureInfo.InvariantCulture, out var st))
                return BadRequest("開始時間格式錯誤（HH:mm）。");
            if (!TimeSpan.TryParseExact(dto.EndTime, @"hh\:mm", CultureInfo.InvariantCulture, out var et))
                return BadRequest("結束時間格式錯誤（HH:mm）。");

            var startDt = dto.StartDate.Date + st;
            var endDt = dto.EndDate.Date + et;
            if (endDt <= startDt.AddMinutes(30))
                return BadRequest("結束時間需晚於開始時間 30 分鐘以上。");

            var mins = (endDt - startDt).TotalMinutes;
            var hours = (decimal)(Math.Round(mins / 30.0) / 2.0);

            var startDOnly = DateOnly.FromDateTime(dto.StartDate);
            var endDOnly = DateOnly.FromDateTime(dto.EndDate);

            var overlap = await _context.EmployeeLeaveApplications.AnyAsync(x =>
                x.EmployeeId == meId &&
                x.Status != "Cancelled" &&
                x.Status != "Rejected" &&
                (x.StartDate ?? DateOnly.MinValue) <= endDOnly &&
                startDOnly <= (x.EndDate ?? DateOnly.MaxValue)
            );
            if (overlap) return Conflict("申請日期區間與既有紀錄重疊。");

            // 代理人檢核
            if (dto.AgentEmployeeId <= 0)
                return BadRequest("必須選擇職務代理人。");

            var agent = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == dto.AgentEmployeeId);
            if (agent == null) return BadRequest("請選擇有效的代理人。");
            if (agent.IsSupervisor == true) return BadRequest("代理人不可為部門主管。");

            var myDeptId = await _context.Employees
                .Where(e => e.EmployeeId == meId)
                .Select(e => e.DepartmentId)
                .FirstAsync();

            if (agent.DepartmentId != myDeptId)
                return BadRequest("代理人必須為同部門員工。");

            var reasonWithTag = ReasonCodec.Embed(dto.Reason, st, et);

            var row = new EmployeeLeaveApplication
            {
                EmployeeId = meId,
                LeaveTypeId = dto.LeaveTypeId,
                StartDate = startDOnly,
                EndDate = endDOnly,
                LeaveHours = hours,
                Reason = reasonWithTag,
                Status = "審核中",
                ApplyDate = DateTime.UtcNow
            };

            _context.EmployeeLeaveApplications.Add(row);
            await _context.SaveChangesAsync();

            await CreateLogsFromTemplateAsync(row.LeaveId, meId, dto.AgentEmployeeId);

            return CreatedAtAction(nameof(GetMy), new { id = row.LeaveId }, new { row.LeaveId });
        }

        [HttpPut("{id:int}/cancel")]
        public async Task<ActionResult> Cancel(int id)
        {
            var meId = User.EmployeeId();
            if (meId == 0) return Unauthorized("找不到員工身分。");

            var row = await _context.EmployeeLeaveApplications
                .FirstOrDefaultAsync(x => x.LeaveId == id && x.EmployeeId == meId);

            if (row == null) return NotFound();
            if (row.Status is "Approved" or "Rejected")
                return BadRequest("此申請已審核，無法取消。");

            row.Status = "Cancelled";
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // 同關任一人簽即可（ANY）
        private async Task CreateLogsFromTemplateAsync(int leaveId, int applicantId, int agentEmployeeId)
        {
            var flow = await _context.EmployeeApprovalFlowTemplates
                .Where(t => t.FormType == "Leave")
                .OrderBy(t => t.StepNumber)
                .ToListAsync();

            if (!flow.Any())
                throw new InvalidOperationException("尚未設定請假單簽核流程樣板。");

            var meDeptId = await _context.Employees
                .Where(e => e.EmployeeId == applicantId)
                .Select(e => e.DepartmentId)
                .FirstAsync();

            var deptSupervisors = await _context.Employees
                .Where(e => e.DepartmentId == meDeptId && e.IsSupervisor == true)
                .Select(e => e.EmployeeId)
                .ToListAsync();

            var hrMembers = await (from e in _context.Employees
                                   join d in _context.EmployeeDepartments on e.DepartmentId equals d.DepartmentId
                                   where d.DepartmentName.Contains("人資")
                                   select e.EmployeeId).ToListAsync();

            var logs = new List<EmployeeApprovalLog>();
            bool firstWaitingAssigned = false;
            int maxStep = flow.Max(x => x.StepNumber) ?? 0;

            foreach (var f in flow)
            {
                var role = (f.Role ?? string.Empty).Trim();
                var stepName = (f.StepName ?? string.Empty).Trim();

                List<int> approverIds = role switch
                {
                    "Agent" => new List<int> { agentEmployeeId },
                    "DeptSupervisor" => deptSupervisors.ToList(),
                    "HR" => hrMembers.ToList(),
                    _ => new List<int>()
                };

                if (approverIds.Count == 0)
                    throw new InvalidOperationException($"找不到簽核人：{stepName}（Role={role}）。");

                var statusForThisStep = (!firstWaitingAssigned) ? "待簽核" : "排隊中";

                foreach (var approverId in approverIds)
                {
                    logs.Add(new EmployeeApprovalLog
                    {
                        FormType = "Leave",
                        FormId = leaveId,
                        StepNumber = f.StepNumber,   // 同關同編號
                        StepName = stepName,
                        ApproverId = approverId,
                        ApproveStatus = statusForThisStep,
                        ApproveComment = null,
                        ApproveDate = null,
                        IsFinalStep = (f.StepNumber == maxStep)
                    });
                }

                if (!firstWaitingAssigned) firstWaitingAssigned = true;
            }

            _context.EmployeeApprovalLogs.AddRange(logs);
            await _context.SaveChangesAsync();
        }

        #region Reason 時間標記工具
        private static class ReasonCodec
        {
            private static readonly Regex Tag =
                new(@"\[T:(\d{2}):(\d{2})-(\d{2}):(\d{2})\]\s*", RegexOptions.Compiled);

            public static string Embed(string? reason, TimeSpan start, TimeSpan end)
                => $"[T:{start:hh\\:mm}-{end:hh\\:mm}] " + (reason ?? "").Trim();

            public static (string? start, string? end, string clean) Extract(string? reason)
            {
                if (string.IsNullOrWhiteSpace(reason)) return (null, null, "");
                var m = Tag.Match(reason!);
                if (!m.Success) return (null, null, reason!.TrimStart());
                var st = $"{m.Groups[1].Value}:{m.Groups[2].Value}";
                var et = $"{m.Groups[3].Value}:{m.Groups[4].Value}";
                var clean = Tag.Replace(reason!, "").TrimStart();
                return (st, et, clean);
            }
        }
        #endregion
    }
}
