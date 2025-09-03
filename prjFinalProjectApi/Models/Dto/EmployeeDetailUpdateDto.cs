namespace prjFinalProjectApi.Models.Dto
{
    /// <summary>員工詳細資料更新用 DTO（只要有值才更新）</summary>
    public sealed class EmployeeDetailUpdateDto
    {
        // 基本
        public string? Name { get; set; }
        public string? Gender { get; set; }
        public string? IdentityNumber { get; set; }
        public string? BirthDate { get; set; }                 // "yyyy-MM-dd"
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? EducationLevel { get; set; }
        public string? RegisteredAddress { get; set; }
        public string? CurrentAddress { get; set; }
        public int? Height { get; set; }
        public int? Weight { get; set; }
        public string? PayrollBankAccount { get; set; }

        // 在職/部門/職稱
        public bool? EmploymentStatus { get; set; }            // 可直接給 bool
        public string? EmploymentStatusText { get; set; }      // 或給 "在職"/"離職"
        public int? DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
        public int? JobTitleId { get; set; }
        public string? JobTitleName { get; set; }
        public string? HireDate { get; set; }                  // "yyyy-MM-dd"

        // 布林
        public bool? PoliceClearanceCertified { get; set; }
        public bool? IsSupervisor { get; set; }
        public bool? IsAdmin { get; set; }

        // 緊急聯絡人
        public string? EmergencyContactPerson { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? EmergencyContactRelationship { get; set; }

        // 圖檔路徑（若有）
        public string? PhotoPath { get; set; }
    }
}
