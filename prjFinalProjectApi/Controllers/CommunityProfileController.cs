using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Security.Claims;

namespace prjFinalProjectApi.Controllers
{
    // 新增 DTO 類別
    public class UpdateProfileDto
    {
        public string? DisplayName { get; set; }
        public string? Bio { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class CommunityProfileController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IWebHostEnvironment _environment;

        public CommunityProfileController(DbNursingHomeContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // 取得會員社群資料
        [HttpGet("{memberId}/profile")]
        public async Task<IActionResult> GetMemberProfile(int memberId)
        {
            // 從 Members 表取得基本資料
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.FMemberId == memberId);

            if (member == null)
                return NotFound("會員不存在");

            // 從 CommunityUserProfiles 表取得社群資料
            var userProfile = await _context.CommunityUserProfiles
                .FirstOrDefaultAsync(p => p.MemberId == memberId);

            // 計算追蹤者數量
            var followersCount = await _context.CommunityFollows
                .CountAsync(f => f.FollowingId == memberId);

            // 檢查當前登入會員是否已追蹤此人
            bool isFollowing = false;
            var loginAccount = User.FindFirstValue(ClaimTypes.Name);
            if (!string.IsNullOrEmpty(loginAccount))
            {
                var loginMember = await _context.Members
                    .FirstOrDefaultAsync(m => m.FAccount == loginAccount);

                if (loginMember != null && loginMember.FMemberId != memberId)
                {
                    isFollowing = await _context.CommunityFollows
                        .AnyAsync(f => f.FollowerId == loginMember.FMemberId && f.FollowingId == memberId);
                }
            }

            var dto = new CommunityProfileDto
            {
                MemberId = member.FMemberId,
                Name = userProfile?.DisplayName ?? member.FName ?? "未設定名稱",
                PhotoUrl = ConvertToFullUrl(userProfile?.ProfilePictureUrl),
                Bio = userProfile?.ProfileBio ?? "",
                Followers = followersCount,
                IsFollowing = isFollowing
            };

            return Ok(dto);
        }

        // 將相對路徑轉換為完整 URL
        private string ConvertToFullUrl(string? photoUrl)
        {
            if (string.IsNullOrEmpty(photoUrl))
                return "https://localhost:7124/assets/img/default-avatar.png";

            if (photoUrl.StartsWith("http"))
                return photoUrl; // 已經是完整 URL

            return $"https://localhost:7124{photoUrl}"; // 轉換為完整 URL
        }

        // 取得會員貼文
        [HttpGet("{memberId}/posts")]
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

        // 上傳會員照片
        [HttpPost("{memberId}/upload-photo")]
        [Authorize]
        public async Task<IActionResult> UploadPhoto(int memberId, IFormFile photo)
        {
            try
            {
                // 驗證登入會員
                var loginAccount = User.FindFirstValue(ClaimTypes.Name);
                if (loginAccount == null) return Unauthorized();

                var loginMember = await _context.Members
                    .FirstOrDefaultAsync(m => m.FAccount == loginAccount);
                if (loginMember == null) return Unauthorized();

                // 確認只能修改自己的照片
                if (loginMember.FMemberId != memberId)
                    return Forbid("只能修改自己的照片");

                // 驗證檔案
                if (photo == null || photo.Length == 0)
                    return BadRequest("請選擇檔案");

                // 檢查檔案類型
                var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/jpg" };
                if (!allowedTypes.Contains(photo.ContentType.ToLower()))
                    return BadRequest("只支援 JPG、PNG、GIF 格式");

                // 檢查檔案大小 (5MB)
                if (photo.Length > 5 * 1024 * 1024)
                    return BadRequest("檔案大小不能超過 5MB");

                // 修正：建立正確的上傳目錄
                var uploadsDir = Path.Combine(_environment.WebRootPath, "images", "community", "userprofiles");
                if (!Directory.Exists(uploadsDir))
                    Directory.CreateDirectory(uploadsDir);

                // 生成唯一檔名
                var fileExtension = Path.GetExtension(photo.FileName);
                var fileName = $"{memberId}_{DateTime.Now:yyyyMMdd_HHmmss}{fileExtension}";
                var filePath = Path.Combine(uploadsDir, fileName);

                // 取得或建立使用者社群檔案
                var userProfile = await _context.CommunityUserProfiles
                    .FirstOrDefaultAsync(p => p.MemberId == memberId);

                // 刪除舊照片（如果存在）
                if (userProfile != null && !string.IsNullOrEmpty(userProfile.ProfilePictureUrl) &&
                    !userProfile.ProfilePictureUrl.Contains("default-avatar"))
                {
                    var oldPhotoPath = Path.Combine(_environment.WebRootPath,
                        userProfile.ProfilePictureUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(oldPhotoPath))
                    {
                        System.IO.File.Delete(oldPhotoPath);
                    }
                }

                // 儲存新照片
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await photo.CopyToAsync(stream);
                }

                // 設定新的照片 URL 路徑
                var photoUrl = $"/images/community/userprofiles/{fileName}";

                if (userProfile == null)
                {
                    // 建立新的社群檔案記錄
                    userProfile = new CommunityUserProfile
                    {
                        MemberId = memberId,
                        DisplayName = loginMember.FName ?? "未設定名稱",
                        ProfileBio = "",
                        ProfilePictureUrl = photoUrl,
                        LastUpdatedAt = DateTime.Now
                    };
                    _context.CommunityUserProfiles.Add(userProfile);
                }
                else
                {
                    // 更新現有記錄
                    userProfile.ProfilePictureUrl = photoUrl;
                    userProfile.LastUpdatedAt = DateTime.Now;
                }

                await _context.SaveChangesAsync();

                // 回傳完整的 URL
                var fullPhotoUrl = $"https://localhost:7124{photoUrl}";
                return Ok(new { photoUrl = fullPhotoUrl });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"照片上傳錯誤: {ex.Message}");
                return StatusCode(500, "照片上傳失敗");
            }
        }

        // 更新會員社群資料（顯示名稱和簡介）
        [HttpPut("{memberId}/profile")]
        [Authorize]
        public async Task<IActionResult> UpdateProfile(int memberId, [FromBody] UpdateProfileDto dto)
        {
            var loginAccount = User.FindFirstValue(ClaimTypes.Name);
            if (loginAccount == null) return Unauthorized();

            var loginMember = await _context.Members
                .FirstOrDefaultAsync(m => m.FAccount == loginAccount);
            if (loginMember == null) return Unauthorized();

            if (loginMember.FMemberId != memberId)
                return Forbid("只能修改自己的資料");

            var userProfile = await _context.CommunityUserProfiles
                .FirstOrDefaultAsync(p => p.MemberId == memberId);

            if (userProfile == null)
            {
                userProfile = new CommunityUserProfile
                {
                    MemberId = memberId,
                    DisplayName = dto.DisplayName ?? loginMember.FName ?? "未設定名稱",
                    ProfileBio = dto.Bio ?? "",
                    ProfilePictureUrl = "/assets/img/default-avatar.png",
                    LastUpdatedAt = DateTime.Now
                };
                _context.CommunityUserProfiles.Add(userProfile);
            }
            else
            {
                userProfile.DisplayName = dto.DisplayName ?? userProfile.DisplayName;
                userProfile.ProfileBio = dto.Bio ?? userProfile.ProfileBio;
                userProfile.LastUpdatedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync();
            return Ok("資料更新成功");
        }
    }
}