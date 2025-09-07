using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using OfficeOpenXml;
using System.Globalization;

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
        public async Task<ActionResult<IEnumerable<SupplieslistDto>>> GetSuppliesProducts()
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
        public async Task<ActionResult<SupplieslistDto>> GetSuppliesProduct(int id)
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

        // =====EPPlus=====
        // 下載 Excel 範本（首列固定標題；符合現有 Model/前端介面）
        [HttpGet("excel/template")]
        public IActionResult DownloadTemplate()
        {
            var headers = new[]
            {
            "物品名稱",   // string
            "基本單位",       // string
            "剩餘數量",          // int
            "單價",          // int
            "供應商編號",            // int (請填有效供應商 Id)
            "類別編號",    // int (請填有效類別 Id)
            "Exist"                  // bool (true/false 或 1/0)
        };

            using var pkg = new ExcelPackage();
            var ws = pkg.Workbook.Worksheets.Add("SuppliesProducts");

            // 標題列
            for (int i = 0; i < headers.Length; i++)
                ws.Cells[1, i + 1].Value = headers[i];

            // 給一列示範資料（可刪）
            ws.Cells[2, 1].Value = "醫療手套S";
            ws.Cells[2, 2].Value = "盒";
            ws.Cells[2, 3].Value = 0;
            ws.Cells[2, 4].Value = 120;
            ws.Cells[2, 5].Value = 1;   // SupplierId 範例
            ws.Cells[2, 6].Value = 2;   // SuppliesCategoryId 範例
            ws.Cells[2, 7].Value = true;

            ws.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            ws.View.FreezePanes(2, 1);

            var bytes = pkg.GetAsByteArray();
            const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            return File(bytes, contentType, "SuppliesProducts_Template.xlsx");
        }

        //匯入 Excel 檔案（批次新增 SuppliesProducts）
        [HttpPost("excel/import")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(50_000_000)] // 50MB，可依需求調整
        public async Task<ActionResult<SuppliesImportResult>> ImportExcel([FromForm] ExcelImportRequest req)
        {
            var file = req.File;
            if (file == null || file.Length == 0)
                return BadRequest("找不到上傳檔案。");

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                return BadRequest("檔案格式錯誤，僅接受 .xlsx。");

            var expectedHeaders = new[]
            {
            "物品名稱",
            "基本單位",
            "剩餘數量",
            "單價",
            "供應商編號",
            "類別編號",
            "Exist"
        };

            var result = new SuppliesImportResult();

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;

            using var pkg = new ExcelPackage(ms);
            var ws = pkg.Workbook.Worksheets.FirstOrDefault();
            if (ws == null || ws.Dimension == null)
                return BadRequest("Excel 內容為空。");

            // 檢查標題列
            for (int i = 0; i < expectedHeaders.Length; i++)
            {
                var header = ws.Cells[1, i + 1].Text?.Trim();
                if (!string.Equals(header, expectedHeaders[i], StringComparison.Ordinal))
                    return BadRequest($"標題列第 {i + 1} 欄應為「{expectedHeaders[i]}」，實際為「{header}」。");
            }

            var inserted = 0;
            var errors = new List<string>();
            var culture = CultureInfo.InvariantCulture;

            // 從第2列開始讀
            for (int row = 2; row <= ws.Dimension.End.Row; row++)
            {
                // 全空列就跳過
                bool isEmpty = true;
                for (int col = 1; col <= expectedHeaders.Length; col++)
                    if (!string.IsNullOrWhiteSpace(ws.Cells[row, col].Text))
                    { isEmpty = false; break; }
                if (isEmpty) continue;

                try
                {
                    var name = ws.Cells[row, 1].Text?.Trim();
                    var qtyPerUnit = ws.Cells[row, 2].Text?.Trim();
                    var unitsInStockText = ws.Cells[row, 3].Text?.Trim();
                    var pricePerUnitText = ws.Cells[row, 4].Text?.Trim();
                    var supplierIdText = ws.Cells[row, 5].Text?.Trim();
                    var categoryIdText = ws.Cells[row, 6].Text?.Trim();
                    var existText = ws.Cells[row, 7].Text?.Trim();

                    if (string.IsNullOrWhiteSpace(name))
                        throw new Exception("SuppliesProductName 必填");
                    if (string.IsNullOrWhiteSpace(qtyPerUnit))
                        throw new Exception("QuantityPerUnit 必填");

                    if (!int.TryParse(unitsInStockText, NumberStyles.Integer, culture, out var unitsInStock))
                        throw new Exception("UnitsInStock 需為整數");
                    if (!int.TryParse(pricePerUnitText, NumberStyles.Number, culture, out var pricePerUnit))
                        throw new Exception("PricePerUnit 需為整數");
                    if (!int.TryParse(supplierIdText, NumberStyles.Integer, culture, out var supplierId))
                        throw new Exception("SupplierId 需為整數");
                    if (!int.TryParse(categoryIdText, NumberStyles.Integer, culture, out var categoryId))
                        throw new Exception("SuppliesCategoryId 需為整數");

                    bool exist = existText?.Equals("1") == true
                                 || existText?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

                    // 檢查供應商/類別是否存在（避免孤兒資料）
                    if (!_context.SuppliesSuppliers.Any(s => s.SuppliesSupplierId == supplierId))
                        throw new Exception($"SupplierId 不存在: {supplierId}");
                    if (!_context.SuppliesCategories.Any(c => c.SuppliesCategoryId == categoryId))
                        throw new Exception($"SuppliesCategoryId 不存在: {categoryId}");

                    var entity = new SuppliesProduct
                    {
                        SuppliesProductName = name,
                        QuantityPerUnit = qtyPerUnit,
                        UnitsInStock = unitsInStock,
                        PricePerUnit = pricePerUnit,
                        SupplierId = supplierId,
                        SuppliesCategoryId = categoryId,
                        Exist = exist
                    };

                    _context.SuppliesProducts.Add(entity);
                    inserted++;
                }
                catch (Exception ex)
                {
                    errors.Add($"第 {row} 列：{ex.Message}");
                }
            }

            if (inserted > 0)
                await _context.SaveChangesAsync();

            result.Inserted = inserted;
            result.Errors = errors;
            return Ok(result);
        }
    }
}
