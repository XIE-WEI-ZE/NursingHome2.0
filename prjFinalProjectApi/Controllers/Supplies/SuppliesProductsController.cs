using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;

namespace prjFinalProjectApi.Controllers.Supplies
{
    [Route("api/[controller]")]
    [ApiController]
    public class SuppliesProductsController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public SuppliesProductsController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // GET: api/SuppliesProducts
        // 將供應商ID與供應品類別ID的名稱加入後端API回傳
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SuppliesProduct>>> GetSuppliesProducts()
        {
            var suppliesProducts = await (
                                from SuppliesProducts in _context.SuppliesProducts
                                join SuppliesSupplierName in _context.SuppliesSuppliers on SuppliesProducts.SupplierId equals SuppliesSupplierName.SuppliesSupplierId
                                join SuppliesCategoryName in _context.SuppliesCategories on SuppliesProducts.SuppliesCategoryId equals SuppliesCategoryName.SuppliesCategoryId
                                select new SupplieslistDto
                                {
                                    SuppliesProductID = SuppliesProducts.SuppliesProductId,
                                    SuppliesProductName = SuppliesProducts.SuppliesProductName,
                                    QuantityPerUnit = SuppliesProducts.QuantityPerUnit,
                                    UnitsInStock = SuppliesProducts.UnitsInStock,
                                    PricePerUnit = SuppliesProducts.PricePerUnit,
                                    SupplierId = SuppliesProducts.SupplierId,
                                    SuppliesSupplierName = SuppliesSupplierName.SuppliesSupplierName,
                                    SuppliesCategoryId = SuppliesProducts.SuppliesCategoryId,
                                    SuppliesCategoryName = SuppliesCategoryName.SuppliesCategoryName,
                                    Exist = SuppliesProducts.Exist
                                }).ToListAsync();

            return Ok(suppliesProducts);
        }

        // GET: api/SuppliesProducts/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SuppliesProduct>> GetSuppliesProduct(int id)
        {
            var supplieslist = await _context.SuppliesProducts.FindAsync(id);

            if (supplieslist == null)
            {
                return NotFound();
            }

            var supplieslistDto = await (
                                from SuppliesProducts in _context.SuppliesProducts
                                join SuppliesSupplierName in _context.SuppliesSuppliers on SuppliesProducts.SupplierId equals SuppliesSupplierName.SuppliesSupplierId
                                join SuppliesCategoryName in _context.SuppliesCategories on SuppliesProducts.SuppliesCategoryId equals SuppliesCategoryName.SuppliesCategoryId
                                where SuppliesProducts.SuppliesProductId == id
                                select new SupplieslistDto
                                {
                                    SuppliesProductID = supplieslist.SuppliesProductId,
                                    SuppliesProductName = supplieslist.SuppliesProductName,
                                    QuantityPerUnit = supplieslist.QuantityPerUnit,
                                    UnitsInStock = supplieslist.UnitsInStock,
                                    PricePerUnit = supplieslist.PricePerUnit,
                                    SuppliesSupplierName = SuppliesSupplierName.SuppliesSupplierName,
                                    SuppliesCategoryName = SuppliesCategoryName.SuppliesCategoryName,
                                    Exist = supplieslist.Exist
                                }).FirstOrDefaultAsync();

            return Ok(supplieslistDto);
        }

        [HttpGet("search")]
        public async Task<ActionResult<object>> SearchSuppliesProducts(
        string? keyword = "",
        int page = 1,
        int pageSize = 10
    )
        {
            var query = from p in _context.SuppliesProducts
                        join s in _context.SuppliesSuppliers on p.SupplierId equals s.SuppliesSupplierId
                        join c in _context.SuppliesCategories on p.SuppliesCategoryId equals c.SuppliesCategoryId
                        select new SupplieslistDto
                        {
                            SuppliesProductID = p.SuppliesProductId,
                            SuppliesProductName = p.SuppliesProductName,
                            QuantityPerUnit = p.QuantityPerUnit,
                            UnitsInStock = p.UnitsInStock,
                            PricePerUnit = p.PricePerUnit,
                            SupplierId = p.SupplierId,
                            SuppliesSupplierName = s.SuppliesSupplierName,
                            SuppliesCategoryId = p.SuppliesCategoryId,
                            SuppliesCategoryName = c.SuppliesCategoryName,
                            Exist = p.Exist
                        };

            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(p =>
                    p.SuppliesProductName.Contains(keyword) ||
                    p.SuppliesSupplierName.Contains(keyword) ||
                    p.SuppliesCategoryName.Contains(keyword)
                );
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var data = await query
                .OrderByDescending(p => p.SuppliesProductID)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new { totalCount, totalPages, page, pageSize, data });
        }

        // PUT: api/SuppliesProducts/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<ActionResult> PutSuppliesProduct(int id, SuppliesProduct suppliesProduct)
        {
            var _suppliesProductToUpdate = await _context.SuppliesProducts.FindAsync(id);
            if(_suppliesProductToUpdate == null) return NotFound();

            _suppliesProductToUpdate.SuppliesProductName = suppliesProduct.SuppliesProductName;
            _suppliesProductToUpdate.QuantityPerUnit = suppliesProduct.QuantityPerUnit;
            _suppliesProductToUpdate.UnitsInStock = suppliesProduct.UnitsInStock;
            _suppliesProductToUpdate.PricePerUnit = suppliesProduct.PricePerUnit;
            _suppliesProductToUpdate.SupplierId = suppliesProduct.SupplierId;
            _suppliesProductToUpdate.SuppliesCategoryId = suppliesProduct.SuppliesCategoryId;
            _suppliesProductToUpdate.Exist = suppliesProduct.Exist;

            _context.SuppliesProducts.Update(_suppliesProductToUpdate);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SuppliesProductExists(id))
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

        // POST: api/SuppliesProducts
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SuppliesProduct>> PostSuppliesProduct(SuppliesProduct suppliesProduct)
        {
            _context.SuppliesProducts.Add(suppliesProduct);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSuppliesProduct", new { id = suppliesProduct.SuppliesProductId }, suppliesProduct);
        }
        private bool SuppliesProductExists(int id)
        {
            return _context.SuppliesProducts.Any(e => e.SuppliesProductId == id);
        }
    }
}
