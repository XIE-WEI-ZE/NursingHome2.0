using Microsoft.AspNetCore.Mvc;
using prjFinalProjectApi.Models;
using System.Text.Json;

namespace prjFinalProjectApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommunityChatSessionController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public CommunityChatSessionController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // 使用臨時連結驗證的聊天會話
        [HttpPost("create-temp-session")]
        public IActionResult CreateTempSession([FromBody] TempSessionRequest request)
        {
            if (string.IsNullOrEmpty(request.MemberId) ||
                !int.TryParse(request.MemberId, out int memberId))
                return BadRequest("無效的會員ID");

            // 創建一個臨時的聊天會話ID
            var sessionId = Guid.NewGuid().ToString();

            // 設置session cookie，即使在無痕模式也可以使用
            Response.Cookies.Append("chat_session", sessionId, new CookieOptions
            {
                SameSite = SameSiteMode.Lax,
                HttpOnly = false,
                Expires = DateTime.Now.AddHours(1)
            });

            // 將會員ID對應到這個會話
            Response.Cookies.Append("chat_member_id", request.MemberId, new CookieOptions
            {
                SameSite = SameSiteMode.Lax,
                HttpOnly = false,
                Expires = DateTime.Now.AddHours(1)
            });

            return Ok(new { sessionId, memberId = request.MemberId });
        }

        // 獲取當前聊天會話用戶信息
        [HttpGet("current-user")]
        public IActionResult GetCurrentUser()
        {
            var memberIdCookie = Request.Cookies["chat_member_id"];
            if (string.IsNullOrEmpty(memberIdCookie))
                return NotFound("沒有活動的聊天會話");

            return Ok(new { memberId = memberIdCookie });
        }
    }

    public class TempSessionRequest
    {
        public string MemberId { get; set; } = string.Empty;
    }
}