namespace prjFinalProjectApi.Models.Dto
{
    /// <summary>職稱下拉用項目，包含所屬部門（可能為 null）</summary>
    public sealed record EmployeeJobTitleOptionDto(int Id, string Name, int? DeptId);
}