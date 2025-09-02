using System;

namespace prjFinalProjectApi.Models.Dto
{
    /// <summary>單筆簽核紀錄（時間軸節點）</summary>
    public class EmployeeApprovalLogDto
    {
        public int ApprovalId { get; set; }
        public string StepName { get; set; } = "";
        public string Role { get; set; } = "";
        public string ApproverName { get; set; } = "";
        public string ApproveStatus { get; set; } = "";
        public string? ApproveComment { get; set; }
        public DateTime? ApproveDate { get; set; }
    }
}
