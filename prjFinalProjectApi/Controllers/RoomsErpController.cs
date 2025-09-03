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
                        fRoomStatus = r.FRoomStatus.Trim(),
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
                                            fCheckInDate = o.FCheckInDate != null ? o.FCheckInDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : null
                                        }).ToList()
                    })
                    .ToListAsync();

                return Ok(new { message = "成功獲取房間列表", data = rooms });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetRooms 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        // POST: api/RoomsErp - 新增房間
        [HttpPost]
        public async Task<IActionResult> CreateRoom([FromForm] UpdateRoomERPDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "輸入數據無效", errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
            }

            try
            {
                var room = new RoomTable
                {
                    FRoomName = dto.FRoomName,
                    FRoomAlias = dto.FRoomAlias,
                    FRoomDescription = dto.FRoomDescription,
                    FRoomPrice = dto.FRoomPrice,
                    FBedCount = dto.FBedCount,
                    FRoomType = dto.FRoomType,
                    FRoomStatus = dto.FRoomStatus,
                    FLastUpdated = DateTime.Now
                };

                _context.RoomTables.Add(room);
                await _context.SaveChangesAsync();

                // 處理圖片
                if (dto.RoomImages != null && dto.RoomImages.Length > 0)
                {
                    foreach (var image in dto.RoomImages)
                    {
                        var fileName = $"{Guid.NewGuid()}_{image.FileName}";
                        var filePath = Path.Combine("wwwroot/images/rooms", fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await image.CopyToAsync(stream);
                        }

                        _context.RoomImages.Add(new RoomImage
                        {
                            FRoomId = room.FRoomId,
                            ImagePath = $"rooms/{fileName}"
                        });
                    }
                    await _context.SaveChangesAsync();
                }

                return Ok(new { message = "房間新增成功", roomId = room.FRoomId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateRoom 錯誤: {ex.Message}");
                return StatusCode(500, new { message = "新增房間失敗", error = ex.Message });
            }
        }

        // PUT: api/RoomsErp/{roomId} - 更新房間
        [HttpPut("{roomId}")]
        public async Task<IActionResult> UpdateRoom(int roomId, [FromForm] UpdateRoomERPDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "輸入數據無效", errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
            }

            try
            {
                var room = await _context.RoomTables.FindAsync(roomId);
                if (room == null)
                {
                    return NotFound(new { message = "房間不存在" });
                }

                room.FRoomName = dto.FRoomName;
                room.FRoomAlias = dto.FRoomAlias;
                room.FRoomDescription = dto.FRoomDescription;
                room.FRoomPrice = dto.FRoomPrice;
                room.FBedCount = dto.FBedCount;
                room.FRoomType = dto.FRoomType;
                room.FRoomStatus = dto.FRoomStatus;
                room.FLastUpdated = DateTime.Now;

                // 處理新圖片
                if (dto.RoomImages != null && dto.RoomImages.Length > 0)
                {
                    foreach (var image in dto.RoomImages)
                    {
                        var fileName = $"{Guid.NewGuid()}_{image.FileName}";
                        var filePath = Path.Combine("wwwroot/images/rooms", fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await image.CopyToAsync(stream);
                        }

                        _context.RoomImages.Add(new RoomImage
                        {
                            FRoomId = room.FRoomId,
                            ImagePath = $"rooms/{fileName}"
                        });
                    }
                }

                // 僅在明確傳入 existingImages 且包含有效圖片路徑時處理刪除
                if (dto.ExistingImages != null && dto.ExistingImages.Any(img => !string.IsNullOrEmpty(img)))
                {
                    Console.WriteLine($"Received existingImages: {string.Join(", ", dto.ExistingImages)}");
                    var existingImages = await _context.RoomImages.Where(i => i.FRoomId == roomId).ToListAsync();
                    foreach (var img in existingImages)
                    {
                        if (!dto.ExistingImages.Contains(img.ImagePath))
                        {
                            Console.WriteLine($"Deleting image: {img.ImagePath}");
                            _context.RoomImages.Remove(img);
                            var filePath = Path.Combine("wwwroot", img.ImagePath ?? "");
                            if (System.IO.File.Exists(filePath))
                            {
                                System.IO.File.Delete(filePath);
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No valid existingImages provided or empty, skipping image deletion");
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "房間更新成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateRoom 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "更新房間失敗", error = ex.Message });
            }
        }

        // DELETE: api/RoomsErp/{roomId} - 刪除房間
        [HttpDelete("{roomId}")]
        public async Task<IActionResult> DeleteRoom(int roomId)
        {
            try
            {
                var room = await _context.RoomTables.FindAsync(roomId);
                if (room == null)
                {
                    return NotFound(new { message = "房間不存在" });
                }

                // 刪除相關圖片
                var images = await _context.RoomImages.Where(i => i.FRoomId == roomId).ToListAsync();
                foreach (var img in images)
                {
                    var filePath = Path.Combine("wwwroot", img.ImagePath ?? "");
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }
                _context.RoomImages.RemoveRange(images);

                // 刪除相關床位
                var beds = await _context.RoomBeds.Where(b => b.FRoomId == roomId).ToListAsync();
                _context.RoomBeds.RemoveRange(beds);

                // 刪除相關入住記錄
                var occupancies = await _context.RoomOccupancies.Where(o => o.FBedId.HasValue && beds.Select(b => b.FBedId).Contains(o.FBedId.Value)).ToListAsync();
                _context.RoomOccupancies.RemoveRange(occupancies);

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

        // PATCH: api/RoomsErp/{roomId}/status - 上/下架
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

        // POST: api/RoomsErp/checkout - 離院
        [HttpPost("checkout")]
        public async Task<IActionResult> CheckoutOccupancies([FromBody] List<int> occupancyIds)
        {
            if (occupancyIds == null || occupancyIds.Count == 0)
            {
                return BadRequest(new { message = "無有效的入住記錄 ID" });
            }

            try
            {
                foreach (var id in occupancyIds)
                {
                    var occupancy = await _context.RoomOccupancies.FindAsync(id);
                    if (occupancy != null && occupancy.FCheckOutDate == null)
                    {
                        occupancy.FCheckOutDate = DateTime.Now;
                        occupancy.FBedId = null;

                        if (occupancy.FBedId.HasValue)
                        {
                            var bed = await _context.RoomBeds.FindAsync(occupancy.FBedId.Value);
                            if (bed != null)
                            {
                                bed.FBedStatus = false;
                            }
                        }
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "離院成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Checkout 錯誤: {ex.Message}");
                return StatusCode(500, new { message = "離院失敗", error = ex.Message });
            }
        }
    }
}