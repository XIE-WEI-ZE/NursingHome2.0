using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MemberMyRoomController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public MemberMyRoomController(DbNursingHomeContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        // 查詢會員房間資料（需 JWT Token，無需參數）
        [HttpGet("room")]
        [Authorize(Policy = "MemberOnly")] // 使用 MemberOnly 策略，確保 JWT 認證
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
                    .Select(o => new
                    {
                        FOccupancyId = o.FOccupancyId,
                        FBedId = o.FBedId,
                        FBillingStatus = o.FBillingStatus,
                        Bed = _context.RoomBeds
                            .Where(b => b.FBedId == o.FBedId)
                            .Select(b => new
                            {
                                FBedCode = b.FBedCode,
                                FRoomId = b.FRoomId,
                                Room = _context.RoomTables
                                    .Where(r => r.FRoomId == b.FRoomId)
                                    .Select(r => new
                                    {
                                        FRoomName = r.FRoomName,
                                        FRoomAlias = r.FRoomAlias,
                                        FRoomType = r.FRoomType,
                                        FRoomPrice = r.FRoomPrice,
                                        Images = _context.RoomImages
                                            .Where(i => i.FRoomId == r.FRoomId)
                                            .Select(i => i.ImagePath)
                                            .ToArray()
                                    })
                                    .FirstOrDefault()
                            })
                            .FirstOrDefault()
                    })
                    .FirstOrDefaultAsync();

                if (occupancy == null)
                {
                    return NotFound(new { message = "會員無當前房間資料" });
                }

                if (occupancy.Bed == null || occupancy.Bed.Room == null)
                {
                    return BadRequest(new { message = "房間或床位資料無效" });
                }

                var roomTable = occupancy.Bed.Room;
                var response = new
                {
                    member = new { fName = member.FName ?? "未提供", fIdNumber = member.FIdNumber ?? "未提供" },
                    roomTable = new
                    {
                        fRoomName = roomTable.FRoomName ?? "未分配",
                        fRoomAlias = roomTable.FRoomAlias ?? "未命名",
                        fRoomType = roomTable.FRoomType ?? false,
                        fRoomPrice = roomTable.FRoomPrice,
                        images = roomTable.Images ?? new string[0]
                    },
                    roomBed = new { fBedCode = occupancy.Bed.FBedCode ?? "未分配" },
                    roomOccupancy = new { fBillingStatus = occupancy.FBillingStatus ?? false }
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetMemberRoom 錯誤: {ex.Message} - StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "內部伺服器錯誤", error = ex.Message });
            }
        }
    }
}