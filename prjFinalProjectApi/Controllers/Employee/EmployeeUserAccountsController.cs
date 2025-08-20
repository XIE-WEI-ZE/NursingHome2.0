// Controllers/Employee/EmployeeUserAccountsController.cs
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

    // ========= DTOs（就近放在同檔，減少新增檔案） =========
    public class RegisterFullRequest
    {
        // Employee 欄位
        public string Name { get; set; } = string.Empty;
        public string IdentityNumber { get; set; } = string.Empty; // 身分證/識別碼
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        // Account 欄位
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
        public string Token { get; set; } = string.Empty; // JWT
        public int ExpiresIn { get; set; }               // 秒
        public object? User { get; set; }                // 可回傳基本使用者資訊
    }

    // ========= 註冊（保持你原本流程） =========
    [AllowAnonymous]
    [HttpPost("register-full")]
    public async Task<IActionResult> RegisterFull([FromBody] RegisterFullRequest req)
    {
        // 1) 基本檢查
        if (string.IsNullOrWhiteSpace(req.Name) ||
            string.IsNullOrWhiteSpace(req.IdentityNumber) ||
            string.IsNullOrWhiteSpace(req.Email) ||
            string.IsNullOrWhiteSpace(req.Username) ||
            string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest("請填寫必填欄位：姓名、身分證、Email、帳號、密碼。");
        }

        // 2) 互斥檢查
        if (await _db.EmployeeUserAccounts.AnyAsync(u => u.Username == req.Username))
            return Conflict("帳號已存在");

        if (await _db.Employees.AnyAsync(e => e.IdentityNumber == req.IdentityNumber))
            return Conflict("身分證已存在");

        if (!string.IsNullOrEmpty(req.Email) &&
            await _db.Employees.AnyAsync(e => e.Email == req.Email))
            return Conflict("Email 已存在");

        // 3) 建立（交易確保全有或全無）
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 3-1 建 Employee（讓 DB 自動產生 EmployeeId）
            var emp = new Employee
            {
                Name = req.Name,
                IdentityNumber = req.IdentityNumber,
                Phone = req.Phone,
                Email = req.Email,

                EmploymentStatus = true, // 在職
                IsAdmin = false,
                IsSupervisor = false
            };

            _db.Employees.Add(emp);
            await _db.SaveChangesAsync(); // 這一步後，emp.EmployeeId 就有值

            // 3-2 建 Account，綁到剛剛自動產生的 EmployeeId
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

    // ========= 登入（簽發 JWT，含 Role=Employee） =========
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResultDto>> Login([FromBody] LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("帳號或密碼不得為空");

        var account = await _db.EmployeeUserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == dto.Username);

        if (account is null)
            return Unauthorized("帳號或密碼錯誤");

        if (account.LockedUntil.HasValue && account.LockedUntil.Value > DateTime.UtcNow)
            return Unauthorized("帳號已鎖定，請稍後再試");

        var ok = VerifyPassword(dto.Password, account.PasswordSalt, account.PasswordHash);
        if (!ok)
            return Unauthorized("帳號或密碼錯誤");

        if (account.IsActive != true)
            return Unauthorized("帳號未啟用");

        // 讀出員工（拿角色旗標）
        var emp = await _db.Employees.AsNoTracking()
                     .FirstOrDefaultAsync(e => e.EmployeeId == account.EmployeeId);

        bool isAdmin = emp?.IsAdmin == true;
        bool isSupervisor = emp?.IsSupervisor == true;

        // 簽發 JWT（含多角色）
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
                Roles = roles           // 回傳目前角色清單，方便前端/你檢查
            }
        });
    }


    // ========= 受保護測試用端點 =========
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            Name = User.Identity?.Name,
            Roles = User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList()
        });
    }


    // ========= 小工具：驗證密碼（HMACSHA256 + Salt/Base64） =========
    private static bool VerifyPassword(string plainPassword, string base64Salt, string base64Hash)
    {
        // ⚠ 若你的 PasswordHelper 使用 PBKDF2/BCrypt/Argon2，請改成對應驗證方式
        var salt = Convert.FromBase64String(base64Salt);
        using var hmac = new HMACSHA256(salt);
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(plainPassword));
        var computedBase64 = Convert.ToBase64String(computed);
        return computedBase64 == base64Hash;
    }

    // ========= 小工具：簽發 JWT（加上 Role=Employee） =========
    /// <summary>
    /// 簽發員工 JWT。基本角色一定有 "Employee"；若旗標為 true，追加 "Admin" / "Supervisor"
    /// </summary>
    private (string token, int expiresInSeconds, List<string> roles)
        IssueJwtForEmployee(string username, string userId, bool isAdmin, bool isSupervisor)
    {
        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpireMinutes);

        // 準備角色清單
        var roles = new List<string> { "Employee" };
        if (isAdmin) roles.Add("Admin");
        if (isSupervisor) roles.Add("Supervisor");

        // 基本 claims
        var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, username),
        new Claim(ClaimTypes.NameIdentifier, userId),
    };

        // 多角色：一個角色一個 ClaimTypes.Role
        foreach (var r in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }

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
        var expiresIn = (int)(expires - DateTime.UtcNow).TotalSeconds;
        return (token, expiresIn, roles);
    }
    [HttpGet("{id}/detail")]
    public async Task<ActionResult<EmployeeDetailDto>> GetEmployeeDetail(int id)
    {
        // 若你有部門/職稱資料表
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
           BirthDate = e.BirthDate.HasValue
              ? e.BirthDate.Value.ToDateTime(TimeOnly.MinValue)
              : (DateTime?)null,
           Phone = e.Phone,
                Email = e.Email,
                EducationLevel = e.EducationLevel,
                RegisteredAddress = e.RegisteredAddress,
                CurrentAddress = e.CurrentAddress,
                Height = e.Height,
                Weight = e.Weight,
                PayrollBankAccount = e.PayrollBankAccount,
                PhotoPath = e.PhotoPath,

           EmploymentStatusText = e.EmploymentStatus.HasValue
    ? (e.EmploymentStatus.Value ? "在職" : "離職")
    : "未知",
           DepartmentName = d != null ? d.DepartmentName : null,
                JobTitleName = j != null ? j.TitleName : null,
           HireDate = e.HireDate.HasValue
              ? e.HireDate.Value.ToDateTime(TimeOnly.MinValue)
              : (DateTime?)null,
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


}
