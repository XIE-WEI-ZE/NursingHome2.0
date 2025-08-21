using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Linq;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoomsController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IConfiguration _config;

        public RoomsController(DbNursingHomeContext context, IConfiguration config)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        // GET: api/rooms
        [HttpGet]
        [AllowAnonymous] //身分驗證JWT Token
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
                        Image = "images/" + (r.RoomImages.Select(i => i.ImagePath).FirstOrDefault() ?? "rooms/default.jpg"),
                        FRoomDescription = r.FRoomDescription ?? "",
                        FRoomPrice = r.FRoomPrice,
                        IsAvailable = r.RoomBeds.Any(b => b.FBedStatus == true)
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
        public async Task<IActionResult> GetRoom(int id)
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
                        Images = r.RoomImages.Select(i => "images/" + i.ImagePath).ToArray().Length > 0
    ? r.RoomImages.Select(i => "images/" + i.ImagePath).ToArray()
    : new[] { "images/rooms/default.jpg" },
                        FRoomDescription = r.FRoomDescription ?? "",
                        FRoomPrice = r.FRoomPrice,
                        FBedCount = r.FBedCount,
                        IsAvailable = r.RoomBeds.Any(b => b.FBedStatus == true)
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
                Console.WriteLine($"GetRoom 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
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
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    Console.WriteLine("驗證錯誤: " + string.Join(", ", errors)); // log 到 console
                    return BadRequest(new { message = "驗證失敗", errors });
                }

                var entity = new RoomVisitReservation
                {
                    FName = reservation.FName,
                    FEmail = reservation.FEmail,
                    FPhoneOrLineId = reservation.FPhoneOrLineId,
                    FReservationDate = reservation.FReservationDate,
                    FCreatedAt = DateTime.Now
                };
                _context.RoomVisitReservations.Add(entity);
                await _context.SaveChangesAsync();

                return Ok(new { message = "預約提交成功", reservationId = entity.FReservationId });
            }
            catch (DbUpdateException ex)
            {
                // 資料庫更新異常（如外鍵約束）
                Console.WriteLine($"CreateReservation 資料庫錯誤: {ex.InnerException?.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "資料庫錯誤", error = ex.InnerException?.Message ?? ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateReservation 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }
        /// <summary>
        /// 創建新的房間預訂。
        /// </summary>
        /// <param name="booking">預訂詳情</param>
        /// <returns>成功時返回預訂 ID</returns>
        /// <response code="200">預訂提交成功</response>
        /// <response code="400">驗證失敗或床位不可用</response>
        /// <response code="500">內部伺服器錯誤</response>
        // POST: api/rooms/bookings
        [HttpPost("bookings")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateBooking([FromBody] RoomOccupancyDto booking)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { message = "驗證失敗", errors = ModelState });
                }
                var bed = await _context.RoomBeds
                    .FirstOrDefaultAsync(b => b.FBedId == booking.FBedId && b.FBedStatus == true);
                if (bed == null)
                {
                    return BadRequest(new { message = "所選床位不可用" });
                }
                var occupancy = new RoomOccupancy
                {
                    FBedId = booking.FBedId,
                    FCheckInDate = booking.FCheckInDate,
                    FBillingAmount = booking.FBillingAmount,
                    FBillingDate = DateTime.Now,
                    FPaymentMethod = booking.FPaymentMethod,
                    FBillingStatus = false
                };
                _context.RoomOccupancies.Add(occupancy);
                bed.FBedStatus = false; // 標記為不可用
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