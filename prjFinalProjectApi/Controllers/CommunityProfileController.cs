using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Security.Claims;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController] 
    public class CommunityProfileController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public CommunityProfileController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // 取得會員社群資料
        [HttpGet("{memberId}/profile")]
        // [Authorize] // 暫時註釋掉
        public async Task<IActionResult> GetMemberProfile(int memberId)
        {
            // 暫時不檢查登入會員，直接使用第一個會員來測試
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.FMemberId == memberId);
            if (member == null)
                return NotFound();

            // 簡化版本，先不處理追蹤狀態
            var dto = new CommunityProfileDto
            {
                MemberId = member.FMemberId,
                Name = member.FName ?? "未設定名稱",
                PhotoUrl = "/assets/img/default-avatar.png", // 暫時用固定值
                Bio = "",
                Followers = 0, // 暫時用固定值
                IsFollowing = false
            };

            return Ok(dto);
        }
    
        // 取得會員貼文
        [HttpGet("{memberId}/posts")]
        //[Authorize]
        public async Task<IActionResult> GetMemberPosts(int memberId)
        {
            var posts = await _context.CommunityPosts
                .Where(p => p.MemberId == memberId)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    Id = p.PostId,
                    Title = p.Title,
                    Content = p.Content,
                    CreatedAt = p.CreatedAt,
                    BoardID = p.BoardId,
                })
                .ToListAsync();

            return Ok(posts);
        }

        // 切換追蹤
        [HttpPost("{memberId}/toggle-follow")]
        [Authorize]
        public async Task<IActionResult> ToggleFollow(int memberId)
        {
            var loginAccount = User.FindFirstValue(ClaimTypes.Name);
            if (loginAccount == null) return Unauthorized();

            var loginMember = await _context.Members
                .FirstOrDefaultAsync(m => m.FAccount == loginAccount);
            if (loginMember == null) return Unauthorized();

            if (loginMember.FMemberId == memberId)
                return BadRequest("不能追蹤自己");

            var follow = await _context.CommunityFollows
                .FirstOrDefaultAsync(f => f.FollowerId == loginMember.FMemberId && f.FollowingId == memberId);

            bool isFollowing;
            if (follow != null)
            {
                _context.CommunityFollows.Remove(follow);
                isFollowing = false;
            }
            else
            {
                _context.CommunityFollows.Add(new CommunityFollow
                {
                    FollowerId = loginMember.FMemberId,
                    FollowingId = memberId,
                    FollowedAt = DateTime.Now
                });
                isFollowing = true;
            }

            await _context.SaveChangesAsync();

            int followersCount = await _context.CommunityFollows
                .CountAsync(f => f.FollowingId == memberId);

            return Ok(new { isFollowing, followers = followersCount });
        }
    }
}
