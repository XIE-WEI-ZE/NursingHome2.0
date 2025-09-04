using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using PayPalCheckoutSdk.Orders;
using PayPalCheckoutSdk.Core;
using Microsoft.Extensions.Configuration;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MemberMyRoomController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly PayPalHttpClient _payPalClient;
        private readonly IConfiguration _configuration;

        public MemberMyRoomController(DbNursingHomeContext context, IConfiguration configuration)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            var clientId = _configuration["PayPal:ClientId"];
            var clientSecret = _configuration["PayPal:ClientSecret"];
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                throw new ArgumentException("PayPal Client ID 或 Secret 未在配置中定義。請檢查 appsettings.json。");
            }
            var environment = new SandboxEnvironment(clientId, clientSecret);
            _payPalClient = new PayPalHttpClient(environment);
        }

        [HttpGet("room")]
        [Authorize(Policy = "MemberOnly")]
        public async Task<IActionResult> GetMemberRoom()
        {
            try
            {
                var account = User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(account))
                {
                    return Unauthorized(new { message = "未授權用戶" });
                }

                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.FAccount == account);
                if (member == null)
                {
                    return NotFound(new { message = "會員不存在" });
                }

                var occupancy = await _context.RoomOccupancies
                    .Where(o => o.FMemberId == member.FMemberId && o.FCheckOutDate == null)
                    .Include(o => o.FBed)
                    .ThenInclude(b => b.FRoom)
                    .ThenInclude(r => r.RoomImages)
                    .FirstOrDefaultAsync();

                if (occupancy == null)
                {
                    return NotFound(new { message = "會員無當前房間資料" });
                }

                var latestPayment = await _context.RoomPaymentHistories
                    .Where(p => p.FOccupancyId == occupancy.FOccupancyId)
                    .OrderByDescending(p => p.FBillingDate)
                    .FirstOrDefaultAsync();

                if (latestPayment != null)
                {
                    var thirtyDaysLater = latestPayment.FBillingDate.AddDays(30);
                    var now = DateTime.UtcNow;

                    if (now > thirtyDaysLater && occupancy.FBillingStatus == true)
                    {
                        occupancy.FBillingStatus = false;
                        await _context.SaveChangesAsync();
                    }
                }

                var roomTable = occupancy.FBed.FRoom;
                var response = new
                {
                    member = new { fName = member.FName ?? "未提供", fIdNumber = member.FIdNumber ?? "未提供" },
                    roomTable = new
                    {
                        fRoomName = roomTable.FRoomName ?? "未分配",
                        fRoomAlias = roomTable.FRoomAlias ?? "未命名",
                        fRoomType = roomTable.FRoomType ?? false,
                        fRoomPrice = roomTable.FRoomPrice,
                        images = roomTable.RoomImages?.Select(i => i.ImagePath).ToArray() ?? new string[0]
                    },
                    roomBed = new { fBedCode = occupancy.FBed.FBedCode ?? "未分配" },
                    roomOccupancy = new { fBillingStatus = occupancy.FBillingStatus ?? false, fOccupancyId = occupancy.FOccupancyId }, // 確保返回 fOccupancyId
                    paymentHistory = await _context.RoomPaymentHistories
                        .Where(p => p.FOccupancyId == occupancy.FOccupancyId)
                        .OrderByDescending(p => p.FBillingDate)
                        .Select(p => new
                        {
                            p.FBillingAmount,
                            p.FBillingDate,
                            p.FPaymentMethod,
                            p.FPaypalOrderId
                        })
                        .ToListAsync()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetMemberRoom 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        [HttpPost("room/record-payment")]
        [Authorize(Policy = "MemberOnly")]
        public async Task<IActionResult> RecordPayment([FromBody] RoomPaymentDto paymentDto)
        {
            try
            {
                var account = User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(account))
                {
                    return Unauthorized(new { message = "未授權用戶" });
                }

                Console.WriteLine($"Received payment request: OccupancyId={paymentDto.OccupancyId}, Amount={paymentDto.Amount}, PaypalOrderId={paymentDto.PaypalOrderId}");

                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.FAccount == account);
                if (member == null)
                {
                    return NotFound(new { message = "會員不存在" });
                }

                var occupancy = await _context.RoomOccupancies
                    .Include(o => o.FBed)
                    .FirstOrDefaultAsync(o => o.FOccupancyId == paymentDto.OccupancyId && o.FMemberId == member.FMemberId);
                if (occupancy == null)
                {
                    return NotFound(new { message = "入住記錄不存在" });
                }

                // 驗證 PayPal 訂單（如果使用 PayPal）
                if (!string.IsNullOrEmpty(paymentDto.PaypalOrderId))
                {
                    var request = new OrdersGetRequest(paymentDto.PaypalOrderId);
                    var response = await _payPalClient.Execute(request);
                    if (response.StatusCode != System.Net.HttpStatusCode.OK || response.Result<Order>().Status != "COMPLETED")
                    {
                        return BadRequest(new { message = "PayPal 訂單驗證失敗", details = response.Result<Order>()?.Status });
                    }
                }

                // 記錄支付
                var payment = new RoomPaymentHistory
                {
                    FOccupancyId = occupancy.FOccupancyId,
                    FBillingAmount = paymentDto.Amount,
                    FBillingDate = DateTime.UtcNow,
                    FPaymentMethod = string.IsNullOrEmpty(paymentDto.PaypalOrderId) ? "Credit Card" : "PayPal",
                    FBillingStatus = true,
                    FPaypalOrderId = paymentDto.PaypalOrderId
                };

                _context.RoomPaymentHistories.Add(payment);
                occupancy.FBillingStatus = true; // 更新繳費狀態
                await _context.SaveChangesAsync();

                Console.WriteLine($"Payment recorded successfully for OccupancyId={paymentDto.OccupancyId}");

                return Ok(new { message = "支付記錄成功", occupancyId = occupancy.FOccupancyId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RecordPayment 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        [HttpGet("receipt/{receiptId}")]
        [Authorize(Policy = "MemberOnly")]
        public async Task<IActionResult> GetReceipt(int receiptId)
        {
            try
            {
                var receipt = await _context.RoomPaymentReceipts
                    .Include(r => r.FPayment)
                    .ThenInclude(p => p.FOccupancy)
                    .FirstOrDefaultAsync(r => r.FReceiptId == receiptId);

                if (receipt == null)
                {
                    return NotFound(new { message = "收據不存在" });
                }

                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.FMemberId == receipt.FPayment.FOccupancy.FMemberId);
                if (member == null || member.FAccount != User.FindFirstValue(ClaimTypes.Name))
                {
                    return Unauthorized(new { message = "無權存取此收據" });
                }

                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", receipt.FReceiptFilePath);
                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound(new { message = "收據檔案不存在" });
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                return File(fileBytes, "application/pdf", $"{receipt.FReceiptNumber}.pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetReceipt 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }
    }
}