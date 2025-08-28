using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using prjFinalProjectApi.Helpers;                 // User.* 擴充 & PasswordHelper
using prjFinalProjectApi.Models;
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
        private readonly EmailSender _email;
        private readonly string _webBase; // 寄出的連結會用到

        public EmployeeUserAccountsController(
            DbNursingHomeContext db,
            ILogger<EmployeeUserAccountsController> logger,
            EmailSender email,
            IConfiguration cfg)
        {
            _db = db;
            _logger = logger;
            _email = email;
            _webBase = cfg["WebBaseUrl"] ?? "http://localhost:4200";
        }

        // ====== 小 DTOs ======
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
        public sealed class VerifyPasswordDto { public string OldPassword { get; set; } = string.Empty; }
        public sealed class ChangePasswordDto
        {
            public string OldPassword { get; set; } = string.Empty;
            public string NewPassword { get; set; } = string.Empty;
        }
        public sealed class ForgotPasswordDto { public string AccountOrEmail { get; set; } = string.Empty; }
        public sealed class ResetPasswordDto
        {
            public string Token { get; set; } = string.Empty;
            public string NewPassword { get; set; } = string.Empty;
        }

        // 產生 URL-safe Base64 Token（一次性）
        private static string NewToken(int bytes = 32)
        {
            var raw = RandomNumberGenerator.GetBytes(bytes);
            return Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

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
                return Ok(new
                {
                    message = "註冊成功",
                    employeeId = emp.EmployeeId,
                    userAccountId = acc.UserAccountId,
                    username = acc.Username
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "RegisterFull 失敗");
                return StatusCode(500, "註冊失敗，請稍後重試");
            }
        }

        // ===== 登入（Cookie；匿名） =====
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

        // ===== 目前身分 =====
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

        // ===== 舊密碼驗證 =====
        [HttpPost("password/verify")]
        public async Task<IActionResult> VerifyOldPassword([FromBody] VerifyPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.OldPassword)) return BadRequest("請輸入舊密碼");
            var empId = User.EmployeeId();
            if (empId <= 0) return Unauthorized("未登入");

            var account = await _db.EmployeeUserAccounts.FirstOrDefaultAsync(a => a.EmployeeId == empId);
            if (account is null || account.IsActive != true) return Unauthorized("帳號不存在或未啟用");

            var ok = VerifyPassword(dto.OldPassword, account.PasswordSalt!, account.PasswordHash!);
            if (!ok) return BadRequest("舊密碼不正確"); // 用 400 避免攔截器誤導頁

            return Ok("OK");
        }

        // ===== 修改密碼（需登入） =====
        [HttpPost("password/change")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NewPassword)) return BadRequest("請輸入新密碼");
            if (!Regex.IsMatch(dto.NewPassword, @"^[A-Za-z0-9]{4,12}$"))
                return BadRequest("新密碼需為 4–12 位英數字");

            var empId = User.EmployeeId();
            if (empId <= 0) return Unauthorized("未登入");

            var account = await _db.EmployeeUserAccounts.FirstOrDefaultAsync(a => a.EmployeeId == empId);
            if (account is null || account.IsActive != true) return Unauthorized("帳號不存在或未啟用");

            if (!string.IsNullOrWhiteSpace(dto.OldPassword))
            {
                var okOld = VerifyPassword(dto.OldPassword, account.PasswordSalt!, account.PasswordHash!);
                if (!okOld) return BadRequest("舊密碼不正確");
            }

            var (hash, salt) = PasswordHelper.HashPassword(dto.NewPassword);
            account.PasswordHash = hash;
            account.PasswordSalt = salt;
            account.LoginFailCount = 0;
            account.LockedUntil = null;

            await _db.SaveChangesAsync();
            await HttpContext.SignOutAsync("EmployeeCookie"); // 改密碼後強制登出
            return Ok("密碼已變更");
        }

        // ===== 忘記密碼：寄出重設信（匿名） =====
        [AllowAnonymous]
        [HttpPost("password/forgot")]
        public async Task<IActionResult> Forgot([FromBody] ForgotPasswordDto dto)
        {
            var key = (dto?.AccountOrEmail ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(key)) return BadRequest("請輸入帳號或 Email");

            // 以帳號或 Email 尋找帳號
            var acc = await _db.EmployeeUserAccounts.FirstOrDefaultAsync(a => a.Username == key);
            if (acc == null)
            {
                acc = await (from a in _db.EmployeeUserAccounts
                             join e in _db.Employees on a.EmployeeId equals e.EmployeeId
                             where e.Email == key
                             select a).FirstOrDefaultAsync();
            }

            // 無論是否找到，都回 OK（避免暴力探測）
            if (acc == null) return Ok(new { message = "若資料存在，已寄出重設信。" });

            var email = await _db.Employees
                .Where(e => e.EmployeeId == acc.EmployeeId)
                .Select(e => e.Email)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(email))
                return Ok(new { message = "若資料存在，已寄出重設信。" });

            // 建立一次性 token（30 分鐘）
            var token = NewToken(32);
            var now = DateTime.UtcNow;

            var req = new EmployeePasswordResetRequest
            {
                Username = acc.Username!,
                Token = token,
                RequestedTime = now,
                ExpireTime = now.AddMinutes(30),
                IsUsed = false
            };
            _db.EmployeePasswordResetRequests.Add(req);
            await _db.SaveChangesAsync();

            // 前端頁面：employeesetnewpassword（你指定的名稱）
            var link = $"{_webBase}/erp/employeesetnewpassword?token={token}";

            var subject = "員工密碼重設連結";
            var html = $@"
<p>您（或他人）要求重設密碼，請在 30 分鐘內點擊下列連結：</p>
<p><a href=""{link}"">{link}</a></p>
<p>若非您本人操作，請忽略本信。</p>";

            try { await _email.SendAsync(email, subject, html); }
            catch (Exception ex) { _logger.LogError(ex, "寄送重設密碼 Email 失敗"); }

            return Ok(new { message = "若資料存在，已寄出重設信。" });
        }

        // ===== 依 Token 重設密碼（匿名） =====
        [AllowAnonymous]
        [HttpPost("password/reset")]
        public async Task<IActionResult> Reset([FromBody] ResetPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest("參數有誤");
            if (!Regex.IsMatch(dto.NewPassword, "^[A-Za-z0-9]{4,12}$"))
                return BadRequest("新密碼需為 4–12 位英數字");

            var now = DateTime.UtcNow;

            var req = await _db.EmployeePasswordResetRequests
                .Where(r => r.Token == dto.Token && r.IsUsed == false && r.ExpireTime > now)
                .FirstOrDefaultAsync();

            if (req == null) return BadRequest("連結無效或已過期");

            var acc = await _db.EmployeeUserAccounts.FirstOrDefaultAsync(a => a.Username == req.Username);
            if (acc == null) return BadRequest("帳號不存在");

            // 統一使用 PasswordHelper
            var (hash, salt) = PasswordHelper.HashPassword(dto.NewPassword);
            acc.PasswordHash = hash;
            acc.PasswordSalt = salt;
            acc.LoginFailCount = 0;
            acc.LockedUntil = null;

            req.IsUsed = true;

            await _db.SaveChangesAsync();
            return Ok(new { message = "密碼已更新，請重新登入。" });
        }

        // ===== Helpers =====
        private static bool VerifyPassword(string plainPassword, string base64Salt, string base64Hash)
        {
            var salt = Convert.FromBase64String(base64Salt);
            using var hmac = new HMACSHA256(salt);
            var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(plainPassword));
            return Convert.ToBase64String(computed) == base64Hash;
        }
    }
}
