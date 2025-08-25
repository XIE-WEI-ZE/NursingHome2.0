using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using prjFinalProjectApi.Helpers;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.IO;

// ✅ 避免 Controllers.Employee 和 Models.Employee 衝突
using EmployeeEntity = prjFinalProjectApi.Models.Employee;

[ApiController]
[Route("api/[controller]")]
public class EmployeeUserAccountsController : ControllerBase
{
    private readonly DbNursingHomeContext _db;
    private readonly ILogger<EmployeeUserAccountsController> _logger;
    private readonly JwtOptions _jwt;

    public EmployeeUserAccountsController(
        DbNursingHomeContext db,
        ILogger<EmployeeUserAccountsController> logger,
        IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _logger = logger;
        _jwt = jwtOptions.Value;
    }

    // ========= DTOs =========
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

    public class LoginResultDto
    {
        public string Token { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public object? User { get; set; }
    }

    // ⬇️ 新增：前端「編輯頁」送進來的 camelCase DTO（方案 A 對應）
    public sealed class UpdateEmployeeDetailApi
    {
        public int employeeId { get; set; }

        public string name { get; set; } = string.Empty;
        public string identityNumber { get; set; } = string.Empty;
        public string? birthDate { get; set; }     // yyyy-MM-dd
        public string? phone { get; set; }
        public string? email { get; set; }
        public string? educationLevel { get; set; }
        public string? registeredAddress { get; set; }
        public string? currentAddress { get; set; }
        public int? height { get; set; }
        public int? weight { get; set; }
        public string? payrollBankAccount { get; set; }

        // 職務（名稱僅示意，如需改以 Id 作為主，前端可以改成傳 id）
        public string? employmentStatusText { get; set; }   // 在職/離職
        public string? departmentName { get; set; }
        public string? jobTitleName { get; set; }
        public string? hireDate { get; set; }               // yyyy-MM-dd
        public bool policeClearanceCertified { get; set; }
        public bool isSupervisor { get; set; }
        public bool isAdmin { get; set; }

        // 緊急聯絡人
        public string? emergencyContactPerson { get; set; }
        public string? emergencyContactPhone { get; set; }
        public string? emergencyContactRelationship { get; set; }
    }

    // ========= 註冊 =========
    [AllowAnonymous]
    [HttpPost("register-full")]
    public async Task<IActionResult> RegisterFull([FromBody] RegisterFullRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) ||
            string.IsNullOrWhiteSpace(req.IdentityNumber) ||
            string.IsNullOrWhiteSpace(req.Email) ||
            string.IsNullOrWhiteSpace(req.Username) ||
            string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest("請填寫必填欄位：姓名、身分證、Email、帳號、密碼。");
        }

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
            // ✅ 使用 EmployeeEntity
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

    // ========= 登入 =========
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResultDto>> Login([FromBody] LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("帳號或密碼不得為空");

        var account = await _db.EmployeeUserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == dto.Username);

        if (account is null) return Unauthorized("帳號或密碼錯誤");

        if (account.LockedUntil.HasValue && account.LockedUntil.Value > DateTime.UtcNow)
            return Unauthorized("帳號已鎖定");

        var ok = VerifyPassword(dto.Password, account.PasswordSalt, account.PasswordHash);
        if (!ok) return Unauthorized("帳號或密碼錯誤");

        if (account.IsActive != true) return Unauthorized("帳號未啟用");

        var emp = await _db.Employees.AsNoTracking()
                     .FirstOrDefaultAsync(e => e.EmployeeId == account.EmployeeId);

        bool isAdmin = emp?.IsAdmin == true;
        bool isSupervisor = emp?.IsSupervisor == true;

        var (token, expires, roles) =
            IssueJwtForEmployee(account.Username, account.EmployeeId.ToString(), isAdmin, isSupervisor);

