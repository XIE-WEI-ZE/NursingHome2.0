using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;

namespace prjFinalProjectApi.Hubs
{
    public class CustomerServiceHub : Hub
    {
        private readonly DbNursingHomeContext _context;

        public CustomerServiceHub(DbNursingHomeContext context)
        {
            _context = context;
        }

        // 當用戶連線
        public override async Task OnConnectedAsync()
        {
            var memberIdClaim = Context.User?.FindFirst("name")?.Value;
            if (int.TryParse(memberIdClaim, out int memberId))
            {
                Console.WriteLine($"Customer Service User {memberId} connected with connectionId: {Context.ConnectionId}");
            }
            await base.OnConnectedAsync();
        }

        // 當用戶斷線
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var memberIdClaim = Context.User?.FindFirst("name")?.Value;
            if (int.TryParse(memberIdClaim, out int memberId))
            {
                Console.WriteLine($"Customer Service User {memberId} disconnected from connectionId: {Context.ConnectionId}");
            }
            await base.OnDisconnectedAsync(exception);
        }

        // 加入客服會話（加入 SignalR 群組）
        public async Task JoinConversation(int ticketId, int userId)
        {
            string groupName = $"ticket-{ticketId}"; // 使用 ticketId 作為群組名稱
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            Console.WriteLine($"Connection {Context.ConnectionId} joined conversation {ticketId}, User: {userId}");

            // 驗證用戶ID
            var memberIdClaim = Context.User?.FindFirst("MemberID")?.Value
                                ?? Context.User?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

            if (int.TryParse(memberIdClaim, out int tokenMemberId) && tokenMemberId != userId)
            {
                Console.WriteLine($"用戶ID不匹配: Token={tokenMemberId}, 參數={userId}");
                await Clients.Caller.SendAsync("Error", "用戶身份驗證失敗");
                return;
            }

            Console.WriteLine($"用戶 {userId} 已加入客服會話 {ticketId}");
        }

        // 發送客服訊息
        public async Task SendMessage(int ticketId, int userId, string message)
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

                // 根據角色設定 senderType
                var roleClaim = Context.User?.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value
                                ?? Context.User?.FindFirst("role")?.Value;
                string senderType = (roleClaim == "Employee" || roleClaim == "Staff") ? "staff" : "member";

                // 建立訊息物件並寫入 DB（使用 CommunityMessage）
                var chatMessage = new CommunityMessage
                {
                    TicketId = ticketId,
                    SenderType = senderType,
                    Content = message,
                    SentAt = DateTime.Now
                };
                _context.CommunityMessages.Add(chatMessage);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Customer Service Message saved to DB: Ticket {ticketId}, Sender {userId}, Type {senderType}");

                // 廣播給房間的其他成員（排除發送者）
                string groupName = $"ticket-{ticketId}";
                await Clients.OthersInGroup(groupName).SendAsync("ReceiveMessage",
                    ticketId.ToString(),
                    userId.ToString(),
                    senderName,
                    message,
                    chatMessage.SentAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    senderType); // 添加 senderType 到廣播

                Console.WriteLine($"Message broadcasted to conversation {ticketId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SendMessage: {ex.Message}");
                await Clients.Caller.SendAsync("Error", $"發送訊息失敗: {ex.Message}");
                throw;
            }
        }
    }
}