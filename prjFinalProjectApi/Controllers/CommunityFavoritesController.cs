using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;

namespace prjFinalProjectApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommunityFavoritesController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public CommunityFavoritesController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // 切換收藏貼文
        [HttpPost("{postId}/favorite")]
        public async Task<IActionResult> ToggleFavorite(int postId)
        {
            int memberId = GetCurrentMemberId();
            if (memberId == 0) return Unauthorized("未授權的用戶");

            var existing = await _context.CommunityFavorites
                .FirstOrDefaultAsync(f => f.MemberId == memberId && f.PostId == postId);

            if (existing != null)
            {
                _context.CommunityFavorites.Remove(existing);
                await _context.SaveChangesAsync();
                return Ok(new { isFavorited = false });
            }
            else
            {
                var favorite = new CommunityFavorite
                {
                    MemberId = memberId,
                    PostId = postId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.CommunityFavorites.Add(favorite);
                await _context.SaveChangesAsync();
                return Ok(new { isFavorited = true });
            }
        }

        // 取得會員收藏列表
        [HttpGet("{memberId}/favorites")]
        public async Task<IActionResult> GetFavorites(int memberId)
        {
            var favorites = await _context.CommunityFavorites
                .Where(f => f.MemberId == memberId)
                .Select(f => new { f.PostId })
                .ToListAsync();

            return Ok(favorites);
        }

        // 新增收藏
        [HttpPost("{memberId}/favorites")]
        public async Task<IActionResult> AddFavorite(int memberId, [FromBody] dynamic dto)
        {
            int currentMemberId = GetCurrentMemberId();
            if (currentMemberId == 0 || currentMemberId != memberId) return Unauthorized("未授權的用戶");

            int postId = dto.postId;
            var exists = await _context.CommunityFavorites
                .AnyAsync(f => f.MemberId == memberId && f.PostId == postId);
            if (exists) return BadRequest("該貼文已收藏");

            var favorite = new CommunityFavorite
            {
                MemberId = memberId,
                PostId = postId,
                CreatedAt = DateTime.UtcNow
            };
            _context.CommunityFavorites.Add(favorite);
            await _context.SaveChangesAsync();
            return Ok(favorite);
        }

        // 取消收藏
        [HttpDelete("{memberId}/favorites/{postId}")]
        public async Task<IActionResult> RemoveFavorite(int memberId, int postId)
        {
            int currentMemberId = GetCurrentMemberId();
            if (currentMemberId == 0 || currentMemberId != memberId) return Unauthorized("未授權的用戶");

            var favorite = await _context.CommunityFavorites
                .FirstOrDefaultAsync(f => f.MemberId == memberId && f.PostId == postId);
            if (favorite == null) return NotFound("收藏記錄不存在");

            _context.CommunityFavorites.Remove(favorite);
            await _context.SaveChangesAsync();
            return Ok();
        }

        // 透過 JWT 取得登入會員 ID
        private int GetCurrentMemberId()
        {
            var claim = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "MemberId");
            if (claim != null && int.TryParse(claim.Value, out int memberId))
                return memberId;
            return 0;
        }
    }
}