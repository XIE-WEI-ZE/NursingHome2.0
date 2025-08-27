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
    public class SuppliesPurchasingController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public SuppliesPurchasingController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // GET: api/SuppliesPurchasing
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SuppliesPurchasingOrder>>> GetSuppliesPurchasingOrders()
        {
            return await _context.SuppliesPurchasingOrders.ToListAsync();
        }

        // GET: api/SuppliesPurchasing/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SuppliesPurchasingOrder>> GetSuppliesPurchasingOrder(int id)
        {
            var suppliesPurchasingOrder = await _context.SuppliesPurchasingOrders.FindAsync(id);

            if (suppliesPurchasingOrder == null)
            {
                return NotFound();
            }

            return suppliesPurchasingOrder;
        }

        // PUT: api/SuppliesPurchasing/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSuppliesPurchasingOrder(int id, SuppliesPurchasingOrder suppliesPurchasingOrder)
        {
            if (id != suppliesPurchasingOrder.SuppliesPurchasingOrderId)
            {
                return BadRequest();
            }

            _context.Entry(suppliesPurchasingOrder).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SuppliesPurchasingOrderExists(id))
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

        // POST: api/SuppliesPurchasing
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SuppliesPurchasingOrder>> PostSuppliesPurchasingOrder(SuppliesPurchasingOrder suppliesPurchasingOrder)
        {
            _context.SuppliesPurchasingOrders.Add(suppliesPurchasingOrder);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSuppliesPurchasingOrder", new { id = suppliesPurchasingOrder.SuppliesPurchasingOrderId }, suppliesPurchasingOrder);
        }

        // DELETE: api/SuppliesPurchasing/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSuppliesPurchasingOrder(int id)
        {
            var suppliesPurchasingOrder = await _context.SuppliesPurchasingOrders.FindAsync(id);
            if (suppliesPurchasingOrder == null)
            {
                return NotFound();
            }

            _context.SuppliesPurchasingOrders.Remove(suppliesPurchasingOrder);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SuppliesPurchasingOrderExists(int id)
        {
            return _context.SuppliesPurchasingOrders.Any(e => e.SuppliesPurchasingOrderId == id);
        }
    }
}
