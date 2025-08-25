using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto.ERP;
using System.Text.RegularExpressions;
using System.IO; 

namespace prjFinalProjectApi.Controllers.Members
{
    [Route("api/backend/member")]
    [ApiController]
    public class BackendMemberController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IWebHostEnvironment _environment; 

        public BackendMemberController(DbNursingHomeContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllMembers([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var members = await _context.Members
                    .AsNoTracking()
                    .OrderBy(m => m.FCreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(m => new
                    {
                        m.FMemberId,
                        m.FName,
                        m.FEmail,
                        FBirthDate = m.FBirthDate.HasValue ? m.FBirthDate.Value.ToString("yyyy-MM-dd") : null,
                        m.FCity,
                        m.FDistrict,
                        m.FRoadAddress,
                        m.FAccountStatus,
                        m.FResidesInCareHomeStatus,
                        m.FProfilePictureUrl
                    })
                    .ToListAsync();

                var totalCount = await _context.Members.CountAsync();
                return Ok(new { members, totalCount, page, pageSize });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "載入會員失敗", error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetMemberById(int? id)
        {
            try
            {
                if (!id.HasValue || id <= 0)
                {
                    return BadRequest(new { message = "無效的會員ID" });
                }

                var member = await _context.Members
                    .AsNoTracking()
                    .Where(m => m.FMemberId == id)
                    .Select(m => new
                    {
                        m.FMemberId,
                        m.FName,
                        m.FEmail,
                        m.FPhone,
                        m.FGender,
                        m.FBirthDate,
                        m.FAccount,
                        m.FAccountStatus,
                        m.FCity,
                        m.FDistrict,
                        m.FRoadAddress,
                        m.FResidesInCareHomeStatus,
                        m.FProfilePictureUrl
                    })
                    .FirstOrDefaultAsync();

                if (member == null)
                {
                    return NotFound(new { message = "查無此會員的資料" });
                }

                return Ok(member);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult> Update(int? id, [FromBody] UpdateMemberERPDto dto)
        {
            Console.WriteLine("接收到的更新資料: " + Newtonsoft.Json.JsonConvert.SerializeObject(dto));
            try
            {
                var member = await _context.Members.FirstOrDefaultAsync(x => x.FMemberId == id);
                if (member == null)
                    return NotFound(new { message = "查無此會員" });

                // 驗證 Email 格式
                if (!string.IsNullOrWhiteSpace(dto.FEmail))
                {
                    if (!Regex.IsMatch(dto.FEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                        return BadRequest(new { message = "Email 格式無效" });

                    var exists = await _context.Members
                        .AnyAsync(x => x.FEmail == dto.FEmail && x.FMemberId != id);
                    if (exists)
                        return BadRequest(new { message = "Email 已被使用" });
                }

                // 電話格式
                if (!string.IsNullOrWhiteSpace(dto.FPhone) && !Regex.IsMatch(dto.FPhone, @"^09\d{8}$"))
                    return BadRequest(new { message = "手機格式錯誤，應為 09 開頭的 10 位數字" });

                // 嘗試解析出生日期
                if (!string.IsNullOrWhiteSpace(dto.FBirthDate))
                {
                    if (!DateOnly.TryParse(dto.FBirthDate, out var parsedDate))
                        return BadRequest(new { message = "出生日期格式錯誤，應為 yyyy-MM-dd" });

                    if (parsedDate > DateOnly.FromDateTime(DateTime.Today))
                        return BadRequest(new { message = "出生日期不能是未來日期" });

                    member.FBirthDate = parsedDate;
                }
                else
                {
                    member.FBirthDate = null; // 允許出生日期為 null
                }

                // 更新欄位，允許 null 值
                member.FName = string.IsNullOrWhiteSpace(dto.FName) ? member.FName : dto.FName;
                member.FEmail = string.IsNullOrWhiteSpace(dto.FEmail) ? member.FEmail : dto.FEmail;
                member.FGender = string.IsNullOrWhiteSpace(dto.FGender) ? member.FGender : dto.FGender;
                member.FPhone = string.IsNullOrWhiteSpace(dto.FPhone) ? member.FPhone : dto.FPhone;
                member.FCity = string.IsNullOrWhiteSpace(dto.FCity) ? member.FCity : dto.FCity;
                member.FDistrict = string.IsNullOrWhiteSpace(dto.FDistrict) ? member.FDistrict : dto.FDistrict;
                member.FRoadAddress = string.IsNullOrWhiteSpace(dto.FRoadAddress) ? member.FRoadAddress : dto.FRoadAddress;

                if (dto.FAccountStatus.HasValue)
                    member.FAccountStatus = dto.FAccountStatus.Value;

                if (dto.FResidesInCareHomeStatus.HasValue)
                    member.FResidesInCareHomeStatus = dto.FResidesInCareHomeStatus.Value;

                member.FProfilePictureUrl = string.IsNullOrWhiteSpace(dto.FProfilePictureUrl) ? member.FProfilePictureUrl : dto.FProfilePictureUrl;

                await _context.SaveChangesAsync();
                Console.WriteLine("更新成功，會員ID: " + id);
                return Ok(new { message = "更新成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine("更新失敗，錯誤: " + ex.Message);
                return StatusCode(500, new { message = "更新失敗", error = ex.Message });
            }
        }

        [HttpPatch("{id:int}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(int? id)
        {
            try
            {
                var member = await _context.Members.FirstOrDefaultAsync(x => x.FMemberId == id);
                if (member == null)
                    return NotFound(new { message = "查無此會員" });

                member.FAccountStatus = !(member.FAccountStatus ?? false);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = member.FAccountStatus == true ? "已啟用" : "已停權",
                    status = member.FAccountStatus
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "切換狀態失敗", error = ex.Message });
            }
        }

        // 新增上傳頭像端點
        [HttpPost("upload-profile-picture")]
        public async Task<IActionResult> UploadProfilePicture(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "無效的檔案" });

            if (file.Length > 5 * 1024 * 1024) // 限制 5MB
                return BadRequest(new { message = "檔案大小超過 5MB" });

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                return BadRequest(new { message = "僅支援 jpg, jpeg, png 格式" });

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "images", "members");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = Guid.NewGuid().ToString() + ext;
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new
            {
                url = $"images/members/{uniqueFileName}"  
            });
        }

    }
}