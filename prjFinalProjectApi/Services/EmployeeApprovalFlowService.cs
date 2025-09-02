using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;

namespace prjFinalProjectApi.Services
{
    public class EmployeeApprovalFlowService
    {
        private readonly DbNursingHomeContext _ctx;
        public EmployeeApprovalFlowService(DbNursingHomeContext ctx) => _ctx = ctx;

        // 統一碼
        private const string SWaiting = "Waiting";
        private const string SApproved = "Approved";
        private const string SRejected = "Rejected";
        private const string STerminated = "Terminated";
        private const string SCompleted = "Completed";
        private const string DB_Queued = "排隊中";

        /// <summary>
        /// 依樣板產生流程。
        /// 同一步驟若有多位審核者，對每名審核者各寫一筆 Log（StepNumber 相同）。
        /// 第一關設為 Waiting，其餘設為「排隊中」。
        /// </summary>
        public async Task CreateFlowForAsync(string formType, int formId, int applicantEmployeeId)
        {
            var steps = await _ctx.EmployeeApprovalFlowTemplates
                .Where(t => t.FormType == formType)
                .OrderBy(t => t.StepNumber)
                .ToListAsync();

            if (!steps.Any())
                throw new InvalidOperationException($"No approval flow template for {formType}");

            var logs = new List<EmployeeApprovalLog>();

            for (int i = 0; i < steps.Count; i++)
            {
                var t = steps[i];
                var isFirst = i == 0;
                var isFinal = i == steps.Count - 1;

                var approverIds = await ResolveApproverIdsAsync(t.Role ?? "", applicantEmployeeId);

                // 沒人也放一筆 placeholder，避免流程卡住
                if (approverIds.Count == 0) approverIds.Add(0);

                foreach (var aid in approverIds)
                {
                    logs.Add(new EmployeeApprovalLog
                    {
                        FormType = formType,
                        FormId = formId,
                        StepNumber = t.StepNumber,
                        StepName = t.StepName,
                        ApproverId = (aid == 0 ? (int?)null : aid),
                        ApproveStatus = isFirst ? SWaiting : DB_Queued,
                        IsFinalStep = isFinal
                    });
                }
            }

            _ctx.EmployeeApprovalLogs.AddRange(logs);
            await _ctx.SaveChangesAsync();
        }

        /// <summary>
        /// 審核決策：同一步若多人，一人核准即過關，其餘同關節點終止；退回則全線終止。
        /// </summary>
        public async Task DecideAsync(int approvalId, int me, string decision, string? comment)
        {
            var log = await _ctx.EmployeeApprovalLogs.FirstOrDefaultAsync(l => l.ApprovalId == approvalId);
            if (log == null) throw new KeyNotFoundException("Approval not found");

            if (log.ApproverId.HasValue && log.ApproverId.Value != me)
                throw new UnauthorizedAccessException("Not the assigned approver");

            // 標記當前節點
            log.ApproveStatus = decision;   // "Approved" / "Rejected"
            log.ApproveComment = comment;
            log.ApproveDate = DateTime.Now;
            await _ctx.SaveChangesAsync();

            // 同單全部節點
            var all = await _ctx.EmployeeApprovalLogs
                .Where(x => x.FormType == log.FormType && x.FormId == log.FormId)
                .OrderBy(x => x.StepNumber)
                .ToListAsync();

            if (decision == SApproved)
            {
                // 同一步其他未處理的終止
                foreach (var s in all.Where(x =>
                    x.StepNumber == log.StepNumber &&
                    x.ApprovalId != log.ApprovalId &&
                    (x.ApproveStatus == SWaiting || x.ApproveStatus == DB_Queued || string.IsNullOrEmpty(x.ApproveStatus))))
                {
                    s.ApproveStatus = STerminated;
                    s.ApproveComment = "(同關其他簽核者已簽准)";
                    s.ApproveDate = DateTime.Now;
                }
                await _ctx.SaveChangesAsync();

                // 啟動下一步
                var nextStep = all
                    .Where(x => x.StepNumber > log.StepNumber)
                    .Select(x => x.StepNumber)
                    .DefaultIfEmpty(0)
                    .Min();

                if (nextStep > 0)
                {
                    foreach (var n in all.Where(x =>
                        x.StepNumber == nextStep &&
                        (x.ApproveStatus == DB_Queued || string.IsNullOrEmpty(x.ApproveStatus))))
                    {
                        n.ApproveStatus = SWaiting;
                    }
                    await _ctx.SaveChangesAsync();
                }
                else
                {
                    // 無下一步 → 表頭結案
                    await MarkHeaderAsync(log.FormType!, log.FormId ?? 0, SCompleted);
                }
            }
            else // Rejected
            {
                foreach (var r in all.Where(x =>
                    x.ApprovalId != log.ApprovalId &&
                    (x.ApproveStatus == SWaiting || x.ApproveStatus == DB_Queued)))
                {
                    r.ApproveStatus = STerminated;
                    r.ApproveComment = "(流程已退回)";
                    r.ApproveDate = DateTime.Now;
                }
                await _ctx.SaveChangesAsync();

                await MarkHeaderAsync(log.FormType!, log.FormId ?? 0, SRejected);
            }
        }

        /// <summary>
        /// 更新表頭狀態（Leave / MissingPunch）
        /// </summary>
        public async Task MarkHeaderAsync(string formType, int formId, string finalStatus)
        {
            if (formType.Equals("Leave", StringComparison.OrdinalIgnoreCase))
            {
                var h = await _ctx.EmployeeLeaveApplications.FirstOrDefaultAsync(x => x.LeaveId == formId);
                if (h != null)
                {
                    h.Status = finalStatus;
                    if (finalStatus == SCompleted || finalStatus == SApproved)
                        h.ApprovedDate = DateTime.Now;
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
                    if (finalStatus == SCompleted || finalStatus == SApproved)
                        a.ApprovedDate = DateTime.Now;
                    await _ctx.SaveChangesAsync();
                }
                return;
            }
        }

        /// <summary>
        /// 依角色回傳「多位」審核者。
        /// DeptSupervisor：同部門且 IsSupervisor = true 的所有員工
        /// HR            ：部門名稱含「人資」的所有員工
        /// 其他角色      ：目前回傳空集合（可依需求再擴充）
        /// </summary>
        public async Task<List<int>> ResolveApproverIdsAsync(string role, int applicantId)
        {
            // 申請人部門
            var me = await _ctx.Employees.AsNoTracking()
                .Where(e => e.EmployeeId == applicantId)
                .Select(e => new { e.DepartmentId })
                .FirstOrDefaultAsync();

            if (me == null) return new List<int>();

            if (role.Equals("DeptSupervisor", StringComparison.OrdinalIgnoreCase))
            {
                return await _ctx.Employees.AsNoTracking()
                    .Where(e => e.DepartmentId == me.DepartmentId && e.IsSupervisor == true)
                    .Select(e => e.EmployeeId)
                    .ToListAsync();
            }

            if (role.Equals("HR", StringComparison.OrdinalIgnoreCase))
            {
                // 不依賴 IsHr 欄位，直接以部門名稱包含「人資」判斷
                var hrIds = await (
                    from e in _ctx.Employees.AsNoTracking()
                    join d in _ctx.EmployeeDepartments.AsNoTracking()
                        on e.DepartmentId equals d.DepartmentId
                    where (d.DepartmentName ?? "").Contains("人資")
                    select e.EmployeeId
                ).ToListAsync();

                return hrIds;
            }

            // 其餘角色先不處理；如需「職務代理人」等再擴充
            return new List<int>();
        }
    }
}
