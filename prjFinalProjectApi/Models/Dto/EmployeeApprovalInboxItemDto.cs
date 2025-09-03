using System;

namespace prjFinalProjectApi.Models.Dto
{
    /// <summary>清單：待我簽核的項目</summary>
    public class EmployeeApprovalInboxItemDto
    {
        public int ApprovalId { get; set; }
        public string FormType { get; set; } = "";
        public int FormId { get; set; }

        /// <summary>收到日期（可能查不到，因此允許為 null）</summary>
        public DateTime? ReceivedAt { get; set; }

        /// <summary>彙總狀態：Waiting / Rejected / Completed</summary>
        public string Status { get; set; } = "Waiting";

        public string DepartmentName { get; set; } = "";
        public string JobTitleName { get; set; } = "";
        public string ApplicantName { get; set; } = "";

        /// <summary>顯示用：請休假申請單 / 忘打卡申請單</summary>
        public string FormTypeName { get; set; } = "";
    }
}
