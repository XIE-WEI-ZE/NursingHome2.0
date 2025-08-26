using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using prjFinalProjectApi.Models;

namespace prjFinalProjectApi.Helpers
{
    /// <summary>
    /// 員工用 JWT：把 Admin / Supervisor / DepartmentId 都放進 claims
    /// ClaimTypes.Name ＝「帳號」，ClaimTypes.GivenName ＝「顯示姓名」
    /// </summary>
    public static class EmployeeJwtHelper
    {
        public static string GenerateToken(
            Employee employee,
            string username,              // 🔴 新增：帳號（會放進 ClaimTypes.Name）
            string secretKey,
            string issuer,
            string audience,
            int expirMinutes = 60)
        {
            if (employee == null) throw new ArgumentNullException(nameof(employee));
            if (string.IsNullOrWhiteSpace(secretKey)) throw new ArgumentNullException(nameof(secretKey));

            var claims = new List<Claim>
            {
                // ✅ 後端用來識別帳號（Controller 目前用 ClaimTypes.Name 取值）
                new Claim(ClaimTypes.Name, username ?? string.Empty),

                // 顯示用姓名（前端可讀 given_name）
                new Claim(ClaimTypes.GivenName, employee.Name ?? string.Empty),

                // 主要識別：EmployeeId
                new Claim(ClaimTypes.NameIdentifier, employee.EmployeeId.ToString()),

                // 自訂 claims
                new Claim("employeeid", employee.EmployeeId.ToString()),
                new Claim("deptid", (employee.DepartmentId ?? 0).ToString()),
                new Claim("isadmin", (employee.IsAdmin ?? false) ? "true" : "false"),
                new Claim("issupervisor", (employee.IsSupervisor ?? false) ? "true" : "false"),
            };

            // 角色
            if (employee.IsAdmin == true) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            if (employee.IsSupervisor == true) claims.Add(new Claim(ClaimTypes.Role, "Supervisor"));
            claims.Add(new Claim(ClaimTypes.Role, "Employee"));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(expirMinutes);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
