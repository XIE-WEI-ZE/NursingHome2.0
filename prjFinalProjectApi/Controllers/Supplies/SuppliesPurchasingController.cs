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
            var suppliesPurchasingOrders = await (
                from spo in _context.SuppliesPurchasingOrders
                join spod in _context.SuppliesPurchasingOrderDetails on spo.SuppliesPurchasingOrderId equals spod.SuppliesPurchasingOrderId
                join sp in _context.SuppliesProducts on spod.SuppliesProductId equals sp.SuppliesProductId
                join spc in _context.SuppliesCategories on sp.SuppliesCategoryId equals spc.SuppliesCategoryId
                join sps in _context.SuppliesSuppliers on spo.SuppliesSupplierId equals sps.SuppliesSupplierId
                select new SuppliespurchasingDto
                {
                    SuppliesPurchasingOrderId = spo.SuppliesPurchasingOrderId,
                    SuppliesSupplierId = spo.SuppliesSupplierId,
                    ArrivalDate = spo.ArrivalDate,
                    SuppliesPurchasingOrderDetailId = spod.SuppliesPurchasingOrderDetailId,
                    SuppliesProductId = spod.SuppliesProductId,
                    SuppliesProductName = sp.SuppliesProductName,
                    QuantityIn = spod.QuantityIn,
                    ExpiryDate = spod.ExpiryDate,
                    SuppliesSupplierName = sps.SuppliesSupplierName,
                    SuppliesCategoryId = spc.SuppliesCategoryId,
                    SuppliesCategoryName = spc.SuppliesCategoryName
                }).ToListAsync();
            return Ok(suppliesPurchasingOrders);
        }

        [HttpGet("search")]
        public async Task<ActionResult<object>> SearchPurchasingOrders(
        string? keyword = "",
        int page = 1,
        int pageSize = 10
    )
            {
            var query = from spo in _context.SuppliesPurchasingOrders
                        join spod in _context.SuppliesPurchasingOrderDetails on spo.SuppliesPurchasingOrderId equals spod.SuppliesPurchasingOrderId
                        join sp in _context.SuppliesProducts on spod.SuppliesProductId equals sp.SuppliesProductId
                        join spc in _context.SuppliesCategories on sp.SuppliesCategoryId equals spc.SuppliesCategoryId
                        join sps in _context.SuppliesSuppliers on spo.SuppliesSupplierId equals sps.SuppliesSupplierId
                        select new SuppliespurchasingDto
                        {
                            SuppliesPurchasingOrderId = spo.SuppliesPurchasingOrderId,
                            SuppliesSupplierId = spo.SuppliesSupplierId,
                            ArrivalDate = spo.ArrivalDate,
                            SuppliesPurchasingOrderDetailId = spod.SuppliesPurchasingOrderDetailId,
                            SuppliesProductId = spod.SuppliesProductId,
                            SuppliesProductName = sp.SuppliesProductName,
                            QuantityIn = spod.QuantityIn,
                            ExpiryDate = spod.ExpiryDate,
                            SuppliesSupplierName = sps.SuppliesSupplierName,
                            SuppliesCategoryId = spc.SuppliesCategoryId,
                            SuppliesCategoryName = spc.SuppliesCategoryName
                        };

            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(o =>
                    o.SuppliesSupplierName.Contains(keyword) ||
                    o.SuppliesProductName.Contains(keyword) ||
                    o.SuppliesCategoryName.Contains(keyword));
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var data = await query
                .OrderByDescending(o => o.SuppliesPurchasingOrderId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new { totalCount, totalPages, page, pageSize, data });
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
        [HttpPost("CreatePurchasingOrder")]
        public async Task<ActionResult> CreatePurchasingOrder(SuppliesPurchasingOrderDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. 新增進貨單主表
                var order = new SuppliesPurchasingOrder
                {
                    SuppliesSupplierId = dto.SuppliesSupplierId,
                    ArrivalDate = dto.ArrivalDate
                };
                _context.SuppliesPurchasingOrders.Add(order);
                await _context.SaveChangesAsync();

                // 2. 新增明細
                foreach (var detail in dto.Details)
                {
                    var orderDetail = new SuppliesPurchasingOrderDetail
                    {
                        SuppliesPurchasingOrderId = order.SuppliesPurchasingOrderId,
                        SuppliesProductId = detail.SuppliesProductId,
                        QuantityIn = detail.QuantityIn,
                        ExpiryDate = detail.ExpiryDate
                    };
                    _context.SuppliesPurchasingOrderDetails.Add(orderDetail);

                    // 3. 更新物品主表庫存
                    var product = await _context.SuppliesProducts
                        .FirstOrDefaultAsync(p => p.SuppliesProductId == detail.SuppliesProductId);
                    if (product != null)
                    {
                        product.UnitsInStock += detail.QuantityIn ?? 0;
                    }

                    // 4. 更新 / 新增物品效期庫存
                    var productDate = await _context.SuppliesProductsDates
                        .FirstOrDefaultAsync(pd => pd.SuppliesProductId == detail.SuppliesProductId
                                                && pd.ExpiryDate == detail.ExpiryDate);
                    if (productDate != null)
                    {
                        productDate.RemainingStocks += detail.QuantityIn ?? 0;
                    }
                    else
                    {
                        _context.SuppliesProductsDates.Add(new SuppliesProductsDate
                        {
                            SuppliesProductId = detail.SuppliesProductId ?? 0,
                            ExpiryDate = detail.ExpiryDate,
                            RemainingStocks = detail.QuantityIn ?? 0
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { order.SuppliesPurchasingOrderId });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
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
