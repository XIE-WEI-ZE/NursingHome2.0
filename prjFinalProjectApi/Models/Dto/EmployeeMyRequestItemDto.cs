// Models/Dto/EmployeeMyRequestItemDto.cs
namespace prjFinalProjectApi.Models.Dto
{
    public class EmployeeMyRequestItemDto
    {
        public string FormType { get; set; } = "Leave";
        public int FormId { get; set; }
        public DateTime? AppliedAt { get; set; }

        /// <summary>總狀態：審核中/Approved/Rejected/Cancelled</summary>
        public string Status { get; set; } = "";

        public int? CurrentStepNumber { get; set; }
        public string? CurrentStepName { get; set; }
        public List<string> WaitingApprovers { get; set; } = new();

        // ★ 新增：給前端顯示部門/職稱
        public string? DepartmentName { get; set; }
        public string? JobTitleName { get; set; }
    }
}
