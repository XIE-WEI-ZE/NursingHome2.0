using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZXing;
using ZXing.Common;
using prjFinalProjectApi.Hubs;   // ✅ 新增
using Microsoft.AspNetCore.SignalR; // ✅ 新增

namespace prjFinalProjectApi.Controllers.Supplies
{
    [Route("api/[controller]")]
    [ApiController]
    public class SuppliesSalesOrdersController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<OrderHub> _hubContext; // ✅ 新增

        // ✅ 修改建構子，注入 hubContext
        public SuppliesSalesOrdersController(DbNursingHomeContext context,
                                             IWebHostEnvironment env,
                                             IHubContext<OrderHub> hubContext)
        {
            _context = context;
            _env = env;
            _hubContext = hubContext;
        }


        // GET: api/SuppliesSalesOrders
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SuppliesSalesOrderDto>>> 
            GetSuppliesSalesOrders
            (
            [FromQuery] string? keyword,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? status = ""
            )
        {
            // 分頁初始化
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;

            var query = from sso in _context.SuppliesSalesOrders
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
                        };

            // 關鍵字搜尋
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(x =>
                    x.CustomerName.Contains(keyword) ||
                    x.SuppliesSalesOrderId.ToString().Contains(keyword)
                );
            }
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(x => x.OrderStatus == status);
            }
            // 取得總筆數（分頁用）
            var totalCount = await query.CountAsync();

            // 分頁處理
            var data = await query
                .OrderByDescending(x => x.SuppliesSalesOrderId) // 可依需求排序
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var suppliesSalesOrders = await query.ToListAsync();
            return Ok(new
            {
                TotalCount = totalCount,   // 總筆數
                Page = page,               // 當前頁
                PageSize = pageSize,       // 每頁筆數
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                Data = data                // 分頁後的資料
            });
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

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] UpdateStatusDto dto)
        {
            try
            {
                if (string.IsNullOrEmpty(dto.Status))
                {
                    return BadRequest(new { Error = "Status is required" });
                }

                var parameters = new[]
                {
            new SqlParameter("@SuppliesSalesOrderID", id),
            new SqlParameter("@NewStatus", dto.Status)
        };

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC sp_UpdateSalesOrderStatus @SuppliesSalesOrderID, @NewStatus",
                    parameters
                );

                return Ok(new { Message = "Order status updated successfully", Status = dto.Status });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    Error = ex.Message,
                    Inner = ex.InnerException?.Message,
                    Stack = ex.StackTrace
                });
            }
        }

        // 取得本機 IP 位址（非必要）
        private string GetServerIp()
        {
            string localIp = "localhost"; // fallback 預設
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        localIp = ip.ToString(); // IPv4
                        break;
                    }
                }
            }
            catch
            {
                // 如果抓不到，就用 localhost
            }
            return localIp;
        }

        // POST: api/SuppliesSalesOrders
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<IActionResult> PostSuppliesSalesOrder([FromBody] SuppliesSalesOrderDto dto)
        {
            try
            {
                if (dto == null || dto.Details == null || !dto.Details.Any())
                {
                    return BadRequest(new { Error = "Order data is missing or invalid." });
                }

                // 準備 TVP DataTable
                var detailsTable = new DataTable();
                detailsTable.Columns.Add("SuppliesProductId", typeof(int));
                detailsTable.Columns["SuppliesProductId"].AllowDBNull = false;

                detailsTable.Columns.Add("QuantityOfSales", typeof(int));
                detailsTable.Columns["QuantityOfSales"].AllowDBNull = false;

                detailsTable.Columns.Add("ExpiryDate", typeof(DateTime));
                detailsTable.Columns["ExpiryDate"].AllowDBNull = true;

                foreach (var d in dto.Details)
                {
                    detailsTable.Rows.Add(
                        d.SuppliesProductId ?? 0,   // NOT NULL 欄位 → 預設 0
                        d.QuantityOfSales ?? 0,     // NOT NULL 欄位 → 預設 0
                        d.ExpiryDate?.Date ?? (object)DBNull.Value
                    );
                }

                // SQL 參數
                var parameters = new[]
                {
            new SqlParameter("@CustomerName", dto.CustomerName ?? (object)DBNull.Value),
            new SqlParameter("@OrderDate", dto.OrderDate ?? (object)DBNull.Value),
            new SqlParameter("@ReceivedDate", dto.ReceivedDate ?? (object)DBNull.Value),
            new SqlParameter("@OrderStatus", dto.OrderStatus ?? (object)DBNull.Value),
            new SqlParameter("@SalesOrderDetails", detailsTable)
            {
                SqlDbType = SqlDbType.Structured,
                TypeName = "dbo.TVP_SalesOrderDetail"
            },
            new SqlParameter("@UpdateStock", SqlDbType.Bit) { Value = 0 } // 新增訂單時不扣庫存，一定要利用SqlDbType.Bit來綁型別，不然會被當作int處理
        };

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC sp_CreateSalesOrder @CustomerName, @OrderDate, @ReceivedDate, @OrderStatus, @SalesOrderDetails, @UpdateStock",
                    parameters
                );

                // 取得最新的 OrderId
                var newOrderId = await _context.SuppliesSalesOrders
                    .OrderByDescending(o => o.SuppliesSalesOrderId)
                    .Select(o => o.SuppliesSalesOrderId)
                    .FirstOrDefaultAsync();

                // 條碼內容 (掃描後要打 API 更新狀態)
                var folderPath = Path.Combine(_env.WebRootPath, "qrcodes");
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string serverIp = GetServerIp();
                //5000改7124
                //string qrText = $"http://{serverIp}:7124/api/SuppliesSalesOrders/{newOrderId}/status?status=已到貨";
                //string qrText = $"http://{serverIp}:7124/api/SuppliesSalesOrders/{newOrderId}/quick-confirm?status=已到貨";
                // 使用 ngrok 的公開網址
                // ngrok Forwarding 網址

                string publicBase = "https://a42306927f3b.ngrok-free.app";
                // 把「已到貨」轉成 URL 安全字串，避免中文在掃描 QR Code 時被吃掉
                string status = System.Net.WebUtility.UrlEncode("已到貨");
                // 產生完整的 API 網址
                string qrText = $"{publicBase}/api/SuppliesSalesOrders/{newOrderId}/quick-confirm?status={status}";
                // Debug 輸出，方便確認 QR Code 文字
                Console.WriteLine("產生的 QR 文字：" + qrText);



                string? qrcodeUrl = null;
                try
                {
                    // 產生條碼
                    var writer = new ZXing.BarcodeWriterPixelData
                    {
                        Format = BarcodeFormat.QR_CODE,
                        Options = new EncodingOptions
                        {
                            Height = 300,
                            Width = 300,
                            Margin = 1
                        }
                    };
                    var pixelData = writer.Write(qrText);
                    var fileName = $"qrcode_{newOrderId}.png";
                    var filePath = Path.Combine(folderPath, fileName);

                    using (var image = Image.LoadPixelData<Rgba32>(pixelData.Pixels, pixelData.Width, pixelData.Height))
                    {
                        await image.SaveAsync(filePath, new PngEncoder());
                    }

                    qrcodeUrl = $"/qrcodes/{fileName}";
                }
                catch (Exception ex)
                {
                    // 記 Log，但不要中斷主要流程
                    Console.WriteLine("Barcode generation failed: " + ex);
                    throw; // 直接拋出，方便你用 Swagger/前端看錯誤訊息
                }

                return Ok(new
                {
                    Message = "Order created successfully",
                    OrderId = newOrderId,
                    QrcodeUrl = qrcodeUrl
                });


            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    Error = ex.Message,
                    Inner = ex.InnerException?.Message,
                    Stack = ex.StackTrace
                });
            }
        }

        // ✅ 給 QR 掃用：允許匿名（若全站有驗證）
        [AllowAnonymous]
        [HttpGet("{id}/quick-confirm")]
        public async Task<IActionResult> QuickConfirm(int id, [FromQuery] string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Content("<h3>缺少狀態參數</h3>", "text/html; charset=utf-8");

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@SuppliesSalesOrderID", id),
                    new SqlParameter("@NewStatus", status)
                };

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC sp_UpdateSalesOrderStatus @SuppliesSalesOrderID, @NewStatus",
                    parameters
                );

                // ✅ 推播事件到前端
                await _hubContext.Clients.All.SendAsync("OrderStatusChanged", id, status);

                // 回手機一個簡單頁面
                var html = $"""
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <div style="font-family:system-ui;padding:24px">
                  <h2>狀態已更新</h2>
                  <p>訂單 #{id} 已更新為：{System.Net.WebUtility.HtmlEncode(status)}</p>
                </div>
                """;
                return Content(html, "text/html; charset=utf-8");
            }
            catch (Exception ex)
            {
                var html = $"""
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <div style="font-family:system-ui;padding:24px;color:#c00">
                  <h2>更新失敗</h2>
                  <pre>{System.Net.WebUtility.HtmlEncode(ex.Message)}</pre>
                </div>
                """;
                return Content(html, "text/html; charset=utf-8");
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
