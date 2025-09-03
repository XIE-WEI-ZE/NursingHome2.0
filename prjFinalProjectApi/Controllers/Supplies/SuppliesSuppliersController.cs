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

        [HttpGet("search")]
        public async Task<ActionResult<object>> SearchSuppliesSuppliers(
        string? keyword = "",
        bool? continued = null,
        int page = 1,
        int pageSize = 10
)
        {
            var query = _context.SuppliesSuppliers.AsQueryable();

            // 過濾條件：名稱、統編、聯絡人、地址、類別
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(s =>
                    s.SuppliesSupplierName.Contains(keyword) ||
                    s.SuppliesSupplierGui.Contains(keyword) ||
                    s.ContactPerson.Contains(keyword) ||
                    s.ContactNumber.Contains(keyword) ||
                    s.Address.Contains(keyword) ||
                    s.SupplierKeyword.Contains(keyword)
                );
            }

            // 狀態過濾
            if (continued.HasValue)
            {
                query = query.Where(s => s.Continued == continued.Value);
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var data = await query
                .OrderByDescending(s => s.SuppliesSupplierId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new
            {
                totalCount,
                page,
                pageSize,
                totalPages,
                data
            });
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
        public async Task<ActionResult> PutSuppliesSupplier(int id, SuppliesSupplier suppliesSupplier)
        {
            if (id != suppliesSupplier.SuppliesSupplierId)
            {
                return BadRequest();
            }

            var _suppliesSupplierToUpdate = await _context.SuppliesSuppliers.FindAsync(id);
            if (_suppliesSupplierToUpdate == null) return NotFound();

            _suppliesSupplierToUpdate.SuppliesSupplierName = suppliesSupplier.SuppliesSupplierName;
            _suppliesSupplierToUpdate.SuppliesSupplierGui = suppliesSupplier.SuppliesSupplierGui;
            _suppliesSupplierToUpdate.ContactPerson = suppliesSupplier.ContactPerson;
            _suppliesSupplierToUpdate.ContactNumber = suppliesSupplier.ContactNumber;
            _suppliesSupplierToUpdate.Address = suppliesSupplier.Address;
            _suppliesSupplierToUpdate.SupplierKeyword = suppliesSupplier.SupplierKeyword;
            _suppliesSupplierToUpdate.Continued = suppliesSupplier.Continued;

            _context.SuppliesSuppliers.Update(_suppliesSupplierToUpdate);

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
