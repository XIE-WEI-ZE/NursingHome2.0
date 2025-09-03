using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

using prjFinalProjectApi.Helpers;          // User.IsAdmin()/IsSupervisor()/EmployeeId()
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;       // EmployeeDetailDto, EmployeeDetailUpdateDto
using EmployeeEntity = prjFinalProjectApi.Models.Employee;

// ImageSharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Webp;

namespace prjFinalProjectApi.Controllers.Employee
{
    [Route("api/[controller]")]
    [ApiController]
    // 只允許員工 Cookie 驗證
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeesController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IWebHostEnvironment _env;

        public EmployeesController(DbNursingHomeContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        private const string FALLBACK_PHOTO = "/images/employees/noimage.jpg";

        // ---- 共用小工具：DateOnly? -> DateTime? ----
        private static DateTime? ToDateTime(DateOnly? d) =>
            d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null;

        /// <summary>
        /// Admin：全部；主管：同部門；一般：自己
        /// </summary>
        private async Task<bool> CanAccessEmployeeAsync(int targetEmployeeId, int? targetDeptId)
        {
            if (User.IsAdmin()) return true;

            var myId = User.EmployeeId();
            if (myId == targetEmployeeId) return true;

            if (User.IsSupervisor())
            {
                int? myDeptId = await _context.Employees
                    .Where(e => e.EmployeeId == myId)
                    .Select(e => e.DepartmentId)
                    .FirstOrDefaultAsync();

                if (myDeptId.HasValue && targetDeptId.HasValue &&
                    myDeptId.Value == targetDeptId.Value)
                {
                    return true;
                }
            }

            return false;
        }

        // GET: api/Employees（列表也做權限過濾）
        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmployeeEntity>>> GetEmployees()
        {
            var q = _context.Employees.AsNoTracking();

            if (User.IsAdmin()) return await q.ToListAsync();

            var myId = User.EmployeeId();

            if (User.IsSupervisor())
            {
                int? myDeptId = await _context.Employees
                    .Where(e => e.EmployeeId == myId)
                    .Select(e => e.DepartmentId)
                    .FirstOrDefaultAsync();

                return await q.Where(e => e.DepartmentId == myDeptId).ToListAsync();
            }

            return await q.Where(e => e.EmployeeId == myId).ToListAsync();
        }

        // ✅ 完整明細（前端用）：api/Employees/{id}/detail
        [HttpGet("{id:int}/detail")]
        public async Task<ActionResult<EmployeeDetailDto>> GetDetail(int id)
        {
            // 先取部門做權限判斷
            var basic = await _context.Employees.AsNoTracking()
                .Where(e => e.EmployeeId == id)
                .Select(e => new { e.EmployeeId, e.DepartmentId })
                .FirstOrDefaultAsync();

            if (basic == null) return NotFound();
            if (!await CanAccessEmployeeAsync(basic.EmployeeId, basic.DepartmentId)) return Forbid();

            // 只做 DB 可翻譯的查詢 → 取回匿名物件
            var row = await
                (from e in _context.Employees.AsNoTracking()
                 join d in _context.EmployeeDepartments.AsNoTracking()
                      on e.DepartmentId equals d.DepartmentId into dd
                 from d in dd.DefaultIfEmpty()
                 join j in _context.EmployeeJobTitles.AsNoTracking()
                      on e.JobTitleId equals j.JobTitleId into jj
                 from j in jj.DefaultIfEmpty()
                 where e.EmployeeId == id
                 select new
                 {
                     e.EmployeeId,
                     e.Name,
                     e.Gender,
                     e.IdentityNumber,
                     e.BirthDate,               // DateOnly?
                     e.Phone,
                     e.Email,
                     e.EducationLevel,
                     e.RegisteredAddress,
                     e.CurrentAddress,
                     e.Height,
                     e.Weight,
                     e.PayrollBankAccount,
                     e.PhotoPath,
                     e.EmploymentStatus,        // bit
                     DeptName = d != null ? d.DepartmentName : null,
                     JobName = j != null ? j.TitleName : null,
                     e.HireDate,                // DateOnly?
                     e.PoliceClearanceCertified,
                     e.IsSupervisor,
                     e.IsAdmin,
                     e.EmergencyContactPerson,
                     e.EmergencyContactPhone,
                     e.EmergencyContactRelationship
                 })
                .FirstOrDefaultAsync();

            if (row == null) return NotFound();

            // 記憶體轉型：DateOnly? -> DateTime?、bit -> 文字
            var dto = new EmployeeDetailDto
            {
                EmployeeId = row.EmployeeId,
                Name = row.Name ?? string.Empty,

                Gender = row.Gender,
                GenderText = row.Gender,

                IdentityNumber = row.IdentityNumber,
                BirthDate = ToDateTime(row.BirthDate),
                Phone = row.Phone,
                Email = row.Email,
                EducationLevel = row.EducationLevel,
                RegisteredAddress = row.RegisteredAddress,
                CurrentAddress = row.CurrentAddress,
                Height = row.Height,
                Weight = row.Weight,
                PayrollBankAccount = row.PayrollBankAccount,

                PhotoPath = string.IsNullOrWhiteSpace(row.PhotoPath) ? FALLBACK_PHOTO : row.PhotoPath,

                EmploymentStatusText = row.EmploymentStatus == true ? "在職"
                                          : row.EmploymentStatus == false ? "離職" : string.Empty,
                DepartmentName = row.DeptName,
                JobTitleName = row.JobName,
                HireDate = ToDateTime(row.HireDate),

                PoliceClearanceCertified = row.PoliceClearanceCertified,
                IsSupervisor = row.IsSupervisor,
                IsAdmin = row.IsAdmin,

                EmergencyContactPerson = row.EmergencyContactPerson,
                EmergencyContactPhone = row.EmergencyContactPhone,
                EmergencyContactRelationship = row.EmergencyContactRelationship
            };

            return dto;
        }

        // GET: api/Employees/{id}（回 raw Entity；例如編輯初始值）
        [HttpGet("{id:int}")]
        public async Task<ActionResult<EmployeeEntity>> GetEmployee(int id)
        {
            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.EmployeeId == id);

            if (employee == null) return NotFound();

            if (!await CanAccessEmployeeAsync(employee.EmployeeId, employee.DepartmentId))
                return Forbid();

            return employee;
        }

