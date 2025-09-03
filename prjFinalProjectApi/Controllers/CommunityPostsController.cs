using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using prjFinalProjectApi.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using prjFinalProjectApi.Models.Dto;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace prjFinalProjectApi.Controllers
{
    public class UpdateStatusDto
    {
        public string Status { get; set; }
    }

    public class UpdatePostDto
    {
        [Required(ErrorMessage = "標題不可空白")]
        public string Title { get; set; }

        [Required(ErrorMessage = "內容不可空白")]
        public string Content { get; set; }

        public List<IFormFile> Attachments { get; set; } = new List<IFormFile>();
    }

    public class UpdateReplyDto
    {
        public string Content { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class CommunityPostsController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IWebHostEnvironment _env;

        public CommunityPostsController(DbNursingHomeContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // 新增文章
        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreatePost([FromForm] CreatePostDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Content))
            {
                return BadRequest("標題與內容不可空白");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var post = new CommunityPost
                {
                    MemberId = dto.MemberID,
                    BoardId = dto.BoardID,
                    Title = dto.Title,
                    Content = dto.Content,
                    QuotePostId = dto.QuotePostID,
                    ParentPostId = dto.ParentPostID,
                    IsPinned = false,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    PostStatus = "Active"
                };

                _context.CommunityPosts.Add(post);
                await _context.SaveChangesAsync();

                // 處理附件
                if (dto.Attachments != null && dto.Attachments.Count > 0)
                {
                    var uploadsFolder = Path.Combine(_env.WebRootPath, "images", "community", "post");
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    foreach (var file in dto.Attachments)
                    {
                        var uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using var stream = new FileStream(filePath, FileMode.Create);
                        await file.CopyToAsync(stream);

                        // 新增附件記錄，儲存相對路徑
                        var attachment = new CommunityAttachment
                        {
                            PostId = post.PostId,
                            ReplyId = null,
                            AttachmentUrl = $"/images/community/post/{uniqueFileName}"
                        };
                        _context.CommunityAttachments.Add(attachment);
                    }
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                return Ok(new { message = "文章新增成功", postId = post.PostId });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"新增文章失敗: {ex.Message}");
            }
        }

        // 更新文章
        [HttpPut("{id}")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdatePost(int id, [FromForm] UpdatePostDto dto)
        {

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                Console.WriteLine("ModelState 錯誤：" + string.Join(" | ", errors));
                return BadRequest(new { message = "請求資料無效", errors });
            }

            var post = await _context.CommunityPosts.FindAsync(id);
            if (post == null)
                return NotFound("文章不存在");

            if (string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Content))
            {
                return BadRequest("標題與內容不可空白");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 更新基本資訊
                post.Title = dto.Title.Trim();
                post.Content = dto.Content.Trim();
                post.UpdatedAt = DateTime.Now;

                // 處理附件刪除
                var deletedAttachmentIdsJson = Request.Form["deletedAttachmentIds"].FirstOrDefault();
                if (!string.IsNullOrEmpty(deletedAttachmentIdsJson))
                {
                    try
                    {
                        var deletedAttachmentIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(deletedAttachmentIdsJson);
                        if (deletedAttachmentIds != null && deletedAttachmentIds.Count > 0)
                        {
                            Console.WriteLine($"準備刪除附件 IDs: {string.Join(", ", deletedAttachmentIds)}");

                            // 查詢要刪除的附件
                            var attachmentsToDelete = await _context.CommunityAttachments
                                .Where(a => deletedAttachmentIds.Contains(a.AttachmentId) && a.PostId == id)
                                .ToListAsync();

                            Console.WriteLine($"找到 {attachmentsToDelete.Count} 個附件需要刪除");

                            // 刪除實體檔案
                            foreach (var attachment in attachmentsToDelete)
                            {
                                var filePath = Path.Combine(_env.WebRootPath, attachment.AttachmentUrl.TrimStart('/'));
                                if (System.IO.File.Exists(filePath))
                                {
                                    try
                                    {
                                        System.IO.File.Delete(filePath);
                                        Console.WriteLine($"刪除檔案: {filePath}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"刪除檔案失敗: {filePath}, 錯誤: {ex.Message}");
                                        // 繼續執行，不因為檔案刪除失敗而中止
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"檔案不存在: {filePath}");
                                }
                            }

                            // 刪除資料庫記錄
                            _context.CommunityAttachments.RemoveRange(attachmentsToDelete);
                            await _context.SaveChangesAsync();
                            Console.WriteLine($"已從資料庫刪除 {attachmentsToDelete.Count} 個附件記錄");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"處理附件刪除時發生錯誤: {ex.Message}");
                        // 可以選擇繼續執行或回滾交易
                        throw; // 如果要中止更新，取消註解這行
                    }
                }

                // 處理新附件
                if (dto.Attachments != null && dto.Attachments.Count > 0)
                {
                    var uploadsFolder = Path.Combine(_env.WebRootPath, "images", "community", "post");
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    Console.WriteLine($"準備上傳 {dto.Attachments.Count} 個新附件");

                    foreach (var file in dto.Attachments)
                    {
                        if (file.Length <= 0) continue;

                        var uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using var stream = new FileStream(filePath, FileMode.Create);
                        await file.CopyToAsync(stream);

                        var attachment = new CommunityAttachment
                        {
                            PostId = post.PostId,
                            ReplyId = null,
                            AttachmentUrl = $"/images/community/post/{uniqueFileName}"
                        };
                        _context.CommunityAttachments.Add(attachment);
                        Console.WriteLine($"新增附件: {uniqueFileName}");
                    }
                }

                // 儲存所有變更
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                Console.WriteLine("文章更新成功");
                return Ok(new { message = "文章更新成功" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"更新文章失敗: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"內部錯誤: {ex.InnerException.Message}");
                }
                return StatusCode(500, new { message = "文章更新失敗", error = ex.Message });
            }
        }

        // 更新文章狀態
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdatePostStatus(int id, [FromBody] UpdateStatusDto dto)
        {
            var post = await _context.CommunityPosts.FindAsync(id);
            if (post == null)
                return NotFound("文章不存在");

            post.PostStatus = dto.Status;
            post.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return Ok(new { message = "狀態更新成功" });
        }

        // 取得所有文章
        [HttpGet]
        public async Task<IActionResult> GetAllPosts()
        {
            try
            {
                var posts = await (from p in _context.CommunityPosts
                                   join m in _context.Members on p.MemberId equals m.FMemberId into memberGroup
                                   from m in memberGroup.DefaultIfEmpty()
                                   select new
                                   {
                                       p.PostId,
                                       p.MemberId,
                                       p.BoardId,
                                       p.Title,
                                       p.Content,
                                       p.IsPinned,
                                       p.PostStatus,
                                       p.CreatedAt,
                                       p.UpdatedAt,
                                       likes = _context.CommunityInteractions.Count(i =>
                                           i.TargetType == "Post" &&
                                           i.TargetId == p.PostId &&
                                           i.InteractionsType == "Like"),
                                       favorites = _context.CommunityInteractions.Count(i =>
                                           i.TargetType == "Post" &&
                                           i.TargetId == p.PostId &&
                                           i.InteractionsType == "Favorite"),
                                       comments = _context.CommunityReplies.Count(r =>
                                           r.PostId == p.PostId &&
                                           r.ReplieStatus == "Active"),
                                       views = _context.CommunityInteractions.Count(i =>
                                           i.TargetType == "Post" &&
                                           i.TargetId == p.PostId &&
                                           i.InteractionsType == "View"),
                                       author = m != null ? m.FName : "匿名"
                                   })
                                  .OrderByDescending(p => p.UpdatedAt)  // 修改：按 UpdatedAt 排序
                                  .ToListAsync();

                return Ok(posts);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetAllPosts 錯誤: {ex.Message}");
                return StatusCode(500, $"取得文章列表失敗: {ex.Message}");
            }
        }

        // 依看板取得文章（支援分頁）
        [HttpGet("board/{boardID}/paged")]
        public async Task<IActionResult> GetPostsByBoardPaged(int boardID, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 50) pageSize = 10;

                var skip = (page - 1) * pageSize;

                var query = (from p in _context.CommunityPosts
                             where p.BoardId == boardID && p.PostStatus == "Active"
                             select new
                             {
                                 p.PostId,
                                 p.MemberId,
                                 p.BoardId,
                                 p.Title,
                                 p.Content,
                                 p.IsPinned,
                                 p.PostStatus,
                                 p.CreatedAt,
                                 p.UpdatedAt,
                                 likes = _context.CommunityInteractions.Count(i =>
                                     i.TargetType == "Post" &&
                                     i.TargetId == p.PostId &&
                                     i.InteractionsType == "Like"),
                                 favorites = _context.CommunityInteractions.Count(i =>
                                     i.TargetType == "Post" &&
                                     i.TargetId == p.PostId &&
                                     i.InteractionsType == "Favorite"),
                                 comments = _context.CommunityReplies.Count(r =>
                                     r.PostId == p.PostId &&
                                     r.ReplieStatus == "Active"),
                                 views = _context.CommunityInteractions.Count(i =>
                                     i.TargetType == "Post" &&
                                     i.TargetId == p.PostId &&
                                     i.InteractionsType == "View")
                             });

                var totalCount = await query.CountAsync();
                var hasMore = skip + pageSize < totalCount;

                var posts = await query
                    .OrderByDescending(p => p.UpdatedAt)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                if (posts == null || posts.Count == 0)
                    return Ok(new { posts = new List<object>(), hasMore = false });

                return Ok(new { posts, hasMore });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetPostsByBoardPaged 錯誤: {ex.Message}");
                return StatusCode(500, $"取得文章分頁失敗: {ex.Message}");
            }
        }

        // 依看板取得文章
        [HttpGet("board/{boardID}")]
        public async Task<IActionResult> GetPostsByBoard(int boardID)
        {
            try
            {
                var posts = await (from p in _context.CommunityPosts
                                   where p.BoardId == boardID && p.PostStatus == "Active"
                                   select new
                                   {
                                       p.PostId,
                                       p.MemberId,
                                       p.BoardId,
                                       p.Title,
                                       p.Content,
                                       p.IsPinned,
                                       p.PostStatus,
                                       p.CreatedAt,
                                       p.UpdatedAt,
                                       likes = _context.CommunityInteractions.Count(i =>
                                           i.TargetType == "Post" &&
                                           i.TargetId == p.PostId &&
                                           i.InteractionsType == "Like"),
                                       favorites = _context.CommunityInteractions.Count(i =>
                                           i.TargetType == "Post" &&
                                           i.TargetId == p.PostId &&
                                           i.InteractionsType == "Favorite"),
                                       comments = _context.CommunityReplies.Count(r =>
                                           r.PostId == p.PostId &&
                                           r.ReplieStatus == "Active"),
                                       views = _context.CommunityInteractions.Count(i =>
                                           i.TargetType == "Post" &&
                                           i.TargetId == p.PostId &&
                                           i.InteractionsType == "View")
                                   })
                                  .OrderByDescending(p => p.UpdatedAt)  // 修改：按 UpdatedAt 排序
                                  .ToListAsync();

                if (posts == null || posts.Count == 0)
                    return NotFound("該看板尚無文章");

                return Ok(posts);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetPostsByBoard 錯誤: {ex.Message}");
                return StatusCode(500, $"取得看板文章失敗: {ex.Message}");
            }
        }

        // 記錄文章瀏覽
        [HttpPost("{postId}/view")]
        public async Task<IActionResult> RecordPostView(int postId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 檢查文章是否存在
                var post = await _context.CommunityPosts.FindAsync(postId);
                if (post == null)
                    return NotFound("文章不存在");

                var viewInteraction = new CommunityInteraction
                {
                    MemberId = 0, // 瀏覽可以匿名
                    TargetType = "Post",
                    TargetId = postId,
                    InteractionsType = "View",
                    CreatedAt = DateTime.Now
                };
                _context.CommunityInteractions.Add(viewInteraction);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                // 計算新的瀏覽數量
                var viewCount = await _context.CommunityInteractions
                    .CountAsync(i => i.TargetType == "Post"
                                   && i.TargetId == postId
                                   && i.InteractionsType == "View");

                return Ok(new { viewCount });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"記錄瀏覽失敗: {ex.Message}");
            }
        }

        // 取得單篇文章（包含留言、回覆和附件）
        [HttpGet("{id}")]
        public async Task<IActionResult> GetPost(int id)
        {
            var post = await _context.CommunityPosts.FindAsync(id);
            if (post == null)
                return NotFound("文章不存在");

            // 修復：正確處理文章作者資訊，避免空引用
            var postAuthorQuery = from p in _context.CommunityPosts
                                  join m in _context.Members on p.MemberId equals m.FMemberId into memberGroup
                                  from m in memberGroup.DefaultIfEmpty()
                                  where p.PostId == id
                                  select m != null ? m.FName : "匿名";

            var postAuthor = await postAuthorQuery.FirstOrDefaultAsync() ?? "匿名";

            // 修復：正確計算互動次數，避免空值問題
            var postLikes = await _context.CommunityInteractions
                .CountAsync(i => i.TargetType == "Post" && i.TargetId == id && i.InteractionsType == "Like");

            var postFavorites = await _context.CommunityInteractions
                .CountAsync(i => i.TargetType == "Post" && i.TargetId == id && i.InteractionsType == "Favorite");

            var postShares = await _context.CommunityInteractions
                .CountAsync(i => i.TargetType == "Post" && i.TargetId == id && i.InteractionsType == "Share");

            var postViews = await _context.CommunityInteractions
                .CountAsync(i => i.TargetType == "Post" && i.TargetId == id && i.InteractionsType == "View");

            // 修復：查詢主留言，避免空引用
            var mainReplies = await (from r in _context.CommunityReplies
                                     join m in _context.Members on r.MemberId equals m.FMemberId into memberGroup
                                     from m in memberGroup.DefaultIfEmpty()
                                     where r.PostId == id && r.ParentReplyId == null
                                     orderby r.CreatedAt
                                     select new
                                     {
                                         replyID = r.ReplyId,
                                         memberID = r.MemberId,
                                         name = m != null ? m.FName : "匿名",
                                         avatar = m != null ? m.FProfilePictureUrl : null,
                                         content = r.Content,
                                         createdAt = r.CreatedAt,
                                         replieStatus = r.ReplieStatus,
                                         likes = _context.CommunityInteractions.Count(i =>
                                             i.TargetType == "Reply" &&
                                             i.TargetId == r.ReplyId &&
                                             i.InteractionsType == "Like")
                                     }).ToListAsync();

            // 修復：查詢子回覆，避免空引用
            var allSubReplies = await (from sr in _context.CommunityReplies
                                       join m in _context.Members on sr.MemberId equals m.FMemberId into memberGroup
                                       from m in memberGroup.DefaultIfEmpty()
                                       where sr.PostId == id && sr.ParentReplyId != null
                                       orderby sr.CreatedAt
                                       select new
                                       {
                                           replyID = sr.ReplyId,
                                           memberID = sr.MemberId,
                                           parentReplyID = sr.ParentReplyId,
                                           name = m != null ? m.FName : "匿名",
                                           avatar = m != null ? m.FProfilePictureUrl : null,
                                           content = sr.Content,
                                           createdAt = sr.CreatedAt,
                                           replieStatus = sr.ReplieStatus,
                                           likes = _context.CommunityInteractions.Count(i =>
                                               i.TargetType == "Reply" &&
                                               i.TargetId == sr.ReplyId &&
                                               i.InteractionsType == "Like")
                                       }).ToListAsync();

            // 修復：正確處理子回覆嵌套
            var repliesWithSub = mainReplies.Select(r => new
            {
                replyID = r.replyID,
                memberID = r.memberID,
                name = r.name,
                avatar = r.avatar,
                content = r.content,
                createdAt = r.createdAt,
                replieStatus = r.replieStatus,
                likes = r.likes,
                replies = allSubReplies
                    .Where(sr => sr.parentReplyID == r.replyID)
                    .Select(sr => new
                    {
                        replyID = sr.replyID,
                        memberID = sr.memberID,
                        name = sr.name,
                        avatar = sr.avatar,
                        content = sr.content,
                        createdAt = sr.createdAt,
                        replieStatus = sr.replieStatus,
                        likes = sr.likes
                    }).ToList()
            }).ToList();

            // 修復：查附件
            var attachments = await _context.CommunityAttachments
                .Where(a => a.PostId == id)
                .Select(a => new
                {
                    AttachmentID = a.AttachmentId,
                    PostID = a.PostId,
                    ReplyID = a.ReplyId,
                    AttachmentUrl = $"{Request.Scheme}://{Request.Host}{a.AttachmentUrl}"
                })
                .ToListAsync();

            // 使用正確的互動計數
            var result = new
            {
                postId = post.PostId,
                memberId = post.MemberId,
                boardId = post.BoardId,
                post.Title,
                post.Content,
                post.IsPinned,
                post.PostStatus,
                post.CreatedAt,
                post.UpdatedAt,
                author = postAuthor,
                likes = postLikes,
                favorites = postFavorites,
                shares = postShares,
                views = postViews,
                comments = repliesWithSub,
                attachments = attachments
            };

            return Ok(result);
        }

        // 依文章ID列表取得多篇文章
        [HttpPost("postsByIds")]
        public async Task<IActionResult> GetPostsByIds([FromBody] int[] postIds)
        {
            var posts = await _context.CommunityPosts
                .Where(p => postIds.Contains(p.PostId) && p.PostStatus == "Active")
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    id = p.PostId,
                    title = p.Title,
                    content = p.Content,
                    createdAt = p.CreatedAt,
                    memberId = p.MemberId,
                    boardID = p.BoardId
                })
                .ToListAsync();

            return Ok(posts);
        }

        // 取得用戶對文章的互動狀態
        [HttpGet("{postId}/interaction/status/{memberId}")]
        public async Task<IActionResult> GetUserInteractionStatus(int postId, int memberId)
        {
            try
            {
                // 先確認文章是否存在
                var post = await _context.CommunityPosts
                    .FirstOrDefaultAsync(p => p.PostId == postId);

                if (post == null)
                {
                    return NotFound("文章不存在");
                }

                // 查詢特定會員對特定文章的互動記錄
                var interactions = await _context.CommunityInteractions
                    .Where(i => i.TargetType == "Post"
                            && i.TargetId == postId
                            && i.MemberId == memberId)
                    .ToListAsync();

                // 記錄除錯訊息
                Console.WriteLine($"查詢互動狀態 - PostId: {postId}, MemberId: {memberId}");
                Console.WriteLine($"找到 {interactions.Count} 筆互動記錄");

                // 檢查每種互動類型
                var hasLiked = interactions.Any(i => i.InteractionsType == "Like");
                var hasFavorited = interactions.Any(i => i.InteractionsType == "Favorite");
                var hasShared = interactions.Any(i => i.InteractionsType == "Share");

                Console.WriteLine($"互動狀態 - Liked: {hasLiked}, Favorited: {hasFavorited}, Shared: {hasShared}");

                return Ok(new { hasLiked, hasFavorited, hasShared });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetUserInteractionStatus 發生錯誤: {ex.Message}");
                return StatusCode(500, "取得互動狀態時發生錯誤");
            }
        }

        // 新增主留言
        [HttpPost("{postId}/replies")]
        public async Task<IActionResult> CreateReply(int postId, [FromBody] CreateReplyDto dto)
        {
            try
            {
                // 檢查文章是否存在
                var post = await _context.CommunityPosts.FindAsync(postId);
                if (post == null)
                    return NotFound("文章不存在");

                // 檢查會員是否存在
                var member = await _context.Members.FindAsync(dto.MemberId);
                if (member == null)
                    return BadRequest("會員不存在");

                // 驗證內容
                if (string.IsNullOrWhiteSpace(dto.Content))
                    return BadRequest("留言內容不能空白");

                var reply = new CommunityReply
                {
                    PostId = postId,
                    MemberId = dto.MemberId,
                    Content = dto.Content.Trim(),
                    ParentReplyId = null, // 主留言
                    CreatedAt = DateTime.Now,
                    ReplieStatus = "Active" // 設定回覆狀態
                };

                _context.CommunityReplies.Add(reply);
                await _context.SaveChangesAsync();

                // 更新文章的 UpdatedAt
                post.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                // 簡化回傳資料
                var result = new
                {
                    replyID = reply.ReplyId, // 修正屬性名稱
                    memberID = reply.MemberId, // 修正屬性名稱
                    name = member.FName ?? "匿名",
                    avatar = member.FProfilePictureUrl,
                    content = reply.Content,
                    createdAt = reply.CreatedAt,
                    likes = 0
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateReply 錯誤: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"內部錯誤: {ex.InnerException.Message}");
                }
                return StatusCode(500, $"新增留言失敗: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // 新增回覆（回覆某則留言）
        [HttpPost("{postId}/replies/{parentReplyId}/subReplies")]
        public async Task<IActionResult> CreateSubReply(int postId, int parentReplyId, [FromBody] CreateReplyDto dto)
        {
            try
            {
                Console.WriteLine($"接收到回覆請求：postId = {postId}, parentReplyId = {parentReplyId}"); // 新增日誌

                // 檢查文章和父留言是否存在
                var post = await _context.CommunityPosts.FindAsync(postId);
                if (post == null)
                    return NotFound("文章不存在");

                var parentReply = await _context.CommunityReplies.FindAsync(parentReplyId);
                if (parentReply == null)
                {
                    Console.WriteLine($"在資料庫中找不到 parentReplyId: {parentReplyId}"); // 新增日誌
                    return NotFound("父留言不存在");
                }

                // 檢查會員是否存在
                var member = await _context.Members.FindAsync(dto.MemberId);
                if (member == null)
                    return BadRequest("會員不存在");

                // 驗證內容
                if (string.IsNullOrWhiteSpace(dto.Content))
                    return BadRequest("回覆內容不能空白");

                var reply = new CommunityReply
                {
                    PostId = postId,
                    MemberId = dto.MemberId,
                    Content = dto.Content.Trim(),
                    ParentReplyId = parentReplyId,
                    CreatedAt = DateTime.Now,
                    ReplieStatus = "Active"
                };

                _context.CommunityReplies.Add(reply);
                await _context.SaveChangesAsync();

                // 更新文章的 UpdatedAt
                post.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                // 簡化回傳資料
                var result = new
                {
                    replyID = reply.ReplyId, // 修正屬性名稱
                    memberID = reply.MemberId, // 修正屬性名稱
                    name = member.FName ?? "匿名",
                    avatar = member.FProfilePictureUrl,
                    content = reply.Content,
                    createdAt = reply.CreatedAt,
                    likes = 0
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateSubReply 錯誤: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"內部錯誤: {ex.InnerException.Message}");
                }
                return StatusCode(500, $"新增回覆失敗: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // 更新回覆內容
        [HttpPut("replies/{id}")]
        public async Task<IActionResult> UpdateReply(int id, [FromBody] UpdateReplyDto dto)
        {
            var reply = await _context.CommunityReplies.FindAsync(id);
            if (reply == null)
                return NotFound("回覆不存在");

            reply.Content = dto.Content;
            // 如果 CommunityReply 有 UpdatedAt 欄位，添加：
            // reply.UpdatedAt = DateTime.Now;

            // 更新文章的 UpdatedAt
            var post = await _context.CommunityPosts.FindAsync(reply.PostId);
            if (post != null)
            {
                post.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "回覆更新成功" });
        }

        // 更新回覆狀態
        [HttpPut("replies/{id}/status")]
        public async Task<IActionResult> UpdateReplyStatus(int id, [FromBody] UpdateStatusDto dto)
        {
            var reply = await _context.CommunityReplies.FindAsync(id);
            if (reply == null)
                return NotFound("回覆不存在");

            reply.ReplieStatus = dto.Status;  // 使用 ReplieStatus 欄位

            // 更新文章的 UpdatedAt
            var post = await _context.CommunityPosts.FindAsync(reply.PostId);
            if (post != null)
            {
                post.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "狀態更新成功" });
        }

        [HttpGet("replies/{id}")]
        public async Task<IActionResult> GetReplyById(int id)
        {
            var reply = await _context.CommunityReplies.FindAsync(id);
            if (reply == null)
                return NotFound("找不到回覆");

            // 查詢會員姓名
            var member = await _context.Members.FindAsync(reply.MemberId);

            var result = new
            {
                replyId = reply.ReplyId,
                content = reply.Content,
                postId = reply.PostId,
                memberId = reply.MemberId,
                memberName = member?.FName ?? "未知",  // 新增：會員姓名
                createdAt = reply.CreatedAt
            };

            return Ok(result);
        }


        /// 處理文章的喜歡、收藏或分享互動
        [HttpPost("{postId}/interaction")]
        public async Task<IActionResult> ToggleInteraction(int postId, [FromBody] InteractionDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 檢查文章與會員是否存在
                var post = await _context.CommunityPosts.FindAsync(postId);
                if (post == null)
                    return NotFound("文章不存在");

                var member = await _context.Members.FindAsync(dto.MemberId);
                if (member == null)
                    return BadRequest("會員不存在");

                // 檢查互動類型是否有效
                if (dto.InteractionType != "Like" && dto.InteractionType != "Favorite" && dto.InteractionType != "Share")
                {
                    return BadRequest("無效的互動類型");
                }

                // 查詢現有互動記錄
                var interaction = await _context.CommunityInteractions
                    .FirstOrDefaultAsync(i => i.TargetType == "Post"
                                            && i.TargetId == postId
                                            && i.MemberId == dto.MemberId
                                            && i.InteractionsType == dto.InteractionType);

                // 特別處理收藏功能
                if (dto.InteractionType == "Favorite")
                {
                    var favorite = await _context.CommunityFavorites
                        .FirstOrDefaultAsync(f => f.PostId == postId && f.MemberId == dto.MemberId);

                    if (interaction == null)
                    {
                        // 新增互動記錄
                        var newInteraction = new CommunityInteraction
                        {
                            MemberId = dto.MemberId,
                            TargetType = "Post",
                            TargetId = postId,
                            InteractionsType = dto.InteractionType,
                            CreatedAt = DateTime.Now
                        };
                        _context.CommunityInteractions.Add(newInteraction);

                        // 新增收藏記錄
                        if (favorite == null)
                        {
                            var newFavorite = new CommunityFavorite
                            {
                                MemberId = dto.MemberId,
                                PostId = postId,
                                CreatedAt = DateTime.Now
                            };
                            _context.CommunityFavorites.Add(newFavorite);
                        }
                    }
                    else
                    {
                        // 刪除互動記錄
                        _context.CommunityInteractions.Remove(interaction);

                        // 刪除收藏記錄
                        if (favorite != null)
                        {
                            _context.CommunityFavorites.Remove(favorite);
                        }
                    }
                }
                else
                {
                    // 其他互動類型的處理維持不變
                    if (interaction == null)
                    {
                        var newInteraction = new CommunityInteraction
                        {
                            MemberId = dto.MemberId,
                            TargetType = "Post",
                            TargetId = postId,
                            InteractionsType = dto.InteractionType,
                            CreatedAt = DateTime.Now
                        };
                        _context.CommunityInteractions.Add(newInteraction);
                    }
                    else
                    {
                        _context.CommunityInteractions.Remove(interaction);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 重新計算次數並回傳
                var newCount = await _context.CommunityInteractions
                    .CountAsync(i => i.TargetType == "Post" && i.TargetId == postId && i.InteractionsType == dto.InteractionType);

                return Ok(new { interactionType = dto.InteractionType, count = newCount });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"處理互動失敗: {ex.Message}");
            }
        }

        // 處理回覆的喜歡互動
        [HttpPost("{postId}/replies/{replyId}/like")]
        public async Task<IActionResult> ToggleReplyLike(int postId, int replyId, [FromBody] InteractionDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 檢查文章是否存在
                var post = await _context.CommunityPosts.FindAsync(postId);
                if (post == null)
                    return NotFound("文章不存在");

                // 檢查回覆是否存在
                var reply = await _context.CommunityReplies.FindAsync(replyId);
                if (reply == null)
                    return NotFound("回覆不存在");

                // 檢查會員是否存在
                var member = await _context.Members.FindAsync(dto.MemberId);
                if (member == null)
                    return BadRequest("會員不存在");

                // 查詢現有互動記錄
                var interaction = await _context.CommunityInteractions
                    .FirstOrDefaultAsync(i => i.TargetType == "Reply"
                                            && i.TargetId == replyId
                                            && i.MemberId == dto.MemberId
                                            && i.InteractionsType == "Like");

                if (interaction == null)
                {
                    // 新增喜歡記錄
                    var newInteraction = new CommunityInteraction
                    {
                        MemberId = dto.MemberId,
                        TargetType = "Reply",
                        TargetId = replyId,
                        InteractionsType = "Like",
                        CreatedAt = DateTime.Now
                    };
                    _context.CommunityInteractions.Add(newInteraction);
                }
                else
                {
                    // 移除喜歡記錄
                    _context.CommunityInteractions.Remove(interaction);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 更新文章的 UpdatedAt（因為有新的互動）
                post.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                // 計算新的喜歡數量
                var newLikeCount = await _context.CommunityInteractions
                    .CountAsync(i => i.TargetType == "Reply"
                                   && i.TargetId == replyId
                                   && i.InteractionsType == "Like");

                return Ok(new { count = newLikeCount });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"處理回覆喜歡失敗: {ex.Message}");
            }
        }

        // 取得回覆的喜歡狀態
        [HttpGet("{postId}/replies/{replyId}/like/status/{memberId}")]
        public async Task<IActionResult> GetReplyLikeStatus(int postId, int replyId, int memberId)
        {
            var hasLiked = await _context.CommunityInteractions
                .AnyAsync(i => i.TargetType == "Reply"
                              && i.TargetId == replyId
                              && i.MemberId == memberId
                              && i.InteractionsType == "Like");

            return Ok(hasLiked);
        }

        // 刪除附件
        [HttpDelete("attachments/{id}")]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                Console.WriteLine($"嘗試刪除附件 ID: {id}");

                var attachment = await _context.CommunityAttachments.FindAsync(id);
                if (attachment == null)
                {
                    Console.WriteLine($"附件 ID {id} 不存在");
                    return NotFound("附件不存在");
                }

                // 刪除實體檔案
                var filePath = Path.Combine(_env.WebRootPath, attachment.AttachmentUrl.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                {
                    try
                    {
                        System.IO.File.Delete(filePath);
                        Console.WriteLine($"刪除檔案: {filePath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"刪除檔案失敗: {filePath}, 錯誤: {ex.Message}");
                        // 繼續執行，刪除資料庫記錄
                    }
                }

                // 刪除資料庫記錄
                _context.CommunityAttachments.Remove(attachment);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                Console.WriteLine($"附件 ID {id} 刪除成功");
                return Ok(new { message = "附件刪除成功" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"刪除附件失敗: {ex.Message}");
                return StatusCode(500, new { message = "附件刪除失敗", error = ex.Message });
            }
        }
    }
}