        return Ok(new LoginResultDto
        {
            Token = token,
            ExpiresIn = expires,
            User = new
            {
                account.Username,
                account.EmployeeId,
                Name = emp?.Name,
                Roles = roles
            }
        });
    }

    // ========= Me =========
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            Name = User.Identity?.Name,
            Roles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList()
        });
    }

    // ========= 驗證密碼 =========
    private static bool VerifyPassword(string plainPassword, string base64Salt, string base64Hash)
    {
        var salt = Convert.FromBase64String(base64Salt);
        using var hmac = new HMACSHA256(salt);
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(plainPassword));
        return Convert.ToBase64String(computed) == base64Hash;
    }

    // ========= 簽發 JWT =========
    private (string token, int expiresInSeconds, List<string> roles)
        IssueJwtForEmployee(string username, string userId, bool isAdmin, bool isSupervisor)
    {
        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpireMinutes);
        var roles = new List<string> { "Employee" };
        if (isAdmin) roles.Add("Admin");
        if (isSupervisor) roles.Add("Supervisor");

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.NameIdentifier, userId),
        };
        foreach (var r in roles)
            claims.Add(new Claim(ClaimTypes.Role, r));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return (token, (int)(expires - DateTime.UtcNow).TotalSeconds, roles);
    }

    // ========= 員工詳細（讀取） =========
    [HttpGet("{id}/detail")]
    public async Task<ActionResult<EmployeeDetailDto>> GetEmployeeDetail(int id)
    {
        var query =
        from e in _db.Employees.AsNoTracking()
        where e.EmployeeId == id
        join d in _db.EmployeeDepartments.AsNoTracking()
            on e.DepartmentId equals d.DepartmentId into d1
        from d in d1.DefaultIfEmpty()
        join j in _db.EmployeeJobTitles.AsNoTracking()
            on e.JobTitleId equals j.JobTitleId into j1
        from j in j1.DefaultIfEmpty()
        select new EmployeeDetailDto
        {
            EmployeeId = e.EmployeeId,
            Name = e.Name,
            IdentityNumber = e.IdentityNumber,
            BirthDate = e.BirthDate.HasValue ? e.BirthDate.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
            Phone = e.Phone,
            Email = e.Email,
            EducationLevel = e.EducationLevel,
            RegisteredAddress = e.RegisteredAddress,
            CurrentAddress = e.CurrentAddress,
            Height = e.Height,
            Weight = e.Weight,
            PayrollBankAccount = e.PayrollBankAccount,
            PhotoPath = e.PhotoPath,
            EmploymentStatusText = e.EmploymentStatus.HasValue ? (e.EmploymentStatus.Value ? "在職" : "離職") : "未知",
            DepartmentName = d != null ? d.DepartmentName : null,
            JobTitleName = j != null ? j.TitleName : null,
            HireDate = e.HireDate.HasValue ? e.HireDate.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
            PoliceClearanceCertified = e.PoliceClearanceCertified,
            IsSupervisor = e.IsSupervisor,
            IsAdmin = e.IsAdmin,
            EmergencyContactPerson = e.EmergencyContactPerson,
            EmergencyContactPhone = e.EmergencyContactPhone,
            EmergencyContactRelationship = e.EmergencyContactRelationship
        };

        var dto = await query.FirstOrDefaultAsync();
        if (dto == null) return NotFound();
        return dto;
    }

    // ========= 修改密碼 =========
    [Authorize]
    [HttpPost("password/change")]
    public async Task<IActionResult> ChangePassword([FromBody] EmployeeChangePasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldPassword) || string.IsNullOrWhiteSpace(dto.NewPassword))
            return BadRequest("密碼不可為空");

        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username)) return Unauthorized();

        var account = await _db.EmployeeUserAccounts.SingleOrDefaultAsync(u => u.Username == username);
        if (account is null) return Unauthorized();

        if (account.LockedUntil.HasValue && account.LockedUntil.Value > DateTime.UtcNow)
            return StatusCode(423, "帳號暫時被鎖定");

        var ok = VerifyPassword(dto.OldPassword, account.PasswordSalt!, account.PasswordHash!);
        if (!ok)
        {
            account.LoginFailCount = (account.LoginFailCount ?? 0) + 1;
            if (account.LoginFailCount >= 5)
            {
                account.LockedUntil = DateTime.UtcNow.AddMinutes(10);
                account.LoginFailCount = 0;
            }
            await _db.SaveChangesAsync();
            return BadRequest("舊密碼錯誤");
        }

        // ✅ 密碼規則：4–12 位英數字
        if (dto.NewPassword.Length < 4 || dto.NewPassword.Length > 12)
            return BadRequest("新密碼需為 4–12 位英數字");

        var (newHash, newSalt) = PasswordHelper.HashPassword(dto.NewPassword);
        account.PasswordHash = newHash;
        account.PasswordSalt = newSalt;
        account.LoginFailCount = 0;
        account.LockedUntil = null;
        account.LastLoginTime = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ========= 驗證舊密碼 =========
    [Authorize]
    [HttpPost("password/verify")]
    public async Task<IActionResult> VerifyOldPassword([FromBody] EmployeeChangePasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldPassword))
            return BadRequest("密碼不可為空");

        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username)) return Unauthorized();

        var account = await _db.EmployeeUserAccounts.SingleOrDefaultAsync(u => u.Username == username);
        if (account is null) return Unauthorized();

        if (account.LockedUntil.HasValue && account.LockedUntil.Value > DateTime.UtcNow)
            return StatusCode(423, "帳號暫時被鎖定");

        var ok = VerifyPassword(dto.OldPassword, account.PasswordSalt!, account.PasswordHash!);
        if (!ok)
        {
            account.LoginFailCount = (account.LoginFailCount ?? 0) + 1;
            if (account.LoginFailCount >= 5)
            {
                account.LockedUntil = DateTime.UtcNow.AddMinutes(10);
                account.LoginFailCount = 0;
            }
            await _db.SaveChangesAsync();
            return BadRequest("舊密碼錯誤");
        }

        // ✅ 舊密碼正確，直接回 204
        return NoContent();
    }

    // ========= 更新員工詳細（維持 /{id}/detail） =========
    [Authorize]
    [HttpPut("{id:int}/detail")]
    public async Task<IActionResult> UpdateDetail(int id, [FromBody] UpdateEmployeeDetailApi dto)
    {
        if (dto is null || id != dto.employeeId)
            return BadRequest("Invalid payload.");

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == id);
        if (emp is null) return NotFound();

        // 基本資料
        emp.Name = dto.name?.Trim() ?? emp.Name;
        emp.IdentityNumber = dto.identityNumber?.Trim() ?? emp.IdentityNumber;
        emp.Phone = dto.phone?.Trim();
        emp.Email = dto.email?.Trim();
        emp.EducationLevel = dto.educationLevel?.Trim();
        emp.RegisteredAddress = dto.registeredAddress?.Trim();
        emp.CurrentAddress = dto.currentAddress?.Trim();
        emp.Height = dto.height;
        emp.Weight = dto.weight;
        emp.PayrollBankAccount = dto.payrollBankAccount?.Trim();

        // 生日/到職 - 你的欄位是 DateOnly?，這裡做字串轉換
        emp.BirthDate = ParseDateOnly(dto.birthDate);
        emp.HireDate = ParseDateOnly(dto.hireDate);

        // 在職狀態（字串轉 bool；空白就不動）
        if (!string.IsNullOrWhiteSpace(dto.employmentStatusText))
        {
            emp.EmploymentStatus = dto.employmentStatusText!.Trim() == "在職"
                ? true
                : dto.employmentStatusText!.Trim() == "離職"
                    ? false
                    : emp.EmploymentStatus;
        }

        // 角色與證照
        emp.PoliceClearanceCertified = dto.policeClearanceCertified;
        emp.IsSupervisor = dto.isSupervisor;
        emp.IsAdmin = dto.isAdmin;

        // 緊急聯絡人
        emp.EmergencyContactPerson = dto.emergencyContactPerson?.Trim();
        emp.EmergencyContactPhone = dto.emergencyContactPhone?.Trim();
        emp.EmergencyContactRelationship = dto.emergencyContactRelationship?.Trim();

        // 部門 / 職稱（用名稱反查 Id；若找不到則不變更）
        if (!string.IsNullOrWhiteSpace(dto.departmentName))
        {
            var dept = await _db.EmployeeDepartments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DepartmentName == dto.departmentName);
            if (dept != null) emp.DepartmentId = dept.DepartmentId;
        }
        if (!string.IsNullOrWhiteSpace(dto.jobTitleName))
        {
            var job = await _db.EmployeeJobTitles
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.TitleName == dto.jobTitleName);
            if (job != null) emp.JobTitleId = job.JobTitleId;
        }

        await _db.SaveChangesAsync();
        return NoContent(); // 204
    }

    // ========= 上傳員工頭像（儲存並回寫 PhotoPath） =========
    [Authorize]
    [HttpPost("{id:int}/photo")]
    [RequestSizeLimit(10_000_000)] // 10MB
    public async Task<IActionResult> UploadPhoto(int id, IFormFile photo)
    {
        if (photo == null || photo.Length == 0) return BadRequest("No file.");
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == id);
        if (emp is null) return NotFound();

        // wwwroot/uploads/employees
        var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "employees");
        if (!Directory.Exists(uploadsRoot)) Directory.CreateDirectory(uploadsRoot);

        var ext = Path.GetExtension(photo.FileName);
        var fileName = $"{id}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
        var fullPath = Path.Combine(uploadsRoot, fileName);

        await using (var fs = System.IO.File.Create(fullPath))
        {
            await photo.CopyToAsync(fs);
        }

        // 前端可直接用這個相對路徑存取（Program.cs 要有 UseStaticFiles）
        var relativePath = $"/uploads/employees/{fileName}";

        emp.PhotoPath = relativePath;
        await _db.SaveChangesAsync();

        return Ok(new { path = relativePath });
    }

    // ========= Private helpers =========
    private static DateOnly? ParseDateOnly(string? yyyyMMdd)
    {
        if (string.IsNullOrWhiteSpace(yyyyMMdd)) return null;
        if (DateTime.TryParse(yyyyMMdd, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }
}
