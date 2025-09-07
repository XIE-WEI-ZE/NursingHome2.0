using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoomsErpController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IConfiguration _config;

        public RoomsErpController(DbNursingHomeContext context, IConfiguration config)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        // GET: api/RoomsErp
        [HttpGet]
        public async Task<IActionResult> GetRooms()
        {
            try
            {
                var rooms = await _context.RoomTables
                    .Include(r => r.RoomBeds)
                    .ThenInclude(b => b.RoomOccupancies)
                    .Include(r => r.RoomImages)
                    .Where(r => r.FRoomId > 0)
                    .Select(r => new
                    {
                        fRoomId = r.FRoomId,
                        fRoomName = r.FRoomName ?? "",
                        fRoomAlias = r.FRoomAlias ?? "",
                        images = r.RoomImages.Where(i => !string.IsNullOrEmpty(i.ImagePath)).Select(i => i.ImagePath).ToArray(),
                        fRoomDescription = r.FRoomDescription ?? "",
                        fRoomPrice = r.FRoomPrice ?? 0,
                        fBedCount = r.FBedCount ?? 0,
                        isAvailable = r.RoomBeds.Any(b => !(b.FBedStatus ?? false)),
                        availableBeds = r.RoomBeds.Count(b => !(b.FBedStatus ?? false)),
                        image = r.RoomImages.Where(i => !string.IsNullOrEmpty(i.ImagePath)).Select(i => i.ImagePath).FirstOrDefault() ?? "rooms/default-room-image.jpg",
                        fRoomStatus = r.FRoomStatus != null ? r.FRoomStatus.Trim() : "active",
                        fRoomType = r.FRoomType ?? false,
                        lastUpdated = r.FLastUpdated,
                        occupiedInfo = (from b in r.RoomBeds
                                        from o in b.RoomOccupancies
                                        where o.FCheckOutDate == null && o.FMemberId.HasValue
                                        join m in _context.Members on o.FMemberId equals m.FMemberId into members
                                        from m in members.DefaultIfEmpty()
                                        select new
                                        {
                                            memberName = m != null ? m.FName ?? "未知" : "未知",
                                            phone = m != null ? m.FPhone ?? "無" : "無",
                                            bedCode = b.FBedCode,
                                            fOccupancyId = o.FOccupancyId,
                                            fCheckInDate = o.FCheckInDate != null ? o.FCheckInDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : null,
                                            fCheckOutDate = o.FCheckOutDate != null ? o.FCheckOutDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : null
                                        }).ToList()
                    }).ToListAsync();

                return Ok(new { data = rooms });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetRooms 錯誤: {ex.Message}");
                return StatusCode(500, new { message = "獲取房間失敗", error = ex.Message });
            }
        }

        // POST: api/RoomsErp
        [HttpPost]
        public async Task<IActionResult> CreateRoom()
        {
            try
            {
                var form = await Request.ReadFormAsync();

                if (!form.ContainsKey("fRoomName") || string.IsNullOrWhiteSpace(form["fRoomName"].ToString()))
                    return BadRequest(new { message = "房間名稱為必填" });
                if (!form.ContainsKey("fRoomAlias") || string.IsNullOrWhiteSpace(form["fRoomAlias"].ToString()))
                    return BadRequest(new { message = "別名為必填" });

                var room = new RoomTable
                {
                    FRoomName = form["fRoomName"].ToString(),
                    FRoomAlias = form["fRoomAlias"].ToString(),
                    FRoomDescription = form.ContainsKey("fRoomDescription") ? form["fRoomDescription"].ToString() : null,
                    FRoomPrice = int.TryParse(form["fRoomPrice"], out int price) ? Math.Max(0, price) : 0,
                    FBedCount = int.TryParse(form["fBedCount"], out int bedCount) ? bedCount : 0,
                    FRoomType = bool.TryParse(form["fRoomType"], out bool roomType) ? roomType : false,
                    FRoomStatus = form.ContainsKey("fRoomStatus") ? form["fRoomStatus"].ToString() : "active",
                    FLastUpdated = DateTime.Now
                };

                // 驗證床位數
                if (room.FBedCount == null || !new int[] { 1, 2, 4, 6 }.Contains(room.FBedCount.Value))
                    return BadRequest(new { message = "床位數必須為 1、2、4 或 6" });

                _context.RoomTables.Add(room);
                await _context.SaveChangesAsync();

                // 生成床位
                var bedCodes = new List<string> { "A", "B", "C", "D", "E", "F" }.Take(room.FBedCount.Value).ToList();
                foreach (var code in bedCodes)
                {
                    var bed = new RoomBed
                    {
                        FRoomId = room.FRoomId,
                        FBedCode = code,
                        FBedStatus = false
                    };
                    _context.RoomBeds.Add(bed);
                }

                // 處理圖片
                var roomImages = form.Files.GetFiles("RoomImages");
                if (roomImages != null)
                {
                    foreach (var file in roomImages)
                    {
                        if (file.Length > 0)
                        {
                            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/rooms", fileName);
                            using (var stream = new FileStream(path, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }
                            _context.RoomImages.Add(new RoomImage
                            {
                                FRoomId = room.FRoomId,
                                ImagePath = $"images/rooms/{fileName}"
                            });
                        }
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "房間新增成功", roomId = room.FRoomId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateRoom 錯誤: {ex.Message}");
                return StatusCode(500, new { message = "新增房間失敗", error = ex.Message });
            }
        }

        // PUT: api/RoomsErp/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRoom(int id)
        {
            try
            {
                var room = await _context.RoomTables
                    .Include(r => r.RoomBeds)
                    .Include(r => r.RoomImages)
                    .FirstOrDefaultAsync(r => r.FRoomId == id);

                if (room == null)
                    return NotFound(new { message = "房間不存在" });

                var form = await Request.ReadFormAsync();

                if (!form.ContainsKey("fRoomName") || string.IsNullOrWhiteSpace(form["fRoomName"].ToString()))
                    return BadRequest(new { message = "房間名稱為必填" });
                if (!form.ContainsKey("fRoomAlias") || string.IsNullOrWhiteSpace(form["fRoomAlias"].ToString()))
                    return BadRequest(new { message = "別名為必填" });

                room.FRoomName = form["fRoomName"].ToString();
                room.FRoomAlias = form["fRoomAlias"].ToString();
                room.FRoomDescription = form.ContainsKey("fRoomDescription") ? form["fRoomDescription"].ToString() : room.FRoomDescription;
                room.FRoomPrice = int.TryParse(form["fRoomPrice"], out int price) ? Math.Max(0, price) : room.FRoomPrice ?? 0;
                int newBedCount = int.TryParse(form["fBedCount"], out int bedCount) ? bedCount : room.FBedCount.GetValueOrDefault(0);
                room.FRoomType = bool.TryParse(form["fRoomType"], out bool roomType) ? roomType : room.FRoomType ?? false;
                room.FRoomStatus = form.ContainsKey("fRoomStatus") ? form["fRoomStatus"].ToString() : room.FRoomStatus;
                room.FLastUpdated = DateTime.Now;

                // 驗證床位數
                if (!new int[] { 1, 2, 4, 6 }.Contains(newBedCount))
                    return BadRequest(new { message = "床位數必須為 1、2、4 或 6" });

                // 更新床位
                if (newBedCount != room.FBedCount.GetValueOrDefault(0))
                {
                    var existingBeds = room.RoomBeds.ToList();
                    var bedCodes = new List<string> { "A", "B", "C", "D", "E", "F" }.Take(newBedCount).ToList();

                    // 移除多餘床位
                    if (existingBeds.Count > newBedCount)
                    {
                        var bedsToRemove = existingBeds.Skip(newBedCount).ToList();
                        _context.RoomBeds.RemoveRange(bedsToRemove);
                    }
                    // 添加新床位
                    else if (existingBeds.Count < newBedCount)
                    {
                        var existingCodes = existingBeds.Select(b => b.FBedCode).ToList();
                        var newCodes = bedCodes.Except(existingCodes).ToList();
                        foreach (var code in newCodes)
                        {
                            _context.RoomBeds.Add(new RoomBed
                            {
                                FRoomId = room.FRoomId,
                                FBedCode = code,
                                FBedStatus = false
                            });
                        }
                    }
                    room.FBedCount = newBedCount;
                }

                // 處理圖片
                var existingImages = form["ExistingImages"].ToList();
                var imagesToRemove = room.RoomImages.Where(i => !existingImages.Contains(i.ImagePath ?? "")).ToList();
                _context.RoomImages.RemoveRange(imagesToRemove);

                var roomImages = form.Files.GetFiles("RoomImages");
                if (roomImages != null)
                {
                    foreach (var file in roomImages)
                    {
                        if (file.Length > 0)
                        {
                            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/rooms", fileName);
                            using (var stream = new FileStream(path, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }
                            _context.RoomImages.Add(new RoomImage
                            {
                                FRoomId = room.FRoomId,
                                ImagePath = $"images/rooms/{fileName}"
                            });
                        }
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "房間更新成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateRoom 錯誤: {ex.Message}");
                return StatusCode(500, new { message = "更新房間失敗", error = ex.Message });
            }
        }

        // DELETE: api/RoomsErp/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRoom(int id)
        {
            try
            {
                var room = await _context.RoomTables
                    .Include(r => r.RoomBeds)
                    .ThenInclude(b => b.RoomOccupancies)
                    .Include(r => r.RoomImages)
                    .FirstOrDefaultAsync(r => r.FRoomId == id);

                if (room == null)
                    return NotFound(new { message = "房間不存在" });

                // 先刪除相關的 RoomOccupancies
                var occupancies = room.RoomBeds
                    .SelectMany(b => b.RoomOccupancies)
                    .ToList();
                _context.RoomOccupancies.RemoveRange(occupancies);

                // 刪除 RoomBeds
                _context.RoomBeds.RemoveRange(room.RoomBeds);

                // 刪除 RoomImages
                _context.RoomImages.RemoveRange(room.RoomImages);

                // 刪除 RoomTable
                _context.RoomTables.Remove(room);
                await _context.SaveChangesAsync();
                return Ok(new { message = "房間刪除成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DeleteRoom 錯誤: {ex.Message}");
                return StatusCode(500, new { message = "刪除房間失敗", error = ex.Message });
            }
        }

        // PATCH: api/RoomsErp/{roomId}/status
        [HttpPatch("{roomId}/status")]
        public async Task<IActionResult> ToggleRoomStatus(int roomId, [FromBody] string newStatus)
        {
            if (string.IsNullOrEmpty(newStatus) || (newStatus != "active" && newStatus != "vacant"))
            {
                return BadRequest(new { message = "無效的狀態值" });
            }

            try
            {
                var room = await _context.RoomTables.FindAsync(roomId);
                if (room == null)
                {
                    return NotFound(new { message = "房間不存在" });
                }

                room.FRoomStatus = newStatus;
                room.FLastUpdated = DateTime.Now;

                await _context.SaveChangesAsync();
                return Ok(new { message = "狀態更新成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ToggleRoomStatus 錯誤: {ex.Message}");
                return StatusCode(500, new { message = "狀態更新失敗", error = ex.Message });
            }
        }

        // POST: api/RoomsErp/checkout
        [HttpPost("checkout")]
        public async Task<IActionResult> CheckoutOccupancies([FromBody] List<int> occupancyIds)
        {
            if (occupancyIds == null || occupancyIds.Count == 0)
            {
                return BadRequest(new { message = "無有效的入住記錄 ID" });
            }
            try
            {
                using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    foreach (var id in occupancyIds)
                    {
                        var occupancy = await _context.RoomOccupancies
                            .Include(o => o.FBed)
                            .Include(o => o.FMember)
                            .FirstOrDefaultAsync(o => o.FOccupancyId == id);
                        if (occupancy == null || occupancy.FCheckOutDate != null || occupancy.FCheckInDate == null)
                        {
                            continue;
                        }
                        occupancy.FCheckOutDate = DateTime.UtcNow;
                        if (occupancy.FBedId.HasValue)
                        {
                            var bed = await _context.RoomBeds.FindAsync(occupancy.FBedId.Value);
                            if (bed != null)
                            {
                                bed.FBedStatus = false;
                            }
                        }
                        if (occupancy.FMemberId.HasValue)
                        {
                            var member = await _context.Members.FindAsync(occupancy.FMemberId.Value);
                            if (member != null)
                            {
                                member.FResidesInCareHomeStatus = false;
                                // 獨立實現離院通知郵件
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
                                    Subject = "離院通知",
                                    Body = $@"
                                        <h2>離院通知</h2>
                                        <p>親愛的 {member.FName ?? "尊敬的用戶"}，</p>
                                        <p>誠心感謝您選擇入住本院！您的離院手續已於 {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} (UTC) 完成。</p>
                                        <p>請注意隨身物品是否齊全，如有遺漏，請盡速聯繫我們。</p>
                                        <p>感謝您入住本院，您的支持對我們意義重大。我們衷心祝福您未來一切順利，並期待有機會再次為您服務！</p>
                                        <p>此為系統自動發送，請勿直接回覆。如有疑問，請聯繫我們的客服團隊。</p>",
                                    IsBodyHtml = true
                                };
                                mailMessage.To.Add(member.FEmail ?? "no-reply@example.com");

                                try
                                {
                                    smtpClient.Send(mailMessage);
                                    Console.WriteLine($"離院通知郵件已發送至 {member.FEmail ?? "no-reply@example.com"}");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"離院通知郵件發送錯誤: {ex.Message}");
                                    // 這裡選擇僅記錄錯誤，不影響主流程
                                }
                            }
                        }
                    }
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return Ok(new { message = "離院成功" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Checkout 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "離院失敗", error = ex.Message });
            }
        }

        // 1. 獲取繳費紀錄
        // GET: api/RoomsErp/payment-histories
        [HttpGet("payment-histories")]
        [AllowAnonymous] // 允許匿名訪問
        public async Task<IActionResult> GetPaymentHistories()
        {
            try
            {
                // 从 RoomOccupancy 开始，左连接 RoomPaymentHistory 和 Members 获取完整信息
                var occupancyData = await _context.RoomOccupancies
                    .GroupJoin(
                        _context.RoomPaymentHistories,
                        o => o.FOccupancyId,
                        p => p.FOccupancyId,
                        (o, payments) => new { Occupancy = o, Payments = payments.DefaultIfEmpty() }
                    )
                    .SelectMany(x => x.Payments, (o, p) => new
                    {
                        Occupancy = o.Occupancy,
                        Payment = p,
                        Member = _context.Members.FirstOrDefault(m => m.FMemberId == o.Occupancy.FMemberId)
                    })
                    .ToListAsync();

                // 按 FOccupancyId 分组
                var groupedHistories = occupancyData
                    .GroupBy(x => x.Occupancy.FOccupancyId)
                    .Select(group =>
                    {
                        var firstOccupancy = group.First().Occupancy;
                        var payments = group.Select(x => x.Payment).Where(p => p != null).ToList();

                        return new PaymentHistoryDto
                        {
                            PaymentId = payments.Any() ? payments.First().FPaymentId : 0, // 如果没有支付记录，设为 0
                            OccupancyId = group.Key,
                            MemberId = firstOccupancy.FMemberId ?? 0,
                            Name = group.First().Member?.FName ?? "未知",
                            Phone = group.First().Member?.FPhone ?? "無",
                            Email = group.First().Member?.FEmail ?? "無",
                            ResidesInCareHomeStatus = group.First().Member?.FResidesInCareHomeStatus ?? null,
                            // 使用 RoomOccupancy 的 FBillingStatus 作為繳費狀態
                            BillingStatus = firstOccupancy.FBillingStatus ?? false, // 如果為 null，預設為 false
                            BillingAmount = payments.Sum(p => p?.FBillingAmount ?? 0), // 合計所有支付金额
                            BillingDate = payments.Any() ? payments.Max(p => p?.FBillingDate ?? DateTime.MinValue).ToString("yyyy-MM-dd") : "無",
                            PaymentMethod = payments.Any() ? payments.First().FPaymentMethod ?? "未知" : "未知",
                            PaypalOrderId = payments.Any() ? payments.First().FPaypalOrderId ?? "無" : "無",
                            CheckInDate = firstOccupancy.FCheckInDate.HasValue ? firstOccupancy.FCheckInDate.Value.ToString("yyyy-MM-dd") : "無",
                            CheckOutDate = firstOccupancy.FCheckOutDate.HasValue ? firstOccupancy.FCheckOutDate.Value.ToString("yyyy-MM-dd") : "無",
                            // 收集所有支付历史到数组
                            PaymentHistory = payments
                                .Where(p => p != null)
                                .Select(p => new PaymentHistory
                                {
                                    FPaymentId = p.FPaymentId,
                                    FOccupancyId = p.FOccupancyId,
                                    FBillingAmount = p.FBillingAmount,
                                    FBillingDate = p.FBillingDate,
                                    FPaymentMethod = p.FPaymentMethod ?? "未知",
                                    FBillingStatus = p.FBillingStatus,
                                    FPaypalOrderId = p.FPaypalOrderId ?? "無"
                                }).ToArray()
                        };
                    })
                    .ToList();

                // 檢查每個記錄是否逾期，並寄送通知
                foreach (var history in groupedHistories)
                {
                    if (DateTime.TryParse(history.BillingDate, out DateTime billingDate) && history.BillingDate != "無")
                    {
                        DateTime dueDate = billingDate.AddDays(30); // 計算本次繳費時間
                        if (DateTime.UtcNow > dueDate)
                        {
                            // 逾期，寄送通知
                            string subject = "逾期繳費通知";
                            string htmlBody = $@"
<p>親愛的 {history.Name} 先生/女士：</p>
<p>您的入住編號為 {history.OccupancyId} 的帳單已逾期。</p>
<p>本應繳費日期為 {dueDate.ToString("yyyy-MM-dd")}。</p>
<p>請於收到此郵件後盡快完成繳費，以避免影響您的入住權益。我們建議您立即登入系統或聯絡我們處理。</p>
<p>聯絡方式：電話 {history.Phone} 或電子郵件 {history.Email}。</p>
<p>感謝您的配合與理解！</p>
<p>此致<br>Nursing Home 團隊</p>";
                            SendEmail(history.Email, subject, htmlBody);
                        }
                    }
                }

                return Ok(groupedHistories);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetPaymentHistories 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "伺服器錯誤", error = ex.InnerException?.Message });
            }
        }

        // GET: api/RoomsErp/payment-histories/{occupancyId}
        [HttpGet("payment-histories/{occupancyId}")]
        [AllowAnonymous] // 允許匿名訪問
        public async Task<IActionResult> GetPaymentHistoriesByOccupancyId(int occupancyId)
        {
            try
            {
                var paymentDetails = await _context.RoomPaymentHistories
                    .Where(p => p.FOccupancyId == occupancyId)
                    .Select(p => new PaymentHistory
                    {
                        FPaymentId = p.FPaymentId,
                        FOccupancyId = p.FOccupancyId,
                        FBillingAmount = p.FBillingAmount,
                        FBillingDate = p.FBillingDate,
                        FPaymentMethod = p.FPaymentMethod ?? "未知",
                        FBillingStatus = p.FBillingStatus,
                        FPaypalOrderId = p.FPaypalOrderId ?? "無"
                    })
                    .ToListAsync();

                return Ok(paymentDetails);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetPaymentHistoriesByOccupancyId 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "伺服器錯誤", error = ex.InnerException?.Message });
            }
        }

        // 2. 獲取預約參訪列表
        [HttpGet("visit-reservations")]
        public async Task<IActionResult> GetVisitReservations()
        {
            try
            {
                var reservations = await _context.RoomVisitReservations
                    .Select(r => new RoomErpVisitReservationDto
                    {
                        fReservationId = r.FReservationId,
                        fName = r.FName,
                        fEmail = r.FEmail,
                        fPhoneOrLineId = r.FPhoneOrLineId,
                        fReservationDate = r.FReservationDate.ToString("yyyy-MM-dd"),
                        fCreatedAt = r.FCreatedAt.HasValue ? r.FCreatedAt.Value.ToString("yyyy-MM-dd") : "",
                        fStatus = r.FStatus // 確保這是 bool 值，對應資料庫的 0 或 1
                    })
                    .ToListAsync();

                return Ok(reservations);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetVisitReservations 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }
        // 2. 更新預約狀態
        [HttpPut("visit-reservations/{reservationId}/status")]
        public async Task<IActionResult> UpdateVisitStatus(int reservationId, [FromBody] int status)
        {
            try
            {
                var reservation = await _context.RoomVisitReservations.FindAsync(reservationId);
                if (reservation == null)
                {
                    return NotFound(new { message = "預約記錄不存在" });
                }

                // 更新 fStatus 為 0 或 1
                if (status == 0 || status == 1)
                {
                    reservation.FStatus = status == 1; // 轉換 int 到 bool
                    await _context.SaveChangesAsync();
                    return Ok(new { message = "狀態更新成功" });
                }
                else
                {
                    return BadRequest(new { message = "狀態必須為 0 或 1" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateVisitStatus 錯誤: {ex.Message} - StackTrace: {ex.StackTrace} - Request Body: {Newtonsoft.Json.JsonConvert.SerializeObject(status)}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        // 3. 辦理入住
        [HttpPost("occupancy")]
        [Authorize(Policy = "EmployeeOnly")]
        public async Task<IActionResult> CreateOccupancy([FromBody] RoomOccupancyDto dto)
        {
            try
            {
                var member = await _context.Members.FindAsync(dto.FMemberId);
                if (member == null)
                {
                    return NotFound(new { message = "會員不存在" });
                }

                var room = await _context.RoomTables.FindAsync(dto.FRoomId);
                if (room == null)
                {
                    return NotFound(new { message = "房間不存在" });
                }

                var bed = await _context.RoomBeds.FindAsync(dto.FBedId);
                if (bed == null || bed.FBedStatus == true)
                {
                    return BadRequest(new { message = "床位不存在或已被使用" });
                }

                var occupancy = new RoomOccupancy
                {
                    FMemberId = dto.FMemberId,
                    FBedId = dto.FBedId,
                    FCheckInDate = dto.FCheckInDate,
                    FCheckOutDate = null,
                    FBillingStatus = true // 支付成功
                };

                _context.RoomOccupancies.Add(occupancy);
                await _context.SaveChangesAsync();

                var payment = new RoomPaymentHistory
                {
                    FOccupancyId = occupancy.FOccupancyId,
                    FBillingAmount = dto.FBillingAmount,
                    FBillingDate = DateTime.UtcNow,
                    FPaymentMethod = dto.FPaymentMethod,
                    FBillingStatus = true,
                    FPaypalOrderId = dto.FPaypalOrderId
                };

                _context.RoomPaymentHistories.Add(payment);
                bed.FBedStatus = true; // 更新床位狀態
                member.FResidesInCareHomeStatus = true; // 更新會員入住狀態
                await _context.SaveChangesAsync();

                return Ok(new { message = "入住辦理成功", occupancyId = occupancy.FOccupancyId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateOccupancy 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        // 獲取會員列表
        [HttpGet("members")]
        public async Task<IActionResult> GetMembers()
        {
            var members = await _context.Members
                .Where(m => m.FResidesInCareHomeStatus == false) // 只顯示未入住會員
                .Select(m => new { m.FMemberId, m.FName })
                .ToListAsync();
            return Ok(members);
        }

        // 獲取床位列表
        [HttpGet("rooms/{roomId}/beds")]
        public async Task<IActionResult> GetBeds(int roomId)
        {
            var beds = await _context.RoomBeds
                .Where(b => b.FRoomId == roomId && b.FBedStatus == false) // 只顯示未使用床位
                .Select(b => new { b.FBedId, b.FBedCode })
                .ToListAsync();
            return Ok(beds);
        }

        // 批量更新預約日期
        [HttpPut("visit-reservations/batch-update-date")]
        public async Task<IActionResult> BatchUpdateReservationDate([FromBody] BatchUpdateDateDto dto)
        {
            try
            {
                if (dto.ReservationIds == null || dto.ReservationIds.Count == 0)
                {
                    return BadRequest(new { message = "至少選擇一個預約 ID" });
                }

                var reservations = await _context.RoomVisitReservations
                    .Where(r => dto.ReservationIds.Contains(r.FReservationId))
                    .ToListAsync();

                if (reservations.Count == 0)
                {
                    return NotFound(new { message = "未找到匹配的預約記錄" });
                }

                // 收集原本的預約日期
                var originalDates = reservations.ToDictionary(r => r.FReservationId, r => r.FReservationDate);

                foreach (var reservation in reservations)
                {
                    reservation.FReservationDate = dto.NewDate;
                }

                await _context.SaveChangesAsync();

                // 發送郵件通知
                foreach (var reservation in reservations)
                {
                    var originalDate = originalDates[reservation.FReservationId];
                    var htmlBody = $@"
                        <h2>預約日期更新通知</h2>
                        <p>親愛的 {reservation.FName}，</p>
                        <p>您的預約日期被更改</p>
                        <p>原本預約的時間: {originalDate:yyyy-MM-dd}</p>
                        <p>更改後預約時間: {dto.NewDate:yyyy-MM-dd}</p>
                        <p>預約ID: <strong>{reservation.FReservationId}</strong></p>
                        <p>造成您的困擾敬請見諒！如有任何問題，請聯繫客服:09-8888-8888</p>
                        <p>此為系統自動發送，請勿直接回覆。</p>
                        <p>更新時間: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} (UTC)</p>";
                    SendEmail(reservation.FEmail, "預約日期更新通知", htmlBody);
                }

                return Ok(new { message = "批量更新日期成功" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"BatchUpdateReservationDate DbUpdateError: {ex.InnerException?.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "資料庫更新錯誤", error = ex.InnerException?.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"BatchUpdateReservationDate 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }
        // 批量刪除預約
        [HttpDelete("visit-reservations/batch-delete")]
        public async Task<IActionResult> BatchDeleteReservations([FromBody] BatchDeleteDto dto)
        {
            try
            {
                if (dto.ReservationIds == null || dto.ReservationIds.Count == 0)
                {
                    return BadRequest(new { message = "至少選擇一個預約 ID" });
                }

                var reservations = await _context.RoomVisitReservations
                    .Where(r => dto.ReservationIds.Contains(r.FReservationId))
                    .ToListAsync();

                if (reservations.Count == 0)
                {
                    return NotFound(new { message = "未找到匹配的預約記錄" });
                }

                _context.RoomVisitReservations.RemoveRange(reservations);
                await _context.SaveChangesAsync();

                return Ok(new { message = "批量刪除成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"BatchDeleteReservations 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }
        //預約參訪-聯絡狀態
        [HttpPut("visit-reservations/batch-update-contact")]
        public async Task<IActionResult> BatchUpdateContactStatus([FromBody] BatchUpdateContactDto dto)
        {
            try
            {
                // 檢查輸入參數
                if (dto.ReservationIds == null || dto.ReservationIds.Count == 0)
                {
                    return BadRequest(new { message = "至少選擇一個預約 ID" });
                }

                // 查詢匹配的預約記錄
                var reservations = await _context.RoomVisitReservations
                    .Where(r => dto.ReservationIds.Contains(r.FReservationId))
                    .ToListAsync();

                // 檢查是否有匹配的記錄
                if (reservations.Count == 0)
                {
                    return NotFound(new { message = "未找到匹配的預約記錄" });
                }

                // 更新狀態
                foreach (var reservation in reservations)
                {
                    reservation.FStatus = dto.NewStatus; // true: 已聯絡, false: 未聯絡
                }

                // 儲存更改到資料庫
                await _context.SaveChangesAsync();

                // 回傳成功訊息
                return Ok(new { message = "批量聯絡狀態更新成功" });
            }
            catch (DbUpdateException ex)
            {
                // 處理資料庫更新異常
                Console.WriteLine($"BatchUpdateContactStatus DbUpdateError: {ex.InnerException?.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "資料庫更新錯誤", error = ex.InnerException?.Message });
            }
            catch (Exception ex)
            {
                // 處理其他異常
                Console.WriteLine($"BatchUpdateContactStatus 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }
        // 郵件 SMTP 設定
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
