using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
        [HttpGet("payment-history")]
        [Authorize(Policy = "EmployeeOnly")] // 假設後台需員工權限
        public async Task<IActionResult> GetPaymentHistory([FromQuery] int? memberId, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            var query = _context.RoomPaymentHistories
         .Include(p => p.FOccupancy)
         .ThenInclude(o => o.FMember)
         .Include(p => p.RoomPaymentReceipts)  // 現在能存取，因為模型有定義
         .AsQueryable();

            if (memberId.HasValue)
                query = query.Where(p => p.FOccupancy.FMemberId == memberId);
            if (startDate.HasValue)
                query = query.Where(p => p.FBillingDate >= startDate);
            if (endDate.HasValue)
                query = query.Where(p => p.FBillingDate <= endDate);

            var payments = await query
          .OrderByDescending(p => p.FBillingDate)
          .Select(p => new
          {
              p.FPaymentId,
              p.FOccupancyId,
              p.FBillingAmount,
              p.FBillingDate,
              p.FPaymentMethod,
              Receipt = p.RoomPaymentReceipts.Select(r => new  // 假設多張，取所有或 FirstOrDefault
              {
                  r.FReceiptId,
                  r.FReceiptNumber,
                  r.FReceiptDate,
                  r.FReceiptFilePath
              }).FirstOrDefault()  // 如果通常只有一張，用 FirstOrDefault；若需所有，改為 .ToList()
          })
          .ToListAsync();

            return Ok(new { message = "成功獲取繳費記錄", data = payments });
        }
        // 1. 獲取繳費紀錄
        [HttpGet("payment-histories")]
        public async Task<IActionResult> GetPaymentHistory()
        {
            try
            {
                var query = _context.RoomOccupancies
                    .Include(o => o.FMember)
                    .Include(o => o.RoomPaymentHistories)
                    .GroupJoin(_context.RoomPaymentHistories, // 使用 GroupJoin 處理多筆支付記錄
                        o => o.FOccupancyId,
                        p => p.FOccupancyId,
                        (o, payments) => new { Occupancy = o, Payments = payments })
                    .SelectMany(x => x.Payments.DefaultIfEmpty(),
                        (o, p) => new { Occupancy = o.Occupancy, Payment = p })
                    .GroupBy(x => new { x.Occupancy.FOccupancyId, x.Occupancy.FMemberId, x.Occupancy.FMember.FName, Occupancy = x.Occupancy }) // 包含 Occupancy
                    .Select(g => new
                    {
                        Key = g.Key,
                        MaxBillingAmount = g.Select(x => x.Payment == null ? 0 : x.Payment.FBillingAmount).DefaultIfEmpty(0).Max(),
                        MaxBillingDate = g.Select(x => x.Payment == null ? (DateTime?)null : x.Payment.FBillingDate).Max()
                    });

                var intermediateResult = await query.ToListAsync(); // 先執行資料庫查詢

                var paymentHistories = intermediateResult
                    .Select(g => new PaymentHistoryDto
                    {
                        MemberId = g.Key.FMemberId ?? 0,
                        Name = g.Key.FName ?? "未知",
                        BillingAmount = g.MaxBillingAmount,
                        BillingDate = g.MaxBillingDate?.ToString("yyyy-MM-dd") ?? "無",
                        PaymentHistory = g.Key.Occupancy.RoomPaymentHistories.Select(p => new PaymentHistory
                        {
                            FPaymentId = p.FPaymentId,
                            FOccupancyId = p.FOccupancyId,
                            FBillingAmount = p.FBillingAmount,
                            FBillingDate = p.FBillingDate,
                            FPaymentMethod = p.FPaymentMethod ?? "未知",
                            FBillingStatus = p.FBillingStatus,
                            FPaypalOrderId = p.FPaypalOrderId ?? "無"
                        }).ToArray()
                    })
                    .ToList();

                return Ok(paymentHistories);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetPaymentHistory 錯誤: {ex.Message} - StackTrace: {ex.StackTrace} - InnerException: {ex.InnerException?.Message}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message, innerError = ex.InnerException?.Message });
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
    }
}