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
                join cat in _context.SuppliesCategories on spn.SuppliesCategoryId equals cat.SuppliesCategoryId
                join spr in _context.SuppliesSuppliers on spn.SupplierId equals spr.SuppliesSupplierId
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
                    SuppliesProductName = spn.SuppliesProductName,
                    SuppliesCategoryId = cat.SuppliesCategoryId,
                    SuppliesCategoryName = cat.SuppliesCategoryName,
                    SuppliesSupplierId = spr.SuppliesSupplierId,
                    SuppliesSupplierName = spr.SuppliesSupplierName
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
        public async Task<IActionResult> PostSuppliesSalesOrder(SuppliesSalesOrderDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();


            try
            {
                // 新增一筆訂單
                var salesOrder = new SuppliesSalesOrder
                {
                    OrderDate = dto.OrderDate,
                    CustomerName = dto.CustomerName,
                    ReceivedDate = dto.ReceivedDate,
                    OrderStatus = dto.OrderStatus
                };


                _context.SuppliesSalesOrders.Add(salesOrder);
                await _context.SaveChangesAsync();


                // 新增多筆明細
                foreach (var detailDto in dto.Details)
                {
                    var detail = new SuppliesSalesOrderDetail
                    {
                        SuppliesSalesOrderId = salesOrder.SuppliesSalesOrderId,
                        SuppliesProductId = detailDto.SuppliesProductId,
                        QuantityOfSales = detailDto.QuantityOfSales,
                        ExpiryDate = detailDto.ExpiryDate
                    };


                    _context.SuppliesSalesOrderDetails.Add(detail);
                }


                await _context.SaveChangesAsync();
                await transaction.CommitAsync();


                return Ok(new { salesOrder.SuppliesSalesOrderId, Message = "Order created successfully" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { Error = ex.Message });
            }
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
