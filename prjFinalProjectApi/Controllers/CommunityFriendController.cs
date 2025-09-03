using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Security.Claims;

namespace prjFinalProjectApi.Controllers
{
    // DTO 類別
    public class FriendDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
        public string? Bio { get; set; }
    }

    public class FriendRequestDto
    {
        public int RequestID { get; set; }
        public int RequesterID { get; set; }
        public string? RequesterName { get; set; }
        public int ReceiverID { get; set; }
        public string SentAt { get; set; } = string.Empty;
        public string RequestStatus { get; set; } = string.Empty;
    }

    public class UserSearchResultDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
        public string? Bio { get; set; }
    }

    public class FriendRequestRespondDto
    {
        public int RequestID { get; set; }
        public string Action { get; set; } = string.Empty; // "Accepted" 或 "Rejected"
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "MemberOnly")] // 確保需要認證
    public class CommunityFriendController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public CommunityFriendController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // 取得當前用戶 ID（修正 JWT claim 解析）
        private int GetCurrentMemberId()
        {
            // 嘗試多種可能的 claim 名稱
            var claim = HttpContext.User.Claims.FirstOrDefault(c =>
                c.Type == "MemberID" ||
                c.Type == "MemberId" ||
                c.Type == ClaimTypes.NameIdentifier ||
                c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");

            if (claim != null && int.TryParse(claim.Value, out int memberId))
                return memberId;
            return 0;
        }

        // 發送好友請求
        [HttpPost("request/{receiverId}")]
        public async Task<IActionResult> SendFriendRequest(int receiverId)
        {
            int requesterId = GetCurrentMemberId();
            if (requesterId == 0) return Unauthorized(new { message = "未授權的請求" });
            if (requesterId == receiverId) return BadRequest(new { message = "不能加自己為好友" });

            // 檢查是否已經是好友
            bool alreadyFriend = await _context.CommunityFriends
                .AnyAsync(f =>
                    (f.MemberID1 == requesterId && f.MemberID2 == receiverId) ||
                    (f.MemberID1 == receiverId && f.MemberID2 == requesterId));
            if (alreadyFriend) return BadRequest(new { message = "已經是好友了" });

            // 檢查是否已經有待處理的請求
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

        // 回應好友請求
        [HttpPost("request/respond")]
        public async Task<IActionResult> RespondFriendRequest([FromBody] FriendRequestRespondDto dto)
        {
            int currentUserId = GetCurrentMemberId();
            if (currentUserId == 0) return Unauthorized();

            var request = await _context.CommunityFriendRequests
                .FirstOrDefaultAsync(r => r.RequestID == dto.RequestID && r.ReceiverID == currentUserId);
            if (request == null) return NotFound(new { message = "找不到好友請求" });

            if (dto.Action == "Accepted")
            {
                request.RequestStatus = "Accepted";

                // 建立雙向好友關係
                _context.CommunityFriends.AddRange(
                    new CommunityFriend { MemberID1 = request.RequesterID, MemberID2 = request.ReceiverID, CreatedAt = DateTime.Now },
                    new CommunityFriend { MemberID1 = request.ReceiverID, MemberID2 = request.RequesterID, CreatedAt = DateTime.Now }
                );

                // 自動建立私人聊天室
                var room = new CommunityChatRoom
                {
                    RoomName = null, // 私人聊天室不需要名稱
                    RoomType = "Private",
                    CreatorMemberId = request.RequesterID,
                    CreatedAt = DateTime.Now,
                    RoomStatus = "啟用"
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
                .Join(_context.Members,
                    f => f.MemberID2,
                    m => m.FMemberId,
                    (f, m) => new FriendDto
                    {
                        Id = m.FMemberId,
                        Name = m.FName ?? "未知用戶",
                        PhotoUrl = m.FProfilePictureUrl, // 使用正確的屬性名稱
                        Bio = null // 因為資料庫沒有這個欄位，設為 null
                    })
                .ToListAsync();
            return Ok(friends);
        }

        // 取得好友請求列表
        [HttpGet("requests")]
        public async Task<IActionResult> GetFriendRequests()
        {
            int memberId = GetCurrentMemberId();
            if (memberId == 0) return Unauthorized();

            var requests = await _context.CommunityFriendRequests
                .Where(r => r.ReceiverID == memberId && r.RequestStatus == "Pending")
                .Join(_context.Members,
                    r => r.RequesterID,
                    m => m.FMemberId,
                    (r, m) => new FriendRequestDto
                    {
                        RequestID = r.RequestID,
                        RequesterID = r.RequesterID,
                        RequesterName = m.FName ?? "未知用戶", // 使用正確的屬性名稱
                        ReceiverID = r.ReceiverID,
                        SentAt = r.SentAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        RequestStatus = r.RequestStatus
                    })
                .ToListAsync();

            return Ok(requests);
        }

        // 搜尋用戶
        [HttpGet("search")]
        public async Task<IActionResult> SearchUsers([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest(new { message = "查詢不能為空" });

            int currentUserId = GetCurrentMemberId();

            var users = await _context.Members
                .Where(m => m.FName != null && m.FName.Contains(query) && m.FMemberId != currentUserId)
                .Select(m => new UserSearchResultDto
                {
                    Id = m.FMemberId,
                    Name = m.FName ?? "未知用戶",
                    PhotoUrl = m.FProfilePictureUrl, // 使用正確的屬性名稱
                    Bio = null // 因為資料庫沒有這個欄位，設為 null
                })
                .Take(10)
                .ToListAsync();

            return Ok(users);
        }

        // 移除好友
        [HttpDelete("friend/{friendId}")]
        public async Task<IActionResult> RemoveFriend(int friendId)
        {
            int currentUserId = GetCurrentMemberId();
            if (currentUserId == 0) return Unauthorized();

            // 移除雙向好友關係
            var friendships = await _context.CommunityFriends
                .Where(f =>
                    (f.MemberID1 == currentUserId && f.MemberID2 == friendId) ||
                    (f.MemberID1 == friendId && f.MemberID2 == currentUserId))
                .ToListAsync();

            if (!friendships.Any())
                return NotFound(new { message = "找不到好友關係" });

            _context.CommunityFriends.RemoveRange(friendships);
            await _context.SaveChangesAsync();

            return Ok(new { message = "已移除好友" });
        }

        // 檢查是否為好友
        [HttpGet("is-friend/{memberId}/{friendId}")]
        public async Task<IActionResult> IsFriend(int memberId, int friendId)
        {
            var friendship = await _context.CommunityFriends
                .FirstOrDefaultAsync(f =>
                    (f.MemberID1 == memberId && f.MemberID2 == friendId) ||
                    (f.MemberID1 == friendId && f.MemberID2 == memberId));

            return Ok(friendship != null);
        }

        // 取得好友統計
        [HttpGet("stats/{memberId}")]
        public async Task<IActionResult> GetFriendStats(int memberId)
        {
            var friendsCount = await _context.CommunityFriends
                .CountAsync(f => f.MemberID1 == memberId);

            var pendingRequestsCount = await _context.CommunityFriendRequests
                .CountAsync(r => r.ReceiverID == memberId && r.RequestStatus == "Pending");

            return Ok(new { friendsCount, pendingRequestsCount });
        }
    }
}