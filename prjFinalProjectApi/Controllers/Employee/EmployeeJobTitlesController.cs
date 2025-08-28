using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Linq;

namespace prjFinalProjectApi.Controllers.Employee
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeeJobTitlesController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        public EmployeeJobTitlesController(DbNursingHomeContext context) => _context = context;

        /// <summary>
        /// 取得職稱清單（{ id, name, deptId }）
        /// 可用 ?departmentId=1 過濾單一部門
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<EmployeeJobTitleOptionDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<EmployeeJobTitleOptionDto>>> Get([FromQuery] int? departmentId)
        {
            var q = _context.EmployeeJobTitles.AsNoTracking().AsQueryable();
            if (departmentId.HasValue) q = q.Where(x => x.DepartmentId == departmentId.Value);

            var list = await q
                .OrderBy(x => x.TitleName)
                .Select(x => new EmployeeJobTitleOptionDto(
                    x.JobTitleId,
                    x.TitleName ?? string.Empty,
                    x.DepartmentId              // ← 這裡就是 int?
                ))
                .ToListAsync();

            return Ok(list);
        }

    }
}
