using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

[Route("api/[controller]")]
[ApiController]
public class CommunityConversationsController : ControllerBase
{
    private readonly DbNursingHomeContext _context;

    public CommunityConversationsController(DbNursingHomeContext context)
    {
        _context = context;
    }

    // 取得所有對話列表
    // GET: api/communityconversations
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConversationDto>>> GetConversations()
    {
        var tickets = await _context.CommunityTickets.ToListAsync();

        var conversations = new List<ConversationDto>();
        foreach (var ticket in tickets)
        {
            var member = await _context.Members.FirstOrDefaultAsync(m => m.FMemberId == ticket.MemberId);
            var latestMessage = await _context.CommunityMessages
                .Where(m => m.TicketId == ticket.TicketId)
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefaultAsync();

            conversations.Add(new ConversationDto
            {
                Id = ticket.TicketId,
                MemberName = member?.FName ?? "未知",
                MemberAccount = member?.FAccount ?? "未知",
                LatestMessage = latestMessage?.Content ?? "無訊息",
                Status = ticket.TicketsStatus,
                WaitTime = (int)(DateTime.Now - ticket.CreatedAt).TotalMinutes
            });
        }
        return conversations;
    }

    // 更新對話狀態
    // PUT: api/communityconversations/{id}/status
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequest request)
    {
        var ticket = await _context.CommunityTickets.FindAsync(id);
        if (ticket == null) return NotFound();

        ticket.TicketsStatus = request.Status;
        ticket.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // 建立新對話
    [HttpPost]
    public async Task<IActionResult> CreateConversation([FromBody] CreateConversationRequest request)
    {
        var ticket = new CommunityTicket
        {
            MemberId = request.MemberId,
            Title = request.Title,
            Category = request.Category,
            TicketsPriority = request.TicketsPriority,
            TicketsStatus = request.TicketsStatus,
            CreatedAt = DateTime.Now
        };

        _context.CommunityTickets.Add(ticket);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetConversations), new { id = ticket.TicketId }, ticket);
    }

    // 取得特定對話的所有訊息
    [HttpGet("{ticketId}/messages")]
    public async Task<ActionResult<IEnumerable<CommunityMessage>>> GetMessages(int ticketId)
    {
        var messages = await _context.CommunityMessages
            .Where(m => m.TicketId == ticketId)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
        return messages;
    }
}

public class ConversationDto
{
    public int Id { get; set; }
    public string MemberName { get; set; }
    public string MemberAccount { get; set; }
    public string LatestMessage { get; set; }
    public string Status { get; set; }
    public int WaitTime { get; set; }
}

public class UpdateStatusRequest
{
    public string Status { get; set; }
}

public class CreateConversationRequest
{
    public int MemberId { get; set; }
    public string Title { get; set; }
    public string Category { get; set; }
    public string TicketsPriority { get; set; }
    public string TicketsStatus { get; set; }
}