using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Linq;
using System.Security.Claims;
using PayPalCheckoutSdk.Orders;
using PayPalCheckoutSdk.Core;

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
                    return BadRequest(new { message = "驗證失敗", errors = ModelState });
                }

                var account = User.FindFirstValue(ClaimTypes.Name);
                var member = await _context.Members.FirstOrDefaultAsync(m => m.FAccount == account);
                if (member == null || member.FResidesInCareHomeStatus == true)
                    return BadRequest(new { message = "會員不存在或已入住" });

                // 隨機選擇可用床位
                var availableBeds = await _context.RoomBeds
                    .Where(b => b.FRoomId == booking.FRoomId && !(b.FBedStatus ?? false)) // 修正: False/null = 可用
                    .ToListAsync();
                if (!availableBeds.Any())
                {
                    return BadRequest(new { message = "所選房間無可用床位" });
                }
                var selectedBed = availableBeds.OrderBy(b => Guid.NewGuid()).First(); // 隨機選擇

                // 驗證 PayPal 訂單（如果提供）
                if (!string.IsNullOrEmpty(booking.FPaypalOrderId))
                {
                    var request = new OrdersGetRequest(booking.FPaypalOrderId);
                    var response = await _payPalClient.Execute(request);
                    if (response.StatusCode != System.Net.HttpStatusCode.OK || response.Result<Order>().Status != "COMPLETED")
                    {
                        return BadRequest(new { message = "PayPal 訂單驗證失敗" });
                    }
                }

                var occupancy = new RoomOccupancy
                {
                    FMemberId = member.FMemberId,
                    FBedId = selectedBed.FBedId,
                    FCheckInDate = booking.FCheckInDate,
                    FBillingAmount = booking.FBillingAmount,
                    FBillingDate = DateTime.UtcNow,
                    FPaymentMethod = booking.FPaymentMethod,
                    FBillingStatus = !string.IsNullOrEmpty(booking.FPaypalOrderId),
                    FPaypalOrderId = booking.FPaypalOrderId
                };
                _context.RoomOccupancies.Add(occupancy);
                selectedBed.FBedStatus = true; // 設為占用 (True)
                member.FResidesInCareHomeStatus = true;
                await _context.SaveChangesAsync();

                return Ok(new { message = "預訂提交成功", occupancyId = occupancy.FOccupancyId });
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