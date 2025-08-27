using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using prjFinalProjectApi.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using prjFinalProjectApi.Models.Dto;
using System.IO;

namespace prjFinalProjectApi.Controllers
{
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
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    PostStatus = "Active"
                };

                _context.CommunityPosts.Add(post);
                await _context.SaveChangesAsync();

                // 處理附件
                if (dto.Attachments != null && dto.Attachments.Count > 0)
                {
                    var uploadsFolder = Path.Combine(_env.WebRootPath, "images", "communit", "post");
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
                            AttachmentUrl = $"/images/communit/post/{uniqueFileName}"
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

        // 取得所有文章
        [HttpGet]
        public async Task<IActionResult> GetAllPosts()
        {
            var posts = await _context.CommunityPosts
                .Where(p => p.PostStatus == "Active")
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            return Ok(posts);
        }

        // 依看板取得文章
        [HttpGet("board/{boardID}")]
        public async Task<IActionResult> GetPostsByBoard(int boardID)
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
                                       r.ReplieStatus == "Active")
                               })
                              .OrderByDescending(p => p.CreatedAt)
                              .ToListAsync();

            if (posts == null || posts.Count == 0)
                return NotFound("該看板尚無文章");

            return Ok(posts);
        }

        // 取得單篇文章（包含留言、回覆和附件）
        [HttpGet("{id}")]
        public async Task<IActionResult> GetPost(int id)
        {
            var post = await _context.CommunityPosts.FindAsync(id);
            if (post == null)
                return NotFound("文章不存在");

            // 查詢文章作者資訊
            var postAuthor = await (from p in _context.CommunityPosts
                                    join m in _context.Members on p.MemberId equals m.FMemberId into memberGroup
                                    from member in memberGroup.DefaultIfEmpty()
                                    where p.PostId == id
                                    select member != null ? member.FName : "匿名").FirstOrDefaultAsync();

            // 正確計算互動次數
            var postLikes = await _context.CommunityInteractions
                .CountAsync(i => i.TargetType == "Post" && i.TargetId == id && i.InteractionsType == "Like");

            var postFavorites = await _context.CommunityInteractions
                .CountAsync(i => i.TargetType == "Post" && i.TargetId == id && i.InteractionsType == "Favorite");

            var postShares = await _context.CommunityInteractions
                .CountAsync(i => i.TargetType == "Post" && i.TargetId == id && i.InteractionsType == "Share");

            // 查詢主留言
            var mainReplies = await (from r in _context.CommunityReplies
                                     join m in _context.Members on r.MemberId equals m.FMemberId into memberGroup
                                     from member in memberGroup.DefaultIfEmpty()
                                     where r.PostId == id && r.ParentReplyId == null && r.ReplieStatus == "Active"
                                     orderby r.CreatedAt
                                     select new
                                     {
                                         replyID = r.ReplyId,
                                         memberID = r.MemberId,
                                         name = member != null ? member.FName : "匿名",
                                         avatar = member != null ? member.FProfilePictureUrl : null,
                                         content = r.Content,
                                         createdAt = r.CreatedAt,
                                         likes = _context.CommunityInteractions.Count(i =>
                                             i.TargetType == "Reply" &&
                                             i.TargetId == r.ReplyId &&
                                             i.InteractionsType == "Like")
                                     }).ToListAsync();

            // 查詢所有子回覆
            var allSubReplies = await (from sr in _context.CommunityReplies
                                       join m in _context.Members on sr.MemberId equals m.FMemberId into memberGroup
                                       from member in memberGroup.DefaultIfEmpty()
                                       where sr.PostId == id && sr.ParentReplyId != null && sr.ReplieStatus == "Active"
                                       orderby sr.CreatedAt
                                       select new
                                       {
                                           replyID = sr.ReplyId,
                                           memberID = sr.MemberId,
                                           parentReplyID = sr.ParentReplyId,
                                           name = member != null ? member.FName : "匿名",
                                           avatar = member != null ? member.FProfilePictureUrl : null,
                                           content = sr.Content,
                                           createdAt = sr.CreatedAt,
                                           likes = _context.CommunityInteractions.Count(i =>
                                               i.TargetType == "Reply" &&
                                               i.TargetId == sr.ReplyId &&
                                               i.InteractionsType == "Like")
                                       }).ToListAsync();

            // 正確地將子回覆嵌套到主留言中
            var repliesWithSub = mainReplies.Select(r => new
            {
                replyID = r.replyID,
                memberID = r.memberID,
                name = r.name,
                avatar = r.avatar,
                content = r.content,
                createdAt = r.createdAt,
                likes = r.likes,
                replies = allSubReplies.Where(sr => sr.parentReplyID == r.replyID)
                                      .Select(sr => new
                                      {
                                          replyID = sr.replyID,
                                          memberID = sr.memberID,
                                          name = sr.name,
                                          avatar = sr.avatar,
                                          content = sr.content,
                                          createdAt = sr.createdAt,
                                          likes = sr.likes
                                      }).ToList()
            }).ToList();

            // 查附件
            var attachments = await _context.CommunityAttachments
                .Where(a => a.PostId == id)
                .Select(a => $"{Request.Scheme}://{Request.Host}{a.AttachmentUrl}")
                .ToListAsync();

            // 修正：使用正確的互動計數
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
                author = postAuthor ?? "匿名",
                likes = postLikes,        // 使用實際計算的數量
                favorites = postFavorites, // 使用實際計算的數量
                shares = postShares,      // 使用實際計算的數量
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
                    CreatedAt = DateTime.UtcNow,
                    ReplieStatus = "Active" // 設定回覆狀態
                };

                _context.CommunityReplies.Add(reply);
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
                    CreatedAt = DateTime.UtcNow,
                    ReplieStatus = "Active"
                };

                _context.CommunityReplies.Add(reply);
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
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.CommunityInteractions.Add(newInteraction);

                        // 新增收藏記錄
                        if (favorite == null)
                        {
                            var newFavorite = new CommunityFavorite
                            {
                                MemberId = dto.MemberId,
                                PostId = postId,
                                CreatedAt = DateTime.UtcNow
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
                            CreatedAt = DateTime.UtcNow
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
                        CreatedAt = DateTime.UtcNow
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
    }
}
