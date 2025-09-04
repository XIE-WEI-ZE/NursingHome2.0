using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Linq;
using System.Security.Claims;
using PayPalCheckoutSdk.Orders;
using PayPalCheckoutSdk.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Security.AccessControl;
using System.Security.Principal;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoomsController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IConfiguration _config;
        private readonly PayPalHttpClient _payPalClient;

        public RoomsController(DbNursingHomeContext context, IConfiguration config)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            var clientId = _config["PayPal:ClientId"];
            var clientSecret = _config["PayPal:ClientSecret"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                throw new ArgumentException("PayPal Client ID 或 Secret 未在配置中定義。請檢查 appsettings.json。");
            }

            var environment = new SandboxEnvironment(clientId, clientSecret);
            _payPalClient = new PayPalHttpClient(environment);
        }

        // GET: api/rooms
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetRooms()
        {
            try
            {
                var rooms = await _context.RoomTables
                    .Include(r => r.RoomBeds)
                    .Include(r => r.RoomImages)
                    .Where(r => r.FRoomId > 0 && r.FRoomStatus.Trim() == "active") // 只顯示上架房間
                    .Select(r => new RoomDto
                    {
                        FRoomId = r.FRoomId,
                        FRoomAlias = r.FRoomAlias ?? "",
                        Image = r.RoomImages.Where(i => !string.IsNullOrEmpty(i.ImagePath)).Select(i => i.ImagePath).FirstOrDefault() ?? "rooms/default-room-image.jpg",
                        FRoomDescription = r.FRoomDescription ?? "",
                        FRoomPrice = r.FRoomPrice,
                        FBedCount = r.FBedCount,
                        IsAvailable = r.RoomBeds.Any(b => !(b.FBedStatus ?? false)), // 修正: False/null = 可用
                        AvailableBeds = r.RoomBeds.Count(b => !(b.FBedStatus ?? false)) // 修正: 計算可用床位
                    })
                    .ToListAsync();
                if (!rooms.Any())
                {
                    return NotFound(new { message = "無可用房間資料" });
                }
                return Ok(new { message = "成功獲取房間列表", data = rooms });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetRooms 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        // GET: api/rooms/{id}
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetRoomById(int id)
        {
            try
            {
                var room = await _context.RoomTables
                    .Include(r => r.RoomBeds)
                    .Include(r => r.RoomImages)
                    .Where(r => r.FRoomId == id)
                    .Select(r => new RoomDetailDto
                    {
                        FRoomId = r.FRoomId,
                        FRoomAlias = r.FRoomAlias ?? "",
                        Images = r.RoomImages.Where(i => !string.IsNullOrEmpty(i.ImagePath)).Select(i => i.ImagePath).ToArray(),
                        FRoomDescription = r.FRoomDescription ?? "",
                        FRoomPrice = r.FRoomPrice,
                        FBedCount = r.FBedCount,
                        IsAvailable = r.RoomBeds.Any(b => !(b.FBedStatus ?? false)), // 修正: False/null = 可用
                        AvailableBeds = r.RoomBeds.Count(b => !(b.FBedStatus ?? false)) // 修正: 計算可用床位
                    })
                    .FirstOrDefaultAsync();

                if (room == null || (await _context.RoomTables.FindAsync(id))?.FRoomStatus.Trim() != "active")
                {
                    return NotFound(new { message = "房間不存在或已下架" });
                }
                return Ok(new { message = "成功獲取房間詳情", data = room });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetRoomById 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        // POST: api/rooms/bookings
        [HttpPost("bookings")]
        [Authorize(Policy = "MemberOnly")]
        public async Task<IActionResult> CreateBooking([FromBody] RoomOccupancyDto booking)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { message = "驗證失敗", errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
                }

                var account = User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(account))
                {
                    return Unauthorized(new { message = "未授權用戶" });
                }

                var member = await _context.Members.FirstOrDefaultAsync(m => m.FAccount == account);
                if (member == null)
                {
                    return NotFound(new { message = "會員不存在" });
                }

                if (member.FResidesInCareHomeStatus == true)
                {
                    return BadRequest(new { message = "您已入住，不能重複預訂" });
                }

                var room = await _context.RoomTables
                    .Include(r => r.RoomBeds)
                    .FirstOrDefaultAsync(r => r.FRoomId == booking.FRoomId);

                if (room == null)
                {
                    return NotFound(new { message = "房間不存在" });
                }

                var availableBeds = room.RoomBeds.Where(b => !(b.FBedStatus ?? false)).ToList();
                if (!availableBeds.Any())
                {
                    return BadRequest(new { message = "所選房間無可用床位" });
                }

                var selectedBed = availableBeds.OrderBy(b => Guid.NewGuid()).First();

                // 支付驗證
                if (booking.FPaymentMethod.ToLower() == "paypal" && !string.IsNullOrEmpty(booking.FPaypalOrderId))
                {
                    var request = new OrdersGetRequest(booking.FPaypalOrderId);
                    var response = await _payPalClient.Execute(request);
                    if (response.StatusCode != System.Net.HttpStatusCode.OK || response.Result<Order>().Status != "COMPLETED")
                    {
                        return BadRequest(new { message = "PayPal 訂單驗證失敗" });
                    }
                }
                else if (booking.FPaymentMethod.ToLower() == "信用卡")
                {
                    if (string.IsNullOrEmpty(booking.FPaypalOrderId)) // 模擬信用卡支付成功
                    {
                        // 這裡可添加信用卡驗證邏輯（暫時跳過）
                    }
                }
                else
                {
                    return BadRequest(new { message = "不支持的支付方式" });
                }

                using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    try
                    {
                        var occupancy = new RoomOccupancy
                        {
                            FMemberId = member.FMemberId,
                            FBedId = selectedBed.FBedId,
                            FCheckInDate = booking.FCheckInDate,
                            FBillingStatus = booking.FPaymentMethod.ToLower() == "paypal" ? !string.IsNullOrEmpty(booking.FPaypalOrderId) : true
                        };

                        _context.RoomOccupancies.Add(occupancy);
                        await _context.SaveChangesAsync();

                        var payment = new RoomPaymentHistory
                        {
                            FOccupancyId = occupancy.FOccupancyId,
                            FBillingAmount = booking.FBillingAmount,
                            FBillingDate = DateTime.UtcNow,
                            FPaymentMethod = booking.FPaymentMethod,
                            FBillingStatus = booking.FPaymentMethod.ToLower() == "paypal" ? !string.IsNullOrEmpty(booking.FPaypalOrderId) : true,
                            FPaypalOrderId = booking.FPaypalOrderId
                        };

                        _context.RoomPaymentHistories.Add(payment);
                        await _context.SaveChangesAsync();

                        // 移除 RoomPaymentReceipt 邏輯，因為不再生成 PDF
                        // var receiptNumber = $"REC-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(100, 999)}";
                        // var receiptFilePath = GenerateReceiptPdf(receiptNumber, payment);
                        // var receipt = new RoomPaymentReceipt
                        // {
                        //     FPaymentId = payment.FPaymentId,
                        //     FReceiptNumber = receiptNumber,
                        //     FReceiptDate = DateTime.UtcNow,
                        //     FReceiptFilePath = receiptFilePath,
                        //     FNotes = "初始入住繳費收據"
                        // };
                        // _context.RoomPaymentReceipts.Add(receipt);
                        // await _context.SaveChangesAsync();

                        selectedBed.FBedStatus = true;
                        member.FResidesInCareHomeStatus = true;

                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        return Ok(new { message = "預訂提交成功", occupancyId = occupancy.FOccupancyId }); // 移除 receiptId
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Console.WriteLine($"CreateBooking 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                        Console.WriteLine($"內部異常: {ex.InnerException?.Message}");
                        return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message, innerError = ex.InnerException?.Message });
                    }
                }
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"CreateBooking 資料庫錯誤: {ex.InnerException?.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "資料庫錯誤", error = ex.InnerException?.Message ?? ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateBooking 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }
    }
}
