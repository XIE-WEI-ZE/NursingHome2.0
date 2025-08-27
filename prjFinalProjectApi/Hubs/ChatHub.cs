using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;

namespace prjFinalProjectApi.Hubs
{
    public class ChatHub : Hub
    {
        private readonly DbNursingHomeContext _context;

        public ChatHub(DbNursingHomeContext context)
        {
            _context = context;
        }

        // 當用戶連線
        public override async Task OnConnectedAsync()
        {
            // 從 JWT Token 取得用戶 ID 並註冊連線
            var memberIdClaim = Context.User?.FindFirst("name")?.Value;
            if (int.TryParse(memberIdClaim, out int memberId))
            {
                ChatUserConnection.AddConnection(memberId, Context.ConnectionId);
                Console.WriteLine($"User {memberId} connected with connectionId: {Context.ConnectionId}");
            }
            await base.OnConnectedAsync();
        }

        // 當用戶斷線
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // 移除連線記錄
            var memberIdClaim = Context.User?.FindFirst("name")?.Value;
            if (int.TryParse(memberIdClaim, out int memberId))
            {
                ChatUserConnection.RemoveConnection(memberId, Context.ConnectionId);
                Console.WriteLine($"User {memberId} disconnected from connectionId: {Context.ConnectionId}");
            }
            await base.OnDisconnectedAsync(exception);
        }

        // 加入聊天室（加入 SignalR 群組）
        public async Task JoinRoom(string roomId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            Console.WriteLine($"Connection {Context.ConnectionId} joined room {roomId}");

            // 紀錄使用者加入聊天室的時間
            var memberIdClaim = Context.User?.FindFirst("name")?.Value;
            if (int.TryParse(memberIdClaim, out int memberId))
            {
                var exists = await _context.CommunityChatRoomMembers
                    .AnyAsync(x => x.RoomId == int.Parse(roomId) && x.MemberId == memberId);

                if (!exists)
                {
                    _context.CommunityChatRoomMembers.Add(new CommunityChatRoomMember
                    {
                        RoomId = int.Parse(roomId),
                        MemberId = memberId,
                        JoinedAt = DateTime.Now
                    });
                    await _context.SaveChangesAsync();
                }
            }
        }

        // 發送訊息給聊天室成員
        public async Task SendMessage(string roomId, string senderId, string message)
        {
            int roomIdInt = int.Parse(roomId);
            int senderIdInt = int.Parse(senderId);

            try
            {
                // 建立訊息物件並寫入 DB
                var chatMessage = new CommunityChatMessage
                {
                    RoomId = roomIdInt,
                    MemberId = senderIdInt,
                    Content = message,
                    SentAt = DateTime.Now
                };
                _context.CommunityChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Message saved to DB: Room {roomId}, Sender {senderId}");

                // 使用 SignalR Groups 廣播給整個房間
                // 注意：參數順序要與 Angular 接收的一致
                await Clients.Group(roomId).SendAsync("ReceiveMessage",
                    roomId,  // roomId 參數
                    senderId,
                    message,
                    chatMessage.SentAt.ToString("yyyy-MM-dd HH:mm:ss"));

                Console.WriteLine($"Message broadcasted to room {roomId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SendMessage: {ex.Message}");
                throw;
            }
        }
    }

    // 記錄使用者對應的 SignalR 連線
    public static class ChatUserConnection
    {
        private static readonly Dictionary<int, HashSet<string>> _connections = new();

        public static void AddConnection(int memberId, string connectionId)
        {
            lock (_connections)
            {
                if (!_connections.ContainsKey(memberId))
                    _connections[memberId] = new HashSet<string>();
                _connections[memberId].Add(connectionId);
            }
        }

        // 移除使用者的連線
        public static void RemoveConnection(int memberId, string connectionId)
        {
            lock (_connections)
            {
                if (_connections.ContainsKey(memberId))
                {
                    _connections[memberId].Remove(connectionId);
                    if (_connections[memberId].Count == 0)
                        _connections.Remove(memberId);
                }
            }
        }

        // 獲取使用者的所有連線 ID
        public static IEnumerable<string> GetConnections(int memberId)
        {
            lock (_connections)
            {
                if (_connections.TryGetValue(memberId, out var set))
                    return set.ToList(); // 返回副本避免並發問題
                return Enumerable.Empty<string>();
            }
        }

        // 獲取所有使用者的連線數量（僅用於除錯）
        public static Dictionary<int, int> GetAllUserConnectionCounts()
        {
            lock (_connections)
            {
                return _connections.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
            }
        }

    }
}