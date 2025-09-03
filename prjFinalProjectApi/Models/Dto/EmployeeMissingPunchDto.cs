namespace prjFinalProjectApi.Models.Dto
{
    public class EmployeeMissingPunchDto
    {
        public DateTime WorkDate { get; set; }          // 忘卡日期
        public string MissingType { get; set; } = "In"; // In/Out
        public string? ApplyReason { get; set; }
        public DateTime RequestedTime { get; set; }     // 實際打卡時間
    }

    public class EmployeeMissingPunchCreateDto
    {
        /// <summary>忘卡日期 (yyyy-MM-dd)</summary>
        public string WorkDate { get; set; } = default!;

        /// <summary>實際打卡時間 (HH:mm)</summary>
        public string RequestedTime { get; set; } = default!;

        /// <summary>In / Out，預設 In</summary>
        public string MissingType { get; set; } = "In";

        /// <summary>可選原因；若不用可不傳</summary>
        public string? ApplyReason { get; set; }
    }
    public class MissingPunchDetailDto
    {
        public int ApplicationID { get; set; }
        public int EmployeeID { get; set; }

        // ↓↓↓ 這三個依資料庫設定改成可為 null ↓↓↓
        public DateTime? WorkDate { get; set; }    // DB: DateOnly?
        public DateTime? RequestedTime { get; set; }    // DB: DateTime?
        public DateTime? ApplyDate { get; set; }    // DB: DateTime?

        public string MissingType { get; set; } = "";
        public string? ApplyReason { get; set; }
        public string Status { get; set; } = "";
        public DateTime? ApprovedDate { get; set; }

        public List<EmployeeApprovalLogDto> Logs { get; set; } = new();
    }

    public class ApprovalLogDto
    {
        public int ApprovalID { get; set; }
        public int StepNumber { get; set; }
        public string StepName { get; set; } = "";
        public int? ApproverID { get; set; }
        public string? ApproverName { get; set; }
        public string? ApproveStatus { get; set; }          // Waiting/Approved/Rejected
        public string? ApproveComment { get; set; }
        public DateTime? ApproveDate { get; set; }
        public bool IsFinalStep { get; set; }
    }

    public class EmployeeApproveDecisionDto
    {
        public string Decision { get; set; } = "Approved"; // Approved / Rejected
        public string? Comment { get; set; }
    }
}
