// Models/Dto/EmployeeDetailDto.cs
namespace prjFinalProjectApi.Models.Dto
{
    public class EmployeeDetailDto
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; } = "";
        public string? IdentityNumber { get; set; }
        public DateTime? BirthDate { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? EducationLevel { get; set; }
        public string? RegisteredAddress { get; set; }
        public string? CurrentAddress { get; set; }
        public int? Height { get; set; }
        public int? Weight { get; set; }
        public string? PayrollBankAccount { get; set; }
        public string? PhotoPath { get; set; }

        // 職務資訊
        public string EmploymentStatusText { get; set; } = ""; // 在職/離職
        public string? DepartmentName { get; set; }
        public string? JobTitleName { get; set; }
        public DateTime? HireDate { get; set; }
        public bool? PoliceClearanceCertified { get; set; } // 良民證
        public bool? IsSupervisor { get; set; }
        public bool? IsAdmin { get; set; }

        // 緊急聯絡人
        public string? EmergencyContactPerson { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? EmergencyContactRelationship { get; set; }
    }
}
