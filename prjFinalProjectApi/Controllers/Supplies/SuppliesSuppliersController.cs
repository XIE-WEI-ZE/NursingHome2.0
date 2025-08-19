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
    public class SuppliesSuppliersController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public SuppliesSuppliersController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // GET: api/SuppliesSuppliers
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SuppliesSupplier>>> GetSuppliesSuppliers()
        {
            return await _context.SuppliesSuppliers.ToListAsync();
        }

        // GET: api/SuppliesSuppliers/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SuppliesSupplier>> GetSuppliesSupplier(int id)
        {
            var suppliesSupplier = await _context.SuppliesSuppliers.FindAsync(id);

            if (suppliesSupplier == null)
            {
                return NotFound();
            }

            return suppliesSupplier;
        }

        // PUT: api/SuppliesSuppliers/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSuppliesSupplier(int id, SuppliesSupplier suppliesSupplier)
        {
            if (id != suppliesSupplier.SuppliesSupplierId)
            {
                return BadRequest();
            }

            _context.Entry(suppliesSupplier).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SuppliesSupplierExists(id))
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

        // POST: api/SuppliesSuppliers
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SuppliesSupplier>> PostSuppliesSupplier(SuppliesSupplier suppliesSupplier)
        {
            _context.SuppliesSuppliers.Add(suppliesSupplier);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSuppliesSupplier", new { id = suppliesSupplier.SuppliesSupplierId }, suppliesSupplier);
        }

        private bool SuppliesSupplierExists(int id)
        {
            return _context.SuppliesSuppliers.Any(e => e.SuppliesSupplierId == id);
        }
    }
}
