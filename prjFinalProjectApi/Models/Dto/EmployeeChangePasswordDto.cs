namespace prjFinalProjectApi.Models.Dto
{
    public class EmployeeChangePasswordDto
    {
        public string OldPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }
}
