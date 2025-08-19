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
    public class SuppliesCategoriesController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public SuppliesCategoriesController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // GET: api/SuppliesCategories
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SuppliesCategory>>> GetSuppliesCategories()
        {
            return await _context.SuppliesCategories.ToListAsync();
        }

        // GET: api/SuppliesCategories/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SuppliesCategory>> GetSuppliesCategory(int id)
        {
            var suppliesCategory = await _context.SuppliesCategories.FindAsync(id);

            if (suppliesCategory == null)
            {
                return NotFound();
            }

            return suppliesCategory;
        }

        // PUT: api/SuppliesCategories/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSuppliesCategory(int id, SuppliesCategory suppliesCategory)
        {
            if (id != suppliesCategory.SuppliesCategoryId)
            {
                return BadRequest();
            }

            _context.Entry(suppliesCategory).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SuppliesCategoryExists(id))
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

        // POST: api/SuppliesCategories
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SuppliesCategory>> PostSuppliesCategory(SuppliesCategory suppliesCategory)
        {
            _context.SuppliesCategories.Add(suppliesCategory);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSuppliesCategory", new { id = suppliesCategory.SuppliesCategoryId }, suppliesCategory);
        }

        // DELETE: api/SuppliesCategories/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSuppliesCategory(int id)
        {
            var suppliesCategory = await _context.SuppliesCategories.FindAsync(id);
            if (suppliesCategory == null)
            {
                return NotFound();
            }

            _context.SuppliesCategories.Remove(suppliesCategory);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SuppliesCategoryExists(int id)
        {
            return _context.SuppliesCategories.Any(e => e.SuppliesCategoryId == id);
        }
    }
}
