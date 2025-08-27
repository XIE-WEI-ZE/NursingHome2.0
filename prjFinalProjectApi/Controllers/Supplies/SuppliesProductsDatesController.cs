using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;

namespace prjFinalProjectApi.Controllers.Supplies
{
    [Route("api/[controller]")]
    [ApiController]
    public class SuppliesProductsDatesController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public SuppliesProductsDatesController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // GET: api/SuppliesProductsDates
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SuppliesProductsDate>>> GetSuppliesProductsDates()
        {
            return await _context.SuppliesProductsDates.ToListAsync();
        }

        // GET: api/SuppliesProductsDates/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SuppliesProductsDate>> GetSuppliesProductsDate(int id)
        {
            var suppliesProductsDate = await _context.SuppliesProductsDates.FindAsync(id);

            if (suppliesProductsDate == null)
            {
                return NotFound();
            }

            return Ok(suppliesProductsDate);
        }

        // PUT: api/SuppliesProductsDates/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSuppliesProductsDate(int id, SuppliesProductsDate suppliesProductsDate)
        {
            if (id != suppliesProductsDate.SuppliesProductsDateId)
            {
                return BadRequest();
            }

            _context.Entry(suppliesProductsDate).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SuppliesProductsDateExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/SuppliesProductsDates
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SuppliesProductsDate>> PostSuppliesProductsDate(SuppliesProductsDate suppliesProductsDate)
        {
            _context.SuppliesProductsDates.Add(suppliesProductsDate);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSuppliesProductsDate", new { id = suppliesProductsDate.SuppliesProductsDateId }, suppliesProductsDate);
        }

        private bool SuppliesProductsDateExists(int id)
        {
            return _context.SuppliesProductsDates.Any(e => e.SuppliesProductsDateId == id);
        }
    }
}
