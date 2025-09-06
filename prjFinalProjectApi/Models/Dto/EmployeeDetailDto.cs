namespace prjFinalProjectApi.Models.Dto
{
    public class EmployeeDetailDto
    {
        // 基本資料
        public int EmployeeId { get; set; }
        public string Name { get; set; } = string.Empty; /// <summary>DB 原始性別（你目前直接存「男 / 女」）</summary> 
        public string? Gender { get; set; } /// <summary>顯示用性別（通常等同 Gender，預留需要轉換時使用）</summary> 
        public string? GenderText { get; set; }
        public string? IdentityNumber { get; set; }
        public DateTime? BirthDate { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? EducationLevel { get; set; }
        public string? RegisteredAddress { get; set; }
        public string? CurrentAddress { get; set; }
        public int? Height { get; set; }
        public int? Weight { get; set; }
        public string? PayrollBankAccount { get; set; } /// <summary>大頭照（可為 API 靜態檔完整 URL 或 /images/... 相對路徑）</summary> 
        public string? PhotoPath { get; set; } // 職務/在職 /// <summary>在職/離職 的文字</summary>
        public string EmploymentStatusText { get; set; } = string.Empty; /// <summary>可選：若前端需要也可帶回ID</summary>
        public int? DepartmentId { get; set; }
        public string? DepartmentName { get; set; } /// <summary>可選：若前端需要也可帶回ID</summary>
        public int? JobTitleId { get; set; }
        public string? JobTitleName { get; set; }
        public DateTime? HireDate { get; set; }
        public bool? PoliceClearanceCertified { get; set; }
        public bool? IsSupervisor { get; set; }
        public bool? IsAdmin { get; set; } // 緊急聯絡人
        public string? EmergencyContactPerson { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? EmergencyContactRelationship { get; set; }
    }
}