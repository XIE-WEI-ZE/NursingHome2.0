using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Hubs;
using prjFinalProjectApi.Models;
using System.Linq;
using System.Security.Claims;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CommunityCustomerServiceController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IHubContext<CustomerServiceHub> _hubContext;

        // 注入 IHubContext<CustomerServiceHub>
        public CommunityCustomerServiceController(DbNursingHomeContext context, IHubContext<CustomerServiceHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // Helper: 由 JWT claims 嘗試取得 memberId（若沒有可傳入 query/body）
        private int? GetMemberIdFromClaims()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub") ?? User.FindFirst("memberId");
            if (idClaim == null) return null;
            return int.TryParse(idClaim.Value, out var id) ? id : null;
        }

        // ==========================
        // 前端會員（Member）端點
        // 需要：__MemberOnly__ policy (Program.cs 已設定)
        // ==========================

        // 建立新的客服工單（會員）
        [HttpPost("member/tickets")]
        [Authorize(Policy = "MemberOnly")]
        public async Task<IActionResult> CreateTicketByMember([FromBody] CreateTicketRequest req)
        {
            var memberId = GetMemberIdFromClaims() ?? req.MemberId;
            if (memberId == null) return Forbid();

            var ticket = new CommunityTicket
            {
                MemberId = memberId.Value,
                Category = req.Category ?? "一般",
                TicketsPriority = req.Priority ?? "Normal",
                TicketsStatus = "等待",
                Title = req.Subject ?? "", // model 使用 Title
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _context.CommunityTickets.Add(ticket);
            await _context.SaveChangesAsync();

            var message = new CommunityMessage
            {
                TicketId = ticket.TicketId,
                SenderType = "member",
                Content = req.InitialMessage ?? "",
                SentAt = DateTime.Now
            };

            _context.CommunityMessages.Add(message);
            await _context.SaveChangesAsync();

            // 推送新對話到所有員工
            await _hubContext.Clients.All.SendAsync("NewConversation", new
            {
                id = ticket.TicketId,
                memberId = ticket.MemberId,
                memberName = _context.Members
                    .Where(m => m.FMemberId == ticket.MemberId)
                    .Select(m => m.FName ?? m.FAccount)
                    .FirstOrDefault() ?? "匿名",
                status = ticket.TicketsStatus,
                priority = ticket.TicketsPriority,
                category = ticket.Category,
                title = ticket.Title,
                latestMessage = req.InitialMessage ?? "",
                lastMessageTime = ticket.CreatedAt
            });

            return Ok(new { ticketId = ticket.TicketId });
        }

        // 會員取得自己所有工單
        [HttpGet("member/tickets")]
        [Authorize(Policy = "MemberOnly")]
        public async Task<IActionResult> GetMemberTickets()
        {
            var memberId = GetMemberIdFromClaims();
            if (memberId == null) return Forbid();

            var tickets = await _context.CommunityTickets
                .Where(t => t.MemberId == memberId.Value)
                .OrderByDescending(t => t.UpdatedAt)
                .Select(t => new
                {
                    t.TicketId,
                    t.Category,
                    title = t.Title,
                    status = t.TicketsStatus,
                    priority = t.TicketsPriority,
                    createdAt = t.CreatedAt,
                    updatedAt = t.UpdatedAt
                })
                .ToListAsync();

            return Ok(tickets);
        }

        // 會員發送訊息到工單
        [HttpPost("member/tickets/{ticketId}/messages")]
        [Authorize(Policy = "MemberOnly")]
        public async Task<IActionResult> MemberSendMessage(int ticketId, [FromBody] SendMessageRequest req)
        {
            var memberId = GetMemberIdFromClaims();
            if (memberId == null) return Forbid();

            var ticket = await _context.CommunityTickets.FindAsync(ticketId);
            if (ticket == null || ticket.MemberId != memberId.Value) return NotFound("找不到工單或無權限");

            var message = new CommunityMessage
            {
                TicketId = ticketId,
                SenderType = "member",
                Content = req.Content ?? "",
                SentAt = DateTime.Now
            };

            _context.CommunityMessages.Add(message);
            ticket.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Ok(new { messageId = message.MessageId });
        }

        // 會員取得某工單所有訊息
        [HttpGet("member/tickets/{ticketId}/messages")]
        [Authorize(Policy = "MemberOnly")]
        public async Task<IActionResult> MemberGetMessages(int ticketId)
        {
            var memberId = GetMemberIdFromClaims();
            if (memberId == null) return Forbid();

            var ticket = await _context.CommunityTickets.FindAsync(ticketId);
            if (ticket == null || ticket.MemberId != memberId.Value) return NotFound("找不到工單或無權限");

            // 取得會員名稱（顯示用），使用 FName 或 FAccount
            var member = await _context.Members.FindAsync(ticket.MemberId);
            var memberName = member != null ? (member.FName ?? member.FAccount) : "會員";

            var messages = (await _context.CommunityMessages
                .Where(m => m.TicketId == ticketId)
                .OrderBy(m => m.SentAt)
                .ToListAsync())
                .Select(m => new
                {
                    m.MessageId,
                    m.TicketId,
                    senderType = m.SenderType,
                    senderName = m.SenderType == "member" ? memberName : "客服",
                    content = m.Content,
                    timestamp = m.SentAt
                })
                .ToList();

            return Ok(messages);
        }

        // ==========================
        // 後台客服（Staff）端點
        // 需要：__EmployeeCookieOnly__ policy (Program.cs 已設定)
        // ==========================

        // 後台取得所有會話（列表）
        [HttpGet("staff/conversations")]
        [Authorize(Policy = "EmployeeCookieOnly")]
        public async Task<IActionResult> StaffGetConversations()
        {
            var conversations = await _context.CommunityTickets
                .OrderByDescending(t => t.UpdatedAt)
                .Select(t => new
                {
                    id = t.TicketId,
                    memberId = t.MemberId,
                    memberName = _context.Members
                                    .Where(m => m.FMemberId == t.MemberId)
                                    .Select(m => m.FName ?? m.FAccount)
                                    .FirstOrDefault() ?? "匿名",
                    status = t.TicketsStatus,
                    priority = t.TicketsPriority,
                    category = t.Category,
                    title = t.Title,
                    latestMessage = _context.CommunityMessages
                        .Where(m => m.TicketId == t.TicketId)
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => m.Content)
                        .FirstOrDefault() ?? "",
                    lastMessageTime = _context.CommunityMessages
                        .Where(m => m.TicketId == t.TicketId)
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => m.SentAt)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(conversations);
        }

        // 後台取得單一會話訊息
        [HttpGet("staff/conversations/{conversationId}/messages")]
        [Authorize(Policy = "EmployeeCookieOnly")]
        public async Task<IActionResult> StaffGetMessages(int conversationId)
        {
            var ticket = await _context.CommunityTickets.FindAsync(conversationId);
            if (ticket == null) return NotFound();

            var member = await _context.Members.FindAsync(ticket.MemberId);
            var memberName = member != null ? (member.FName ?? member.FAccount) : "會員";

            var messages = (await _context.CommunityMessages
                .Where(m => m.TicketId == conversationId)
                .OrderBy(m => m.SentAt)
                .ToListAsync())
                .Select(m => new
                {
                    m.MessageId,
                    m.TicketId,
                    senderType = m.SenderType,
                    senderName = m.SenderType == "member" ? memberName : "客服",
                    content = m.Content,
                    timestamp = m.SentAt
                })
                .ToList();

            return Ok(messages);
        }

        // 後台發送訊息到會話
        [HttpPost("staff/conversations/{conversationId}/messages")]
        [Authorize(Policy = "EmployeeCookieOnly")]
        public async Task<IActionResult> StaffSendMessage(int conversationId, [FromBody] SendMessageRequest req)
        {
            var ticket = await _context.CommunityTickets.FindAsync(conversationId);
            if (ticket == null) return NotFound();

            var message = new CommunityMessage
            {
                TicketId = conversationId,
                SenderType = "staff",
                Content = req.Content ?? "",
                SentAt = DateTime.Now
            };

            _context.CommunityMessages.Add(message);
            ticket.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Ok(new { messageId = message.MessageId });
        }

        // 後台更新會話狀態
        [HttpPatch("staff/conversations/{conversationId}/status")]
        [Authorize(Policy = "EmployeeCookieOnly")]
        public async Task<IActionResult> StaffUpdateStatus(int conversationId, [FromBody] UpdateStatusRequest req)
        {
            var ticket = await _context.CommunityTickets.FindAsync(conversationId);
            if (ticket == null) return NotFound();

            ticket.TicketsStatus = req.Status ?? ticket.TicketsStatus;
            ticket.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Ok();
        }

        // 後台指派客服（可選）
        [HttpPatch("staff/conversations/{conversationId}/assign")]
        [Authorize(Policy = "EmployeeCookieOnly")]
        public async Task<IActionResult> StaffAssign(int conversationId, [FromBody] AssignRequest req)
        {
            var ticket = await _context.CommunityTickets.FindAsync(conversationId);
            if (ticket == null) return NotFound();

            // model 裡的欄位為 HandlerEmployeeId
            ticket.HandlerEmployeeId = req.EmployeeId;
            ticket.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Ok();
        }
    }

    // ======= DTOs =======
    public class CreateTicketRequest
    {
        // 若伺服器端無法從 claims 得到 memberId，前端可帶入（通常不建議）
        public int? MemberId { get; set; }
        public string? Category { get; set; }
        public string? Priority { get; set; }
        public string? Subject { get; set; }
        public string? InitialMessage { get; set; }
    }

    public class SendMessageRequest
    {
        public string? Content { get; set; }
    }

    public class UpdateStatusRequest
    {
        public string? Status { get; set; }
    }

    public class AssignRequest
    {
        public int? EmployeeId { get; set; }
    }
}