using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;

namespace prjFinalProjectApi.Controllers.Employee
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "EmployeeCookie", Policy = "EmployeeCookieOnly")]
    public class EmployeeDepartmentsController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        public EmployeeDepartmentsController(DbNursingHomeContext context) => _context = context;

        /// <summary>取得部門清單（{ id, name }）</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<EmployeeDepartmentOptionDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<EmployeeDepartmentOptionDto>>> Get()
        {
            var list = await _context.EmployeeDepartments
                .AsNoTracking()
                .OrderBy(d => d.DepartmentName)
                .Select(d => new EmployeeDepartmentOptionDto(
                    d.DepartmentId,
                    d.DepartmentName ?? string.Empty
                ))
                .ToListAsync();

            return Ok(list);
        }
    }
}
