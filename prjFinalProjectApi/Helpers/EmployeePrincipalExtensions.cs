using System.Security.Claims;

namespace prjFinalProjectApi.Helpers
{
    public static class EmployeePrincipalExtensions
    {
        public static int EmployeeId(this ClaimsPrincipal u)
            => int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier) ?? u.FindFirstValue("employeeid"), out var x) ? x : 0;

        public static int DepartmentId(this ClaimsPrincipal u)
            => int.TryParse(u.FindFirstValue("deptid"), out var x) ? x : 0;

        public static bool IsAdmin(this ClaimsPrincipal u)
            => (u.FindFirstValue("isadmin") ?? "").Equals("true", StringComparison.OrdinalIgnoreCase);

        public static bool IsSupervisor(this ClaimsPrincipal u)
            => (u.FindFirstValue("issupervisor") ?? "").Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
