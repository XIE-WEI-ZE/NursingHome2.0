using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmployeeJobTitlesController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public EmployeeJobTitlesController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // ✅ GET: api/EmployeeJobTitles?departmentId=1
        // 回傳欄位：JobTitleID, TitleName, DepartmentID（對應前端 mapping）
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> Get([FromQuery] int? departmentId)
        {
            var q = _context.EmployeeJobTitles.AsNoTracking().AsQueryable();

            if (departmentId.HasValue)
            {
                q = q.Where(x => x.DepartmentId == departmentId.Value);
            }

            var rows = await q
                .OrderBy(x => x.TitleName)
                .Select(x => new
                {
                    JobTitleID = x.JobTitleId,
                    TitleName = x.TitleName,
                    DepartmentID = x.DepartmentId
                })
                .ToListAsync();

            return Ok(rows);
        }
    }
}
