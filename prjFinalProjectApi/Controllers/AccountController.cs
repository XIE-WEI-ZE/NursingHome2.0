using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Helpers;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using prjFinalProjectApi.Models.Dtos;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly OneTimeTokenHelper _ott;

        //  連錯門檻與鎖定秒數（可調）
        private const int MAX_FAILED_ATTEMPTS = 5;
        private const int LOCK_SECONDS = 10;

        public AccountController(
            DbNursingHomeContext context,
            IWebHostEnvironment env,
            IConfiguration config,
            OneTimeTokenHelper ott)
        {
            _context = context;
            _env = env;
            _config = config;
            _ott = ott;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromForm] RegisterDto dto)
        {
            if (await _context.Members.AnyAsync(m => m.FAccount == dto.Account))
                return BadRequest("帳號已存在");

            if (dto.Password != dto.ConfirmPassword)
                return BadRequest("密碼不一致");

            byte[] salt = GenerateSalt();
            string hashedPassword = HashPassword(dto.Password, salt);

            string? fileName = null;
            if (dto.Photo != null)
            {
                string uploadPath = Path.Combine(_env.WebRootPath!, "images", "members");
                if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

                fileName = $"{Guid.NewGuid()}{Path.GetExtension(dto.Photo.FileName)}";
                string filePath = Path.Combine(uploadPath, fileName);

                using var fileStream = new FileStream(filePath, FileMode.Create);
                await dto.Photo.CopyToAsync(fileStream);
            }

            var member = new Member
            {
                FAccount = dto.Account,
                FPasswordHash = hashedPassword,
                FPasswordSalt = Convert.ToBase64String(salt),
                FEmail = dto.Email,
                FName = dto.Name,
                FGender = dto.Gender,
                FPhone = dto.Phone,
                FBirthDate = dto.BirthDate != null ? DateOnly.FromDateTime(dto.BirthDate.Value) : null,
                FProfilePictureUrl = fileName,
                FAccountStatus = true,
                FCreatedAt = DateTime.Now
            };

            _context.Members.Add(member);
            await _context.SaveChangesAsync();

            return Ok(new { message = "註冊成功" });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var member = _context.Members.FirstOrDefault(m => m.FAccount == dto.Account);

            if (member == null)
            {
                LogSecurityEvent(null, "LoginFailed", $"帳號不存在：{dto.Account}");
                return Unauthorized(new { message = "帳號不存在" });
            }

            // ★ 先檢查是否在鎖定中（依安全日誌）
            if (IsLockedOut(member.FMemberId, out int secondsLeft))
            {
                LogSecurityEvent(member.FMemberId, "LoginBlocked_LockoutHit", $"剩餘 {secondsLeft} 秒");
                return StatusCode(429, new { message = $"嘗試過多，請 {secondsLeft} 秒後再試" });
            }

            // ★ 停權不可登入（bool? 安全處理）
            if (!member.FAccountStatus.GetValueOrDefault())
            {
                LogSecurityEvent(member.FMemberId, "LoginBlocked_Disabled", "帳號已停權");
                return Unauthorized(new { message = "帳號已停權，請聯絡管理員" });
            }

            if (!VerifyPassword(dto.Password, member.FPasswordHash, member.FPasswordSalt))
            {
                // 先記一筆失敗
                LogSecurityEvent(member.FMemberId, "LoginFailed", "密碼錯誤");

                // ★ 計算「連續」失敗次數（從最近一筆往回，遇到成功/鎖定就停止）
                var fails = CountConsecutiveFailures(member.FMemberId);

                // ★ 達門檻 → 觸發鎖定 LOG，並回 429（Too Many Requests）
                if (fails >= MAX_FAILED_ATTEMPTS)
                {
                    await StartLockoutAsync(member.FMemberId);
                    return StatusCode(429, new { message = $"嘗試過多，請 {LOCK_SECONDS} 秒後再試" });
                }

                return Unauthorized(new { message = "密碼錯誤" });
            }

            //  登入成功產生 Token
            var token = JwtHelper.GenerateToken(
                member.FMemberId,
                member.FAccount,
                member.FEmail,
                _config["Jwt:Key"],
                _config["Jwt:Issuer"],
                _config["Jwt:Audience"],
                60
            );

            //  登入成功紀錄（成功會讓後續的「連續失敗」鏈條中斷）
            LogSecurityEvent(member.FMemberId, "LoginSuccess", "登入成功");

            await SendEmailAsync(
                member.FEmail!,
                "登入成功通知",
                $"<h3>親愛的 {member.FName}，您好！</h3><p>您已於 {DateTime.Now:yyyy/MM/dd HH:mm:ss} 成功登入系統。</p>" +
                $" <p>若非您本人操作，請立即聯絡系統管理員。</p>\n  <hr/>\n  <small>本信件為系統自動通知，請勿回覆</small>"
            );

            return Ok(new
            {
                message = "登入成功",
                token,
                memberId = member.FMemberId,
                name = member.FName
            });
        }

        // 產生隨機鹽
        private static byte[] GenerateSalt(int size = 16)
        {
            return RandomNumberGenerator.GetBytes(size);
        }

        // 雜湊密碼
        private static string HashPassword(string password, byte[] salt)
        {
            byte[] hashed = KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 10000,
                numBytesRequested: 32);
            return Convert.ToBase64String(hashed);
        }

        // 驗證密碼
        private static bool VerifyPassword(string inputPassword, string storedHash, string storedSalt)
        {
            byte[] saltBytes = Convert.FromBase64String(storedSalt);
            string hashOfInput = HashPassword(inputPassword, saltBytes);
            return hashOfInput == storedHash;
        }

        [HttpPost("logout")]
        [Authorize]
        public IActionResult Logout()
        {
            var account = User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrEmpty(account))
                return Unauthorized(new { message = "找不到登入資訊" });

            var member = _context.Members.FirstOrDefault(m => m.FAccount == account);
            if (member != null)
            {
                LogSecurityEvent(member.FMemberId, "Logout", "使用者登出");
            }

            return Ok(new { message = "登出成功" });
        }

        [HttpPost("google-login")]
        public async Task<IActionResult> GoogleLogin([FromBody] JsonElement json)
        {
            try
            {
                var idToken = json.GetRawText().Trim('"');

                var payload = await Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync(idToken);
                var email = payload.Email;
                var name = payload.Name;
                var externalId = payload.Subject;

                var member = await _context.Members.FirstOrDefaultAsync(m => m.FEmail == email);

                if (member == null)
                {
                    member = new Member
                    {
                        FEmail = email,
                        FName = name,
                        FLoginProvider = "Google",
                        FAccount = "google_" + Guid.NewGuid().ToString("N").Substring(0, 10),
                        FAccountStatus = true,
                        FCreatedAt = DateTime.Now,
                        FExternalId = externalId
                    };

                    _context.Members.Add(member);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    if (string.IsNullOrEmpty(member.FLoginProvider))
                    {
                        member.FLoginProvider = "Google";
                        member.FExternalId = externalId;
                        await _context.SaveChangesAsync();
                    }
                }

                // ★ 停權檢查（第三方也要擋）
                if (!await IsMemberActiveAsync(member))
                    return Unauthorized(new { message = "帳號已停權，請聯絡管理員" });

                var token = JwtHelper.GenerateToken(
                    member.FMemberId,
                    member.FAccount,
                    member.FEmail,
                    _config["Jwt:Key"],
                    _config["Jwt:Issuer"],
                    _config["Jwt:Audience"],
                    60
                );

                await LogSecurityEvent(member.FMemberId, "LoginSuccess", "Google 登入成功（帳號整合）");

                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                if (ip == "::1") ip = "127.0.0.1";

                await SendEmailAsync(
                    member.FEmail!,
                    "Google 登入通知",
                    $"""
                    <h3>親愛的 {member.FName}，您好：</h3>
                    <p>您已於 <strong>{DateTime.Now:yyyy/MM/dd HH:mm:ss}</strong> 成功使用 <span style='color:green;'>Google 第三方登入</span>。</p>
                      <p><strong>登入 IP 位址：</strong> {ip}</p>
                      <p>若非您本人操作，請立即聯絡系統管理員。</p>
                      <hr/>
                      <small>本信件為系統自動通知，請勿回覆</small>
                    """
                 );

                return Ok(new
                {
                    token,
                    message = "Google 登入成功",
                    memberId = member.FMemberId,
                    name = member.FName
                });
            }
            catch (Exception ex)
            {
                await LogSecurityEvent(null, "LoginFailed", $"Google 登入失敗：{ex.Message}");

                return BadRequest(new
                {
                    message = "Google 登入失敗",
                    error = ex.Message
                });
            }
        }

        private async Task LogSecurityEvent(int? memberId, string eventType, string? notes)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                if (ip == "::1") ip = "127.0.0.1";

                eventType = string.IsNullOrEmpty(eventType) ? null :
                            eventType.Length > 50 ? eventType.Substring(0, 50) : eventType;

                notes = string.IsNullOrEmpty(notes) ? null :
                        notes.Length > 200 ? notes.Substring(0, 200) : notes;

                ip = string.IsNullOrEmpty(ip) ? null :
                     ip.Length > 200 ? ip.Substring(0, 200) : ip;

                using var db = new DbNursingHomeContext();
                var log = new MemberSecurityLog
                {
                    FMemberId = memberId,
                    FEventType = eventType,
                    FNotes = notes,
                    FIpAddress = ip,
                    FCreatedAt = DateTime.Now
                };

                db.MemberSecurityLogs.Add(log);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LogSecurityEvent 錯誤 : {ex.Message}");
            }
        }

        [HttpGet("security-logs")]
        [Authorize]
        public IActionResult GetSecurityLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 5)
        {
            var account = User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrEmpty(account))
                return Unauthorized(new { message = "找不到登入資訊" });

            var member = _context.Members.FirstOrDefault(m => m.FAccount == account);
            if (member == null)
                return NotFound(new { message = "會員不存在" });

            var query = _context.MemberSecurityLogs
                .Where(log => log.FMemberId == member.FMemberId)
                .OrderByDescending(log => log.FCreatedAt)
                .Take(30);

            var totalCount = query.Count();
            var logs = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(log => new SecurityLogDto
                {
                    EventType = log.FEventType,
                    IpAddress = log.FIpAddress,
                    CreatedAt = log.FCreatedAt
                })
                .ToList();

            return Ok(new
            {
                totalCount,
                page,
                pageSize,
                logs
            });
        }

        private async Task<bool> SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                var smtpConfig = _config.GetSection("Smtp");
                var smtpHost = smtpConfig["Host"];
                var smtpPort = int.Parse(smtpConfig["Port"]);
                var smtpAccount = smtpConfig["Account"];
                var smtpPassword = smtpConfig["Password"];
                var fromName = smtpConfig["FromName"];

                var message = new System.Net.Mail.MailMessage();
                message.From = new System.Net.Mail.MailAddress(smtpAccount, fromName);
                message.To.Add(toEmail);
                message.Subject = subject;
                message.Body = body;
                message.IsBodyHtml = true;

                using (var client = new System.Net.Mail.SmtpClient(smtpHost, smtpPort))
                {
                    client.Credentials = new System.Net.NetworkCredential(smtpAccount, smtpPassword);
                    client.EnableSsl = true;
                    await client.SendMailAsync(message);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"寄信錯誤: {ex.Message}");
                return false;
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var member = await _context.Members.FirstOrDefaultAsync(m => m.FEmail == dto.Email);
            if (member == null)
                return BadRequest(new { message = "查無此 Email" });

            string token = _ott.CreateToken("PasswordReset", member.FMemberId, minutes: 30);

            string resetLink = $"http://localhost:4200/show/reset-password?token={Uri.EscapeDataString(token)}";

            string subject = "重設密碼通知";
            string body = $@"
            <h3>親愛的 {member.FName}，您好：</h3>
            <p>請於 30 分鐘內點擊以下連結重設您的密碼：</p>
            <p><a href='{resetLink}'>{resetLink}</a></p>
            <p>若您沒有請求此操作，請忽略此信。</p>";

            await SendEmailAsync(member.FEmail!, subject, body);
            return Ok(new { message = "密碼重設連結已寄出，請查收您的信箱。" });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Token))
                return BadRequest(new { message = "缺少驗證資訊" });

            var memberId = _ott.ValidateAndGetMemberId("PasswordReset", dto.Token);
            if (memberId == null)
                return Unauthorized(new { message = "重設連結無效或已過期" });

            if (dto.NewPassword != dto.ConfirmPassword)
                return BadRequest(new { message = "兩次密碼不一致" });

            var member = await _context.Members.FirstOrDefaultAsync(m => m.FMemberId == memberId.Value);
            if (member == null)
                return NotFound(new { message = "會員不存在" });

            byte[] salt = GenerateSalt();
            string hashedPassword = HashPassword(dto.NewPassword, salt);

            member.FPasswordSalt = Convert.ToBase64String(salt);
            member.FPasswordHash = hashedPassword;
            await _context.SaveChangesAsync();

            return Ok(new { message = "密碼已成功重設" });
        }

        private async Task<bool> IsMemberActiveAsync(Member member)
        {
            if (member == null) return false;
            if (member.FAccountStatus.GetValueOrDefault()) return true;

            await LogSecurityEvent(member.FMemberId, "LoginBlocked_Disabled", "帳號已停權");
            return false;
        }

        // ================== 鎖定機制（利用安全日誌） ==================

        // 是否仍在鎖定期：找最近一次觸發鎖定的時間，若未滿 LOCK_SECONDS 則仍鎖
        private bool IsLockedOut(int memberId, out int secondsLeft)
        {
            secondsLeft = 0;

            var lastBlock = _context.MemberSecurityLogs
                .Where(x => x.FMemberId == memberId
                         && x.FEventType == "LoginBlocked_Lockout"
                         && x.FCreatedAt != null)              // 先確保有時間
                .OrderByDescending(x => x.FCreatedAt)
                .FirstOrDefault();

            if (lastBlock == null) return false;
            if (!lastBlock.FCreatedAt.HasValue) return false;   // 可為 null 要擋

            var createdAt = lastBlock.FCreatedAt.Value;         // 轉成 DateTime
            var until = createdAt.AddSeconds(LOCK_SECONDS);
            var now = DateTime.Now;

            if (until > now)
            {
                secondsLeft = (int)Math.Ceiling((until - now).TotalSeconds);
                return true;
            }
            return false;
        }

        // 計算「連續」失敗：從最近往回掃，遇到成功或鎖定事件即停止
        private int CountConsecutiveFailures(int memberId)
        {
            var logs = _context.MemberSecurityLogs
                .Where(x => x.FMemberId == memberId &&
                            (x.FEventType == "LoginFailed" ||
                             x.FEventType == "LoginSuccess" ||
                             x.FEventType == "LoginBlocked_Lockout"))
                .OrderByDescending(x => x.FCreatedAt)
                .Take(MAX_FAILED_ATTEMPTS + 10) // 多抓幾筆以免不夠
                .ToList();

            int count = 0;
            foreach (var log in logs)
            {
                if (log.FEventType == "LoginFailed")
                {
                    count++;
                    if (count >= MAX_FAILED_ATTEMPTS) break;
                }
                else
                {
                    break; 
                }
            }
            return count;
        }

        // 觸發鎖定：寫入一筆鎖定事件（有效期由 IsLockedOut 計算）
        private async Task StartLockoutAsync(int memberId)
        {
            await LogSecurityEvent(memberId, "LoginBlocked_Lockout", $"連續失敗 {MAX_FAILED_ATTEMPTS} 次，鎖定 {LOCK_SECONDS} 秒");
        }
    }
}
