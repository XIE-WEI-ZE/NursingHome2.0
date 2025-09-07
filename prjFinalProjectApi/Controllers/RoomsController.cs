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
using System.Net.Mail;
using System.Net;

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
        //訂房支付
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

                        selectedBed.FBedStatus = true;
                        member.FResidesInCareHomeStatus = true;
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

                        await transaction.CommitAsync();

                        // 訂房成功後發送郵件通知
                        var htmlBody = $@"
                            <h2>訂房成功通知</h2>
                            <p>親愛的 {member.FName}，</p>
                            <p>您的訂房已成功！訂房ID: <strong>{occupancy.FOccupancyId}</strong></p>
                            <p>入住日期: {booking.FCheckInDate:yyyy-MM-dd}</p>
                            <p>房間: {room.FRoomAlias}</p>
                            <p>支付方式: {booking.FPaymentMethod}</p>
                            <p>感謝使用我們的服務！如有任何問題，請聯繫客服電話:09-8888-8888</p>
                            <p>此為系統自動發送，請勿直接回覆。</p>
                            <p>建立時間: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} (UTC)</p>";
                        SendEmail(member.FEmail, "訂房成功通知", htmlBody);

                        return Ok(new { message = "預訂提交成功", occupancyId = occupancy.FOccupancyId });
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
        //預約參訪
        [HttpPost("reservations")]
        [AllowAnonymous] // 允許匿名訪問，因為這是公開預約
        public async Task<IActionResult> CreateReservation([FromBody] RoomVisitReservationDto dto)
        {
            try
            {
                // 手動驗證 DTO（雖然 DataAnnotations 已定義，但為了安全再檢查）
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { message = "輸入資料無效", errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
                }

                // 檢查同一個信箱一天內是否已預約
                var oneDayAgo = DateTime.UtcNow.AddDays(-1);
                var existingReservation = await _context.RoomVisitReservations
                    .Where(r => r.FEmail == dto.FEmail && r.FCreatedAt >= oneDayAgo)
                    .FirstOrDefaultAsync();

                if (existingReservation != null)
                {
                    return BadRequest(new { message = "該信箱一天內已預約，請勿重複預約" });
                }

                // 映射 DTO 到 Entity
                var reservation = new RoomVisitReservation
                {
                    FName = dto.FName,
                    FEmail = dto.FEmail,
                    FPhoneOrLineId = dto.FPhoneOrLineId,
                    FReservationDate = dto.FReservationDate,
                    FCreatedAt = DateTime.UtcNow, // 自動生成
                    FStatus = false
                };

                // 保存到 DB
                _context.RoomVisitReservations.Add(reservation);
                await _context.SaveChangesAsync();

                // 自訂發送郵件通知
                var htmlBody = $@"
                    <h2>預約成功通知</h2>
                    <p>親愛的 {reservation.FName}，</p>
                    <p>您的預約已成功！預約ID: <strong>{reservation.FReservationId}</strong></p>
                    <p>預約日期: {reservation.FReservationDate:yyyy-MM-dd}</p>
                    <p>感謝使用我們的服務！如有任何問題，請聯繫客服電話:09-8888-8888</p>
                    <p>此為系統自動發送，請勿直接回覆。</p>
                    <p>建立時間: {reservation.FCreatedAt:yyyy-MM-dd HH:mm:ss} (UTC)</p>";
                SendEmail(reservation.FEmail, "預約成功通知", htmlBody);

                return Ok(new { message = "預約成功", data = reservation.FReservationId });
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
        //預約參訪-郵件的stmp設定
        private void SendEmail(string to, string subject, string htmlBody)
        {
            var smtpHost = _config["Smtp:Host"] ?? "smtp.gmail.com";
            var smtpPort = int.Parse(_config["Smtp:Port"] ?? "587");
            var smtpAccount = _config["Smtp:Account"] ?? "jkldsa1347@gmail.com";
            var smtpPassword = _config["Smtp:Password"] ?? "fddnvshelpyycemg";
            var fromName = _config["Smtp:FromName"] ?? "Nursing Home";

            var smtpClient = new SmtpClient
            {
                Host = smtpHost,
                Port = smtpPort,
                EnableSsl = true,
                Credentials = new NetworkCredential
                {
                    UserName = smtpAccount,
                    Password = smtpPassword
                }
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(smtpAccount, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            mailMessage.To.Add(to);

            try
            {
                smtpClient.Send(mailMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"郵件發送錯誤: {ex.Message}");
                // 可以選擇記錄錯誤或拋出異常，根據需求處理
            }
        }
    }
}