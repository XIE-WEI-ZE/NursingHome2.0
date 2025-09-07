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

                // 根據角色設定 senderType（先決定類型，再用於後續判斷）
                var roleClaim = Context.User?.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value
                        ?? Context.User?.FindFirst("role")?.Value;
                string senderType = (roleClaim == "Employee" || roleClaim == "Staff" || userId == 1) ? "staff" : "member"; // 加入 userId == 1 作為備用

                // 取得發送者名稱（若為 staff，可覆寫為「客服」或改為查員工表）
                var member = await _context.Members.FindAsync(userId);
                var senderName = member != null ? (member.FName ?? member.FAccount) : "未知用戶";
                if (senderType == "staff")
                {
                    senderName = "客服"; // 或查詢員工名稱
                }

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

                // 群組名稱（在送出訊息前先定義）
                string groupName = $"ticket-{ticketId}";

                // 推送訊息給發送者與群組內其他人
                await Clients.Caller.SendAsync("ReceiveMessage",
                    ticketId,
                    userId,
                    senderName,
                    message,
                    chatMessage.SentAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    senderType);

                await Clients.OthersInGroup(groupName).SendAsync("ReceiveMessage",
                    ticketId,
                    userId,
                    senderName,
                    message,
                    chatMessage.SentAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    senderType);

                Console.WriteLine($"Customer Service Message saved to DB: Ticket {ticketId}, Sender {userId}, Type {senderType}");
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