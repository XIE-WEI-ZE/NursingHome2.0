// prjFinalProjectApi/Controllers/RoomsErpController.cs
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
using Microsoft.EntityFrameworkCore; // 確保導入 EF Core

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    //[Authorize] // 限制為登入用戶或後台角色
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
        // prjFinalProjectApi/Controllers/RoomsErpController.cs
        [HttpGet]
        public async Task<IActionResult> GetRooms()
        {
            try
            {
                var rooms = await _context.RoomTables
                    .Include(r => r.RoomBeds)
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
                        isAvailable = r.RoomBeds.Any(b => b.FBedStatus ?? false),
                        availableBeds = r.RoomBeds.Count(b => b.FBedStatus ?? false),
                        image = r.RoomImages.Where(i => !string.IsNullOrEmpty(i.ImagePath)).Select(i => i.ImagePath).FirstOrDefault() ?? "rooms/default-room-image.jpg",
                        fRoomStatus = r.FRoomStatus ?? "active",
                        fRoomType = r.FRoomType ?? false,
                        // 臨時移除 LastUpdated，直到資料庫同步
                        lastUpdated = r.LastUpdated ?? DateTime.UtcNow
                    })
                    .ToListAsync();

                var formattedRooms = rooms.Select(r => new
                {
                    r.fRoomId,
                    r.fRoomName,
                    r.fRoomAlias,
                    r.images,
                    r.fRoomDescription,
                    r.fRoomPrice,
                    r.fBedCount,
                    r.isAvailable,
                    r.availableBeds,
                    r.image,
                    r.fRoomStatus,
                    r.fRoomType,
                    // 臨時使用當前時間
                    lastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }).ToList();

                return Ok(new { message = "Success", data = formattedRooms });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetRooms 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        // POST: api/RoomsErp
        [HttpPost]
        public async Task<IActionResult> CreateRoom([FromForm] UpdateRoomERPDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var room = new RoomTable
                {
                    FRoomAlias = dto.FRoomAlias ?? "",
                    FRoomDescription = dto.FRoomDescription ?? "",
                    FRoomPrice = dto.FRoomPrice,
                    FRoomType = dto.FRoomType,
                    FRoomStatus = dto.FRoomStatus,
                    LastUpdated = DateTime.UtcNow
                };

                _context.RoomTables.Add(room);
                await _context.SaveChangesAsync();

                for (int i = 1; i <= dto.FBedCount; i++)
                {
                    var bed = new RoomBed
                    {
                        FRoomId = room.FRoomId,
                        FBedCode = $"Bed-{i}",
                        FBedStatus = true
                    };
                    _context.RoomBeds.Add(bed);
                }

                if (dto.RoomImage != null)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/rooms");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var uniqueFileName = Guid.NewGuid().ToString() + "_" + dto.RoomImage.FileName;
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.RoomImage.CopyToAsync(stream);
                    }

                    var roomImage = new RoomImage
                    {
                        FRoomId = room.FRoomId,
                        ImagePath = $"/images/rooms/{uniqueFileName}"
                    };
                    _context.RoomImages.Add(roomImage);
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = "房間新增成功", roomId = room.FRoomId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateRoom 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        // PUT: api/RoomsErp/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRoom(int id, [FromForm] UpdateRoomERPDto dto)
        {
            try
            {
                if (id != dto.FRoomId)
                {
                    return BadRequest(new { message = "ID 不匹配" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var room = await _context.RoomTables
                    .Include(r => r.RoomBeds)
                    .Include(r => r.RoomImages)
                    .FirstOrDefaultAsync(r => r.FRoomId == id);

                if (room == null)
                {
                    return NotFound(new { message = "房間不存在" });
                }

                room.FRoomAlias = dto.FRoomAlias ?? "";
                room.FRoomDescription = dto.FRoomDescription ?? "";
                room.FRoomPrice = dto.FRoomPrice;
                room.FRoomType = dto.FRoomType;
                room.FRoomStatus = dto.FRoomStatus;
                room.LastUpdated = DateTime.UtcNow;

                int currentBedCount = room.RoomBeds.Count();
                if (dto.FBedCount != currentBedCount)
                {
                    if (dto.FBedCount > currentBedCount)
                    {
                        for (int i = currentBedCount + 1; i <= dto.FBedCount; i++)
                        {
                            var bed = new RoomBed
                            {
                                FRoomId = room.FRoomId,
                                FBedCode = $"Bed-{i}",
                                FBedStatus = true
                            };
                            _context.RoomBeds.Add(bed);
                        }
                    }
                    else
                    {
                        var bedsToRemove = room.RoomBeds.Skip(dto.FBedCount).ToList();
                        _context.RoomBeds.RemoveRange(bedsToRemove);
                    }
                }

                if (dto.RoomImage != null)
                {
                    var existingImage = room.RoomImages.FirstOrDefault();
                    if (existingImage != null)
                    {
                        var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", existingImage.ImagePath.TrimStart('/'));
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            System.IO.File.Delete(oldFilePath);
                        }
                        _context.RoomImages.Remove(existingImage);
                    }

                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/rooms");
                    var uniqueFileName = Guid.NewGuid().ToString() + "_" + dto.RoomImage.FileName;
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.RoomImage.CopyToAsync(stream);
                    }

                    var newImage = new RoomImage
                    {
                        FRoomId = room.FRoomId,
                        ImagePath = $"/images/rooms/{uniqueFileName}"
                    };
                    _context.RoomImages.Add(newImage);
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = "房間更新成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateRoom 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }

        // PATCH: api/RoomsErp/{id}/status
        [HttpPatch("{id}/status")]
        public async Task<IActionResult> ToggleRoomStatus(int id, [FromBody] string? status)
        {
            try
            {
                if (string.IsNullOrEmpty(status))
                {
                    return BadRequest(new { message = "狀態不可為空" });
                }

                var room = await _context.RoomTables.FindAsync(id);
                if (room == null)
                {
                    return NotFound(new { message = "房間不存在" });
                }

                room.FRoomStatus = status;
                room.LastUpdated = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { message = "狀態更新成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ToggleRoomStatus 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
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
                    .Include(r => r.RoomImages)
                    .FirstOrDefaultAsync(r => r.FRoomId == id);

                if (room == null)
                {
                    return NotFound(new { message = "房間不存在" });
                }

                foreach (var image in room.RoomImages)
                {
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", image.ImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                _context.RoomImages.RemoveRange(room.RoomImages);
                _context.RoomBeds.RemoveRange(room.RoomBeds);
                _context.RoomTables.Remove(room);

                await _context.SaveChangesAsync();

                return Ok(new { message = "房間刪除成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DeleteRoom 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }
    }
}