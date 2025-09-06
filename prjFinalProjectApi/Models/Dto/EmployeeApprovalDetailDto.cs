using System;
using System.Collections.Generic;

namespace prjFinalProjectApi.Models.Dto
{
    /// <summary>簽核明細（左側基本資料 + 進度 + 是否可抽單）</summary>
    public class EmployeeApprovalDetailDto
    {
        /// <summary>目前輪到的那一筆簽核紀錄（若無則 0）</summary>
        public int ApprovalId { get; set; }

        public string FormType { get; set; } = "";
        public int FormId { get; set; }

        public string ApplicantName { get; set; } = "";
        public DateTime? ApplyDate { get; set; }

        // 請假單特有
        public string? LeaveTypeName { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public decimal? LeaveHours { get; set; }

        public string? Reason { get; set; }
        public DateTime? MissingDate { get; set; }
        public string? ActualInTime { get; set; }

        /// <summary>簽核進度（時間軸）</summary>
        public List<EmployeeApprovalLogDto> Logs { get; set; } = new();

        /// <summary>目前登入者是否可抽單（通常＝申請人且流程尚未定案）</summary>
        public bool CanCancel { get; set; }
    }
}
