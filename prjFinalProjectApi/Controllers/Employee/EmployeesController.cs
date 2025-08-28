using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using prjFinalProjectApi.Helpers;   // User.IsAdmin()/IsSupervisor()/EmployeeId()
using prjFinalProjectApi.Models;
using EmployeeEntity = prjFinalProjectApi.Models.Employee;  // 🔴 關鍵：建立別名，避開命名空間衝突

namespace prjFinalProjectApi.Controllers.Employee
{
    [Route("api/[controller]")]
    [ApiController]
    // 只允許員工 Cookie 驗證
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeesController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        public EmployeesController(DbNursingHomeContext context) => _context = context;

        /// <summary>
        /// 權限判斷：
        /// Admin：全部；主管：同部門；一般：自己
        /// DepartmentId 可能為 null → 不是 Admin/本人一律拒絕
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

        // GET: api/Employees  （列表也做權限過濾）
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

                // DepartmentId 為 null 的資料不會被主管看到（如需看到可再調整邏輯）
                return await q.Where(e => e.DepartmentId == myDeptId).ToListAsync();
            }

            // 一般員工：只能看自己
            return await q.Where(e => e.EmployeeId == myId).ToListAsync();
        }

        // GET: api/Employees/5
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

        // PUT: api/Employees/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutEmployee(int id, [FromBody] EmployeeEntity employee)
        {
            if (id != employee.EmployeeId) return BadRequest();

            // 取舊資料的 DepartmentId（int?）做權限判斷
            var exists = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmployeeId == id);

            if (exists == null) return NotFound();

            if (!await CanAccessEmployeeAsync(exists.EmployeeId, exists.DepartmentId))
                return Forbid();

            _context.Entry(employee).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                var stillExists = await _context.Employees.AnyAsync(e => e.EmployeeId == id);
                if (!stillExists) return NotFound();
                throw;
            }

            return NoContent();
        }

        // 其餘 POST/DELETE 端點如需開放，請依同樣方式在進行資料異動前先做：
        // if (!await CanAccessEmployeeAsync(...)) return Forbid();
        // 或僅允許 Admin：if (!User.IsAdmin()) return Forbid();
    }
}
