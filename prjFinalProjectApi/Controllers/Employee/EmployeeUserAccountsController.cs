using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

using prjFinalProjectApi.Helpers;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using EmployeeEntity = prjFinalProjectApi.Models.Employee;

namespace prjFinalProjectApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeeUserAccountsController : ControllerBase
    {
        private readonly DbNursingHomeContext _db;
        private readonly ILogger<EmployeeUserAccountsController> _logger;

        public EmployeeUserAccountsController(
            DbNursingHomeContext db,
            ILogger<EmployeeUserAccountsController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ===== DTOs =====
        public class RegisterFullRequest
        {
            public string Name { get; set; } = string.Empty;
            public string IdentityNumber { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
        public class LoginDto
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
        public sealed class UpdateEmployeeDetailApi
        {
            public int employeeId { get; set; }
            public string name { get; set; } = string.Empty;
            public string identityNumber { get; set; } = string.Empty;
            public string? birthDate { get; set; }
            public string? phone { get; set; }
            public string? email { get; set; }
            public string? educationLevel { get; set; }
            public string? registeredAddress { get; set; }
            public string? currentAddress { get; set; }
            public int? height { get; set; }
            public int? weight { get; set; }
            public string? payrollBankAccount { get; set; }
            public string? employmentStatusText { get; set; }
            public string? departmentName { get; set; }
            public string? jobTitleName { get; set; }
            public string? hireDate { get; set; }
            public bool policeClearanceCertified { get; set; }
            public bool isSupervisor { get; set; }
            public bool isAdmin { get; set; }
            public string? emergencyContactPerson { get; set; }
            public string? emergencyContactPhone { get; set; }
            public string? emergencyContactRelationship { get; set; }
        }
        public class UploadPhotoForm { public IFormFile Photo { get; set; } = default!; }

        // ===== 註冊（匿名） =====
        [AllowAnonymous]
        [HttpPost("register-full")]
        public async Task<IActionResult> RegisterFull([FromBody] RegisterFullRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name) ||
                string.IsNullOrWhiteSpace(req.IdentityNumber) ||
                string.IsNullOrWhiteSpace(req.Email) ||
                string.IsNullOrWhiteSpace(req.Username) ||
                string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("請填寫必填欄位：姓名、身分證、Email、帳號、密碼。");

            if (await _db.EmployeeUserAccounts.AnyAsync(u => u.Username == req.Username))
                return Conflict("帳號已存在");
            if (await _db.Employees.AnyAsync(e => e.IdentityNumber == req.IdentityNumber))
                return Conflict("身分證已存在");
            if (!string.IsNullOrEmpty(req.Email) &&
                await _db.Employees.AnyAsync(e => e.Email == req.Email))
                return Conflict("Email 已存在");

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var emp = new EmployeeEntity
                {
                    Name = req.Name,
                    IdentityNumber = req.IdentityNumber,
                    Phone = req.Phone,
                    Email = req.Email,
                    EmploymentStatus = true,
                    IsAdmin = false,
                    IsSupervisor = false
                };
                _db.Employees.Add(emp);
                await _db.SaveChangesAsync();

                var (hash, salt) = PasswordHelper.HashPassword(req.Password);
                var acc = new EmployeeUserAccount
                {
                    EmployeeId = emp.EmployeeId,
                    Username = req.Username,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    IsActive = true,
                    LoginFailCount = 0,
                    LockedUntil = null,
                    LastLoginTime = null
                };
                _db.EmployeeUserAccounts.Add(acc);
                await _db.SaveChangesAsync();

                await tx.CommitAsync();
                return Ok(new { message = "註冊成功", employeeId = emp.EmployeeId, userAccountId = acc.UserAccountId, username = acc.Username });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "RegisterFull 失敗");
                return StatusCode(500, "註冊失敗，請稍後重試");
            }
        }

        // ===== 登入（Cookie 版；匿名） =====
        [AllowAnonymous]
        [HttpPost("login-cookie")]
        public async Task<IActionResult> LoginCookie([FromBody] LoginDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest("帳號或密碼不得為空");

            var account = await _db.EmployeeUserAccounts.FirstOrDefaultAsync(x => x.Username == dto.Username);
            if (account is null) return Unauthorized("帳號或密碼錯誤");
            if (account.LockedUntil.HasValue && account.LockedUntil.Value > DateTime.UtcNow)
                return Unauthorized("帳號已鎖定");

            var ok = VerifyPassword(dto.Password, account.PasswordSalt!, account.PasswordHash!);
            if (!ok) return Unauthorized("帳號或密碼錯誤");
            if (account.IsActive != true) return Unauthorized("帳號未啟用");

            var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == account.EmployeeId);
            if (emp is null) return Unauthorized();

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, account.Username ?? string.Empty),
                new Claim(ClaimTypes.NameIdentifier, emp.EmployeeId.ToString()),
                new Claim("employeeid", emp.EmployeeId.ToString()),
                // ✅ 與擴充方法對齊
                new Claim("deptid", (emp.DepartmentId ?? 0).ToString()),
                new Claim("isadmin", (emp.IsAdmin ?? false) ? "true" : "false"),
                new Claim("issupervisor", (emp.IsSupervisor ?? false) ? "true" : "false"),
                new Claim(ClaimTypes.Role, (emp.IsAdmin ?? false) ? "Admin" : "User"),
            };

            var identity = new ClaimsIdentity(claims, "EmployeeCookie");
            var principal = new ClaimsPrincipal(identity);
            var props = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync("EmployeeCookie", principal, props);

            return Ok(new
            {
                message = "員工登入成功（Cookie）",
                user = new
                {
                    Username = account.Username,
                    account.EmployeeId,
                    Name = emp.Name,
                    IsAdmin = emp.IsAdmin ?? false,
                    IsSupervisor = emp.IsSupervisor ?? false,
                    DepartmentId = emp.DepartmentId
                }
            });
        }

        [HttpPost("logout-cookie")]
        public async Task<IActionResult> LogoutCookie()
        {
            await HttpContext.SignOutAsync("EmployeeCookie");
            return Ok(new { message = "員工已登出（Cookie）" });
        }

        // ===== Me =====
        [HttpGet("me")]
        public IActionResult Me()
        {
            return Ok(new
            {
                Name = User.Identity?.Name,
                EmployeeId = User.EmployeeId(),
                DepartmentId = User.DepartmentId(),
                IsAdmin = User.IsAdmin(),
                IsSupervisor = User.IsSupervisor()
            });
        }

        // ===== 其餘 action（略）：你原本的 detail / update / photo / change password 等維持不變 =====

        private static bool VerifyPassword(string plainPassword, string base64Salt, string base64Hash)
        {
            var salt = Convert.FromBase64String(base64Salt);
            using var hmac = new System.Security.Cryptography.HMACSHA256(salt);
            var computed = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(plainPassword));
            return Convert.ToBase64String(computed) == base64Hash;
        }

        private static DateOnly? ParseDateOnly(string? yyyyMMdd)
        {
            if (string.IsNullOrWhiteSpace(yyyyMMdd)) return null;
            if (DateTime.TryParse(yyyyMMdd, out var dt))
                return DateOnly.FromDateTime(dt);
            return null;
        }
    }
}
