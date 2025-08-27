using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;

namespace prjFinalProjectApi.Controllers.Employee
{
    [Route("api/[controller]")]
    [ApiController]
    // ✅ 後台僅接受員工 Cookie（JWT 會員無法進）
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeeDepartmentsController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public EmployeeDepartmentsController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // GET: api/EmployeeDepartments
        // 回傳欄位：DepartmentID, DepartmentName（對應前端 mapping）
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> Get()
        {
            var rows = await _context.EmployeeDepartments
                .AsNoTracking()
                .OrderBy(d => d.DepartmentName)
                .Select(d => new
                {
                    DepartmentID = d.DepartmentId,
                    DepartmentName = d.DepartmentName
                })
                .ToListAsync();

            return Ok(rows);
        }
    }
}
