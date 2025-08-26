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

            // 從配置中獲取 PayPal 憑證
            var clientId = _config["PayPal:ClientId"];
            var clientSecret = _config["PayPal:ClientSecret"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                throw new ArgumentException("PayPal Client ID 或 Secret 未在配置中定義。請檢查 appsettings.json。");
            }

            // 初始化 PayPal 客戶端
            var environment = new SandboxEnvironment(clientId, clientSecret);
            _payPalClient = new PayPalHttpClient(environment);
        }

        // GET: api/rooms
        [HttpGet]
        [AllowAnonymous] // 身分驗證 JWT Token
        public async Task<IActionResult> GetRooms()
        {
            try
            {
                var rooms = await _context.RoomTables
                    .Include(r => r.RoomBeds)
                    .Include(r => r.RoomImages)
                    .Where(r => r.FRoomId > 0)
                    .Select(r => new RoomDto
                    {
                        FRoomId = r.FRoomId,
                        FRoomAlias = r.FRoomAlias ?? "",
                        Image = r.RoomImages.Where(i => !string.IsNullOrEmpty(i.ImagePath)).Select(i => i.ImagePath).FirstOrDefault() ?? "rooms/default-room-image.jpg",
                        FRoomDescription = r.FRoomDescription ?? "",
                        FRoomPrice = r.FRoomPrice,
                        FBedCount = r.FBedCount,
                        IsAvailable = r.RoomBeds.Any(b => b.FBedStatus == true),
                        AvailableBeds = r.RoomBeds.Count(b => b.FBedStatus == true)
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

        // GET: api/rooms/5
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetRoomDetail(int id)
        {
            try
            {
                var room = await _context.RoomTables
                    .Include(r => r.RoomImages)
                    .Include(r => r.RoomBeds)
                    .Where(r => r.FRoomId == id)
                    .Select(r => new RoomDetailDto
                    {
                        FRoomId = r.FRoomId,
                        FRoomAlias = r.FRoomAlias ?? "",
                        Images = r.RoomImages.Where(i => !string.IsNullOrEmpty(i.ImagePath)).Select(i => i.ImagePath).ToArray(),
                        FRoomDescription = r.FRoomDescription ?? "",
                        FRoomPrice = r.FRoomPrice,
                        FBedCount = r.FBedCount,
                        IsAvailable = r.RoomBeds.Any(b => b.FBedStatus == true),
                        AvailableBeds = r.RoomBeds.Count(b => b.FBedStatus == true)
                    })
                    .FirstOrDefaultAsync();
                if (room == null)
                {
                    return NotFound(new { message = "房間不存在" });
                }
                return Ok(new { message = "成功獲取房間詳情", data = room });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetRoomDetail 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        // POST: api/rooms/reservations
        [HttpPost("reservations")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateReservation([FromBody] RoomVisitReservationDto reservation)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { message = "驗證失敗", errors = ModelState });
                }
                var newReservation = new RoomVisitReservation
                {
                    FName = reservation.FName,
                    FEmail = reservation.FEmail,
                    FPhoneOrLineId = reservation.FPhoneOrLineId,
                    FReservationDate = reservation.FReservationDate,
                    FCreatedAt = DateTime.Now
                };
                _context.RoomVisitReservations.Add(newReservation);
                await _context.SaveChangesAsync();
                return Ok(new { message = "預約提交成功", data = newReservation.FReservationId });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"CreateReservation 資料庫錯誤: {ex.InnerException?.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "資料庫錯誤", error = ex.InnerException?.Message ?? ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateReservation 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        // POST: api/rooms/bookings (改為 [Authorize]，從 JWT 取 member)
        [HttpPost("bookings")]
        [Authorize]
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

                var bed = await _context.RoomBeds
                    .FirstOrDefaultAsync(b => b.FBedId == booking.FBedId && b.FBedStatus == true);
                if (bed == null)
                {
                    return BadRequest(new { message = "所選床位不可用" });
                }

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
                    FBedId = booking.FBedId,
                    FCheckInDate = booking.FCheckInDate,
                    FBillingAmount = booking.FBillingAmount,
                    FBillingDate = DateTime.UtcNow,
                    FPaymentMethod = booking.FPaymentMethod,
                    FBillingStatus = !string.IsNullOrEmpty(booking.FPaypalOrderId),
                    FPaypalOrderId = booking.FPaypalOrderId
                };
                _context.RoomOccupancies.Add(occupancy);
                bed.FBedStatus = false;
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