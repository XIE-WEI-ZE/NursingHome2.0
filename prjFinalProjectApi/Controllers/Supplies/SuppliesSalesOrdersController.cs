using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace prjFinalProjectApi.Controllers.Supplies
{
    [Route("api/[controller]")]
    [ApiController]
    public class SuppliesSalesOrdersController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public SuppliesSalesOrdersController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // GET: api/SuppliesSalesOrders
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SuppliesSalesOrder>>> GetSuppliesSalesOrders()
        {
            var suppliesSalesOrders = await (
                from sso in _context.SuppliesSalesOrders
                join ssod in _context.SuppliesSalesOrderDetails on sso.SuppliesSalesOrderId equals ssod.SuppliesSalesOrderId
                join spn in _context.SuppliesProducts on ssod.SuppliesProductId equals spn.SuppliesProductId
                select new SuppliessalesDto
                {
                    SuppliesSalesOrderId = sso.SuppliesSalesOrderId,
                    OrderDate = sso.OrderDate,
                    CustomerName = sso.CustomerName,
                    ReceivedDate = sso.ReceivedDate,
                    OrderStatus = sso.OrderStatus,
                    SuppliesSalesOrderDetailId = ssod.SuppliesSalesOrderDetailId,
                    SuppliesProductId = ssod.SuppliesProductId,
                    QuantityOfSales = ssod.QuantityOfSales,
                    ExpiryDate = ssod.ExpiryDate,
                    SuppliesProductName = spn.SuppliesProductName
                }).ToListAsync();
            return Ok(suppliesSalesOrders);
        }

        // GET: api/SuppliesSalesOrders/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SuppliesSalesOrder>> GetSuppliesSalesOrder(int id)
        {
            var suppliesSalesOrder = await _context.SuppliesSalesOrders.FindAsync(id);

            if (suppliesSalesOrder == null)
            {
                return NotFound();
            }

            return suppliesSalesOrder;
        }

        // PUT: api/SuppliesSalesOrders/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSuppliesSalesOrder(int id, SuppliesSalesOrder suppliesSalesOrder)
        {
            if (id != suppliesSalesOrder.SuppliesSalesOrderId)
            {
                return BadRequest();
            }

            _context.Entry(suppliesSalesOrder).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SuppliesSalesOrderExists(id))
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

        // POST: api/SuppliesSalesOrders
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SuppliesSalesOrder>> PostSuppliesSalesOrder(SuppliesSalesOrder suppliesSalesOrder)
        {
            _context.SuppliesSalesOrders.Add(suppliesSalesOrder);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSuppliesSalesOrder", new { id = suppliesSalesOrder.SuppliesSalesOrderId }, suppliesSalesOrder);
        }

        // DELETE: api/SuppliesSalesOrders/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSuppliesSalesOrder(int id)
        {
            var suppliesSalesOrder = await _context.SuppliesSalesOrders.FindAsync(id);
            if (suppliesSalesOrder == null)
            {
                return NotFound();
            }

            _context.SuppliesSalesOrders.Remove(suppliesSalesOrder);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SuppliesSalesOrderExists(int id)
        {
            return _context.SuppliesSalesOrders.Any(e => e.SuppliesSalesOrderId == id);
        }
    }
}
