using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using System.Security.Claims;

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

        // GET: api/MemberMyRoom/room
        [HttpGet("room")]
        [Authorize]
        public async Task<IActionResult> GetMemberRoom()
        {
            try
            {
                var account = User.FindFirstValue(ClaimTypes.Name);
                var member = await _context.Members.FirstOrDefaultAsync(m => m.FAccount == account);
                if (member == null)
                {
                    return NotFound(new { message = "會員不存在" });
                }

                var occupancy = await _context.RoomOccupancies
                    .Include(o => o.FBed)
                    .ThenInclude(b => b.FRoom)
                    .ThenInclude(r => r.RoomImages)
                    .FirstOrDefaultAsync(o => o.FMemberId == member.FMemberId && o.FCheckOutDate == null);

                if (occupancy == null)
                {
                    return NotFound(new { message = "會員無當前房間資料" });
                }

                var response = new
                {
                    member = new { fName = member.FName, fIdNumber = member.FIdNumber },
                    roomTable = new
                    {
                        fRoomName = occupancy.FBed.FRoom.FRoomName,
                        fRoomAlias = occupancy.FBed.FRoom.FRoomAlias,
                        fRoomType = occupancy.FBed.FRoom.FRoomType,
                        fRoomPrice = occupancy.FBed.FRoom.FRoomPrice,
                        images = occupancy.FBed.FRoom.RoomImages.Select(i => i.ImagePath).ToArray()
                    },
                    roomBed = new { fBedCode = occupancy.FBed.FBedCode },
                    roomOccupancy = new { fBillingStatus = occupancy.FBillingStatus }
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