using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Helpers; // IsAdmin/IsSupervisor/EmployeeId/DepartmentId

namespace prjFinalProjectApi.Controllers.Employee
{
    [Route("api/[controller]")]
    [ApiController]
    // ✅ 後台僅接受員工 Cookie（JWT 會員進不來）
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeesBrowseController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public EmployeesBrowseController(DbNursingHomeContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 規則：
        /// Admin → 全部
        /// Supervisor → 同 DepartmentId
        /// 其他 → 只有自己
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var meId = User.EmployeeId();
            var isAdmin = User.IsAdmin();
            var isSup = User.IsSupervisor();
            var myDeptId = User.DepartmentId();

            // 只取必要欄位
            var q = _context.Employees.AsNoTracking().Select(e => new
            {
                employeeId = e.EmployeeId,
                name = e.Name,
                employmentStatus = e.EmploymentStatus,
                departmentId = e.DepartmentId,
                jobTitleId = e.JobTitleId
            });

            if (isAdmin)
            {
                // Admin: 不加條件
            }
            else if (isSup && myDeptId > 0)
            {
                q = q.Where(e => e.departmentId == myDeptId);
            }
            else
            {
                q = q.Where(e => e.employeeId == meId);
            }

            var list = await q
                .OrderBy(e => e.departmentId)
                .ThenBy(e => e.jobTitleId)
                .ThenBy(e => e.employeeId)
                .ToListAsync();

            return Ok(list);
        }
    }
}