        // =============== 更新 ===============

        // ✅ 建議前端呼叫：PUT /api/Employees/{id}/detail
        [HttpPut("{id:int}/detail")]
        public async Task<IActionResult> UpdateDetail(int id, [FromBody] EmployeeDetailUpdateDto dto)
        {
            var e = await _context.Employees.FirstOrDefaultAsync(x => x.EmployeeId == id);
            if (e == null) return NotFound();
            if (!await CanAccessEmployeeAsync(e.EmployeeId, e.DepartmentId)) return Forbid();

            ApplyPatch(e, dto);
            await _context.SaveChangesAsync();
            return Ok(new { message = "更新成功" });
        }

        // 兼容舊路徑：PUT /api/Employees/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutEmployee(int id, [FromBody] EmployeeDetailUpdateDto dto)
        {
            var e = await _context.Employees.FirstOrDefaultAsync(x => x.EmployeeId == id);
            if (e == null) return NotFound();
            if (!await CanAccessEmployeeAsync(e.EmployeeId, e.DepartmentId)) return Forbid();

            ApplyPatch(e, dto);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // 共用：把 UpdateDto 的非空值套到 Entity
        private static void ApplyPatch(EmployeeEntity e, EmployeeDetailUpdateDto dto)
        {
            // 基本欄位（有值才覆蓋）
            if (!string.IsNullOrWhiteSpace(dto.Name)) e.Name = dto.Name;
            if (!string.IsNullOrWhiteSpace(dto.Gender)) e.Gender = dto.Gender;
            if (!string.IsNullOrWhiteSpace(dto.IdentityNumber)) e.IdentityNumber = dto.IdentityNumber;
            if (!string.IsNullOrWhiteSpace(dto.Phone)) e.Phone = dto.Phone;
            if (!string.IsNullOrWhiteSpace(dto.Email)) e.Email = dto.Email;
            if (!string.IsNullOrWhiteSpace(dto.EducationLevel)) e.EducationLevel = dto.EducationLevel;
            if (!string.IsNullOrWhiteSpace(dto.RegisteredAddress)) e.RegisteredAddress = dto.RegisteredAddress;
            if (!string.IsNullOrWhiteSpace(dto.CurrentAddress)) e.CurrentAddress = dto.CurrentAddress;
            if (dto.Height.HasValue) e.Height = dto.Height;
            if (dto.Weight.HasValue) e.Weight = dto.Weight;
            if (!string.IsNullOrWhiteSpace(dto.PayrollBankAccount)) e.PayrollBankAccount = dto.PayrollBankAccount;

            // 日期
            var birth = ParseDateOnly(dto.BirthDate);
            if (birth.HasValue) e.BirthDate = birth;
            var hire = ParseDateOnly(dto.HireDate);
            if (hire.HasValue) e.HireDate = hire;

            // 在職狀態：優先 bool，其次根據文字 "在職"/"離職" 轉換；都沒給就不動
            bool? status = dto.EmploymentStatus;
            if (status is null && !string.IsNullOrWhiteSpace(dto.EmploymentStatusText))
            {
                status = dto.EmploymentStatusText!.Trim() switch
                {
                    "在職" => true,
                    "離職" => false,
                    _ => (bool?)null
                };
            }
            if (status.HasValue) e.EmploymentStatus = status;

            // 部門/職稱（有 Id 就寫 Id）
            if (dto.DepartmentId.HasValue) e.DepartmentId = dto.DepartmentId;
            if (dto.JobTitleId.HasValue) e.JobTitleId = dto.JobTitleId;

            // 布林
            if (dto.PoliceClearanceCertified.HasValue) e.PoliceClearanceCertified = dto.PoliceClearanceCertified;
            if (dto.IsSupervisor.HasValue) e.IsSupervisor = dto.IsSupervisor;
            if (dto.IsAdmin.HasValue) e.IsAdmin = dto.IsAdmin;

            // 緊急聯絡人
            if (!string.IsNullOrWhiteSpace(dto.EmergencyContactPerson)) e.EmergencyContactPerson = dto.EmergencyContactPerson;
            if (!string.IsNullOrWhiteSpace(dto.EmergencyContactPhone)) e.EmergencyContactPhone = dto.EmergencyContactPhone;
            if (!string.IsNullOrWhiteSpace(dto.EmergencyContactRelationship)) e.EmergencyContactRelationship = dto.EmergencyContactRelationship;

            // 圖檔路徑（建議只由 /photo 控制；保留相容）
            if (!string.IsNullOrWhiteSpace(dto.PhotoPath)) e.PhotoPath = dto.PhotoPath;
        }

        private static DateOnly? ParseDateOnly(string? yyyyMMdd)
        {
            if (string.IsNullOrWhiteSpace(yyyyMMdd)) return null;
            if (DateTime.TryParse(yyyyMMdd, out var dt))
                return DateOnly.FromDateTime(dt);
            return null;
        }

        // =============== 上傳照片（壓縮 ≤ 200KB，輸出 .webp） ===============

        [HttpPost("{id:int}/photo")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> UploadPhoto(int id, IFormFile photo)
        {
            if (photo == null || photo.Length == 0) return BadRequest("沒有上傳檔案");

            var emp = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == id);
            if (emp == null) return NotFound();
            if (!await CanAccessEmployeeAsync(emp.EmployeeId, emp.DepartmentId)) return Forbid();

            // 讀圖：用一般 using（Image 不是 IAsyncDisposable）
            try
            {
                using var stream = photo.OpenReadStream();
                using var img = await Image.LoadAsync(stream);

                // 縮放 / 校正
                img.Mutate(x =>
                {
                    x.AutoOrient();
                    x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(1200, 1200) });
                });

                var webRoot = _env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
                var dir = Path.Combine(webRoot, "images", "employees");
                Directory.CreateDirectory(dir);

                // 刪舊圖（僅限我們資料夾、且不是預設圖）
                if (!string.IsNullOrWhiteSpace(emp.PhotoPath))
                {
                    var old = emp.PhotoPath.Replace('\\', '/');
                    if (!old.Equals(FALLBACK_PHOTO, StringComparison.OrdinalIgnoreCase) &&
                        old.StartsWith("/images/employees/", StringComparison.OrdinalIgnoreCase))
                    {
                        var oldAbs = Path.Combine(webRoot, old.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (System.IO.File.Exists(oldAbs))
                        {
                            try { System.IO.File.Delete(oldAbs); } catch { /* ignore */ }
                        }
                    }
                }

                // 目標 200KB，逐步降低 WebP 品質
                const int TARGET = 200 * 1024;
                var quality = 80;
                byte[] bytes;
                using (var ms = new MemoryStream())
                {
                    await img.SaveAsync(ms, new WebpEncoder { Quality = quality });
                    while (ms.Length > TARGET && quality > 40)
                    {
                        quality -= 5;
                        ms.Position = 0; ms.SetLength(0);
                        await img.SaveAsync(ms, new WebpEncoder { Quality = quality });
                    }
                    bytes = ms.ToArray();
                }

                var fileName = $"{id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.webp";
                var savePath = Path.Combine(dir, fileName);
                await System.IO.File.WriteAllBytesAsync(savePath, bytes);

                emp.PhotoPath = $"/images/employees/{fileName}";
                await _context.SaveChangesAsync();

                var absoluteUrl = $"{Request.Scheme}://{Request.Host}{emp.PhotoPath}";
                return Ok(new { url = emp.PhotoPath, absoluteUrl, size = bytes.Length, quality });
            }
            catch
            {
                return BadRequest("檔案格式不正確");
            }
        }

    }
}
