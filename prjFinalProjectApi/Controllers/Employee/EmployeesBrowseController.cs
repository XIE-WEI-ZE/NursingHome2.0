using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Helpers; // 擴充方法 IsAdmin/IsSupervisor/EmployeeId/DepartmentId

namespace prjFinalProjectApi.Controllers.Employee
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // 需要帶 JWT
    public class EmployeesBrowseController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public EmployeesBrowseController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // 規則：
        //   - Admin → 全部
        //   - Supervisor → 同 DepartmentId
        //   - 其他 → 只有自己
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var meId = User.EmployeeId();      // ← 正確名稱
            var isAdmin = User.IsAdmin();      // ← 正確名稱
            var isSup = User.IsSupervisor();   // ← 正確名稱
            var myDeptId = User.DepartmentId();// ← 正確名稱

            // 基礎查詢：只投影必要欄位
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
                // 管理員 → 不加任何 where
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
