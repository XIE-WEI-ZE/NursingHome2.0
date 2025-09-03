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
        public async Task JoinRoom(int roomId, int userId)
        {
            string roomIdString = roomId.ToString();
            await Groups.AddToGroupAsync(Context.ConnectionId, roomIdString);
            Console.WriteLine($"Connection {Context.ConnectionId} joined room {roomId}, User: {userId}");

            // 驗證用戶ID是否與JWT Token中的一致（安全檢查）
            var memberIdClaim = Context.User?.FindFirst("MemberID")?.Value
                                ?? Context.User?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

            if (int.TryParse(memberIdClaim, out int tokenMemberId) && tokenMemberId != userId)
            {
                Console.WriteLine($"用戶ID不匹配: Token={tokenMemberId}, 參數={userId}");
                await Clients.Caller.SendAsync("Error", "用戶身份驗證失敗");
                return;
            }

            // 紀錄使用者加入聊天室的時間
            var exists = await _context.CommunityChatRoomMembers
                .AnyAsync(x => x.RoomId == roomId && x.MemberId == userId);

            if (!exists)
            {
                _context.CommunityChatRoomMembers.Add(new CommunityChatRoomMember
                {
                    RoomId = roomId,
                    MemberId = userId,
                    JoinedAt = DateTime.Now
                });
                await _context.SaveChangesAsync();
                Console.WriteLine($"用戶 {userId} 已加入聊天室 {roomId}");
            }
        }

        // 發送訊息給聊天室成員
        public async Task SendMessage(int roomId, int userId, string message)
        {
            try
            {
                // 驗證用戶ID
                var memberIdClaim = Context.User?.FindFirst("MemberID")?.Value
                                    ?? Context.User?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

                if (int.TryParse(memberIdClaim, out int tokenMemberId) && tokenMemberId != userId)
                {
                    Console.WriteLine($"發送訊息用戶ID不匹配: Token={tokenMemberId}, 參數={userId}");
                    await Clients.Caller.SendAsync("Error", "用戶身份驗證失敗");
                    return;
                }

                // 取得發送者名稱
                var senderName = Context.User?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value ?? "未知用戶";

                // 建立訊息物件並寫入 DB
                var chatMessage = new CommunityChatMessage
                {
                    RoomId = roomId,
                    MemberId = userId,
                    Content = message,
                    SentAt = DateTime.Now
                };
                _context.CommunityChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Message saved to DB: Room {roomId}, Sender {userId}");

                // 廣播給整個房間
                await Clients.Group(roomId.ToString()).SendAsync("ReceiveMessage",
                    roomId.ToString(),  // roomId
                    userId.ToString(),  // senderId
                    senderName,         // senderName
                    message,           // message content
                    chatMessage.SentAt.ToString("yyyy-MM-dd HH:mm:ss")); // timestamp

                Console.WriteLine($"Message broadcasted to room {roomId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SendMessage: {ex.Message}");
                await Clients.Caller.SendAsync("Error", $"發送訊息失敗: {ex.Message}");
                throw;
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
}