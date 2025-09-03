using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Services;
using System.Net.Http;
using System.Net.Http.Json;

namespace prjFinalProjectApi.Controllers
{
    // DTO
    public class AIChatRequest
    {
        public string UserMessage { get; set; } = string.Empty;
    }

    public class AIChatResponse
    {
        public string AiReply { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowWeb")] // 修正：與 Program.cs 一致
    public class ChatController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IAIService _aiService;

        public ChatController(DbNursingHomeContext context, IAIService aiService)
        {
            _context = context;
            _aiService = aiService;
        }

        // 取得或建立私人聊天室
        [HttpGet("private-room/{myId}/{friendId}")]
        public async Task<IActionResult> GetOrCreatePrivateRoom(int myId, int friendId)
        {
            var room = await _context.CommunityChatRooms
                .Where(r => r.RoomType == "Private")
                .Where(r => _context.CommunityChatRoomMembers.Any(m => m.RoomId == r.RoomId && m.MemberId == myId))
                .Where(r => _context.CommunityChatRoomMembers.Any(m => m.RoomId == r.RoomId && m.MemberId == friendId))
                .FirstOrDefaultAsync();

            if (room == null)
            {
                room = new CommunityChatRoom
                {
                    RoomName = null,
                    RoomType = "Private",
                    CreatorMemberId = myId,
                    CreatedAt = DateTime.Now,
                    RoomStatus = "啟用"
                };
                _context.CommunityChatRooms.Add(room);
                await _context.SaveChangesAsync();

                _context.CommunityChatRoomMembers.AddRange(new[]
                {
            new CommunityChatRoomMember { RoomId = room.RoomId, MemberId = myId, JoinedAt = DateTime.Now },
            new CommunityChatRoomMember { RoomId = room.RoomId, MemberId = friendId, JoinedAt = DateTime.Now }
        });
                await _context.SaveChangesAsync();
            }

            // 取得好友的名稱
            var friendName = await _context.Members
                .Where(m => m.FMemberId == friendId)
                .Select(m => m.FName)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                roomId = room.RoomId,
                roomName = friendName ?? "未知用戶", // 使用好友名稱作為聊天室名稱
                friendId = friendId,
                friendName = friendName,
                members = await _context.CommunityChatRoomMembers
                    .Where(m => m.RoomId == room.RoomId)
                    .Select(m => new { memberId = m.MemberId })
                    .ToListAsync()
            });
        }


        // 取得使用者所有私人聊天室
        [HttpGet("private-rooms/{userId}")]
        public async Task<IActionResult> GetPrivateRooms(int userId)
        {
            var rooms = await (from m in _context.CommunityChatRoomMembers
                               join r in _context.CommunityChatRooms on m.RoomId equals r.RoomId
                               where m.MemberId == userId && r.RoomType == "Private"
                               select new
                               {
                                   roomId = r.RoomId,
                                   roomName = r.RoomName,
                                   members = _context.CommunityChatRoomMembers
                                           .Where(rm => rm.RoomId == r.RoomId && rm.MemberId != userId)
                                           .Select(rm => new {
                                               memberId = rm.MemberId,
                                               memberName = _context.Members
                                                          .Where(cm => cm.FMemberId == rm.MemberId)
                                                          .Select(cm => cm.FName)
                                                          .FirstOrDefault()
                                           })
                                           .ToList()
                               }).ToListAsync();

            var result = rooms.Select(r => new
            {
                roomId = r.roomId,
                roomName = r.members.FirstOrDefault()?.memberName ?? "未知用戶", // 使用對方的名稱
                friendId = r.members.FirstOrDefault()?.memberId,
                friendName = r.members.FirstOrDefault()?.memberName
            }).ToList();

            return Ok(result);
        }

        // 取得歷史訊息
        [HttpGet("{roomId}/messages")]
        public async Task<IActionResult> GetMessages(int roomId)
        {
            var messages = await _context.CommunityChatMessages
                .Where(m => m.RoomId == roomId)
                .OrderBy(m => m.SentAt)
                .Select(m => new
                {
                    senderId = m.MemberId,
                    message = m.Content,
                    sentAt = m.SentAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    roomId = m.RoomId
                })
                .ToListAsync();

            return Ok(messages);
        }

        // AI 聊天 (使用 IAIService)
        [HttpPost("ai-chat")]
        public async Task<IActionResult> AIChat([FromBody] AIChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserMessage))
                return BadRequest(new AIChatResponse { AiReply = "使用者訊息為空" });

            try
            {
                // 呼叫已搬到 Services 的 OllamaService
                var aiReply = await _aiService.GetReplyAsync(request.UserMessage);
                return Ok(new AIChatResponse { AiReply = aiReply });
            }
            catch (Exception ex)
            {
                return BadRequest(new AIChatResponse { AiReply = $"發生錯誤: {ex.Message}" });
            }
        }
    }
}