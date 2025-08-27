using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;

namespace prjFinalProjectApi.Controllers
{
    public class FriendDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class CommunityFriendController : Controller
    {
        private readonly DbNursingHomeContext _context;

        public CommunityFriendController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // 送出好友邀請
        [HttpPost("request/{receiverId}")]
        public async Task<IActionResult> SendRequest(int receiverId)
        {
            int requesterId = GetCurrentMemberId();
            if (requesterId == 0) return Unauthorized();
            if (requesterId == receiverId) return BadRequest(new { message = "不能加自己為好友" });

            bool alreadyFriend = await _context.CommunityFriends
                .AnyAsync(f => f.MemberID1 == requesterId && f.MemberID2 == receiverId);
            if (alreadyFriend) return BadRequest(new { message = "已經是好友了" });

            var existing = await _context.CommunityFriendRequests
                .FirstOrDefaultAsync(r =>
                    r.RequesterID == requesterId &&
                    r.ReceiverID == receiverId &&
                    r.RequestStatus == "Pending");
            if (existing != null) return BadRequest(new { message = "已經送出過邀請" });

            var request = new CommunityFriendRequest
            {
                RequesterID = requesterId,
                ReceiverID = receiverId,
                SentAt = DateTime.Now,
                RequestStatus = "Pending"
            };
            _context.CommunityFriendRequests.Add(request);
            await _context.SaveChangesAsync();

            return Ok(new { message = "好友邀請已送出" });
        }

        // 回覆好友邀請 (接受 / 拒絕)
        [HttpPost("request/respond")]
        public async Task<IActionResult> RespondRequest([FromBody] FriendRequestRespondDto dto)
        {
            var request = await _context.CommunityFriendRequests.FindAsync(dto.RequestID);
            if (request == null) return NotFound();

            if (dto.Action == "Accepted")
            {
                request.RequestStatus = "Accepted";

                _context.CommunityFriends.AddRange(
                    new CommunityFriend { MemberID1 = request.RequesterID, MemberID2 = request.ReceiverID, CreatedAt = DateTime.Now },
                    new CommunityFriend { MemberID1 = request.ReceiverID, MemberID2 = request.RequesterID, CreatedAt = DateTime.Now }
                );

                var room = new CommunityChatRoom
                {
                    RoomName = $"Private_{request.RequesterID}_{request.ReceiverID}",
                    RoomType = "Private",
                    CreatorMemberId = request.RequesterID,
                    CreatedAt = DateTime.Now,
                    RoomStatus = "Active"
                };
                _context.CommunityChatRooms.Add(room);
                await _context.SaveChangesAsync();

                _context.CommunityChatRoomMembers.AddRange(
                    new CommunityChatRoomMember { RoomId = room.RoomId, MemberId = request.RequesterID, JoinedAt = DateTime.Now },
                    new CommunityChatRoomMember { RoomId = room.RoomId, MemberId = request.ReceiverID, JoinedAt = DateTime.Now }
                );
            }
            else if (dto.Action == "Rejected")
            {
                request.RequestStatus = "Rejected";
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = $"好友邀請已{dto.Action}" });
        }

        // 取得好友列表
        [HttpGet("list/{memberId}")]
        public async Task<IActionResult> GetFriends(int memberId)
        {
            var friends = await _context.CommunityFriends
                .Where(f => f.MemberID1 == memberId)
                .Join(_context.Members, f => f.MemberID2, m => m.FMemberId,
                      (f, m) => new FriendDto { Id = m.FMemberId, Name = m.FName })
                .ToListAsync();
            return Ok(friends);
        }

        // 取得好友邀請
        [HttpGet("requests")]
        public async Task<IActionResult> GetFriendRequests()
        {
            int memberId = GetCurrentMemberId();
            if (memberId == 0) return Unauthorized();

            var requests = await _context.CommunityFriendRequests
                .Where(r => r.ReceiverID == memberId && r.RequestStatus == "Pending")
                .ToListAsync();

            return Ok(requests);
        }

        private int GetCurrentMemberId()
        {
            var claim = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "MemberId");
            if (claim != null && int.TryParse(claim.Value, out int memberId))
                return memberId;
            return 0;
        }
    }
}
