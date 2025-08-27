using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;

namespace prjFinalProjectApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommunityBoardsController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        private readonly IWebHostEnvironment _env;

        public CommunityBoardsController(DbNursingHomeContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // 取得所有看板
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CommunityBoard>>> GetBoards()
        {
            return Ok(await _context.CommunityBoards.ToListAsync());
        }

        // 取得單一看板
        [HttpGet("{id}")]
        public async Task<ActionResult<CommunityBoard>> GetBoard(int id)
        {
            var board = await _context.CommunityBoards.FindAsync(id);
            if (board == null) return NotFound();
            return Ok(board);
        }

        // 新增看板
        [HttpPost]
        public async Task<ActionResult<CommunityBoard>> CreateBoard([FromForm] CommunityBoardDto boardDto)
        {
            var normalizedName = boardDto.BoardName?.Trim().ToLower();

            // 檢查名稱是否已存在 (避免大小寫/空白差異)
            if (await _context.CommunityBoards
                .AnyAsync(b => b.BoardName.Trim().ToLower() == normalizedName))
            {
                return Conflict(new { message = $"看板名稱 {boardDto.BoardName} 已存在" });
            }

            var board = new CommunityBoard
            {
                BoardName = boardDto.BoardName.Trim(),
                BoardDescription = boardDto.BoardDescription?.Trim(),
                BoardStatus = boardDto.BoardStatus,
                ModeratorId = boardDto.ModeratorId,
                CreatedAt = DateTime.Now
            };

            _context.CommunityBoards.Add(board);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx &&
                                              (sqlEx.Number == 2627 || sqlEx.Number == 2601))
            {
                // 2627 / 2601: UNIQUE KEY 違反
                return Conflict(new { message = $"看板名稱 {boardDto.BoardName} 已存在 (並發錯誤)" });
            }

            // 如果有上傳圖片
            if (boardDto.BoardImage != null)
            {
                var extension = Path.GetExtension(boardDto.BoardImage.FileName);
                var fileName = $"board-{board.BoardId}-{Guid.NewGuid()}{extension}";
                var folder = Path.Combine(_env.WebRootPath, "images/community/board");
                Directory.CreateDirectory(folder);

                var path = Path.Combine(folder, fileName);
                using var stream = new FileStream(path, FileMode.Create);
                await boardDto.BoardImage.CopyToAsync(stream);

                board.BoardUrl = $"/images/community/board/{fileName}";
                _context.Entry(board).State = EntityState.Modified;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx &&
                                                  (sqlEx.Number == 2627 || sqlEx.Number == 2601))
                {
                    return Conflict(new { message = $"圖片儲存時發生唯一鍵衝突" });
                }
            }

            return CreatedAtAction(nameof(GetBoard), new { id = board.BoardId }, board);
        }


        // 更新看板
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateBoard(int id, [FromForm] IFormCollection form)
        {
            var board = await _context.CommunityBoards.FindAsync(id);
            if (board == null) return NotFound();

            // 更新基本欄位
            board.BoardName = form["boardName"];
            board.BoardDescription = form["boardDescription"];
            board.BoardStatus = form["boardStatus"];

            // 處理圖片
            if (form.Files.Count > 0)
            {
                var file = form.Files["boardImage"];
                if (file != null && file.Length > 0)
                {
                    var oldImagePath = board.BoardUrl;

                    // 產生新檔名
                    var newFileName = $"board-{board.BoardId}-{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                    var savePath = Path.Combine(_env.WebRootPath, "images", "community", "board", newFileName);

                    // 儲存新圖片
                    using (var stream = new FileStream(savePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // 更新資料庫路徑
                    board.BoardUrl = $"/images/community/board/{newFileName}";

                    // 刪除舊圖片（如果不是預設圖）
                    if (!string.IsNullOrEmpty(oldImagePath) && !oldImagePath.EndsWith("default.png"))
                    {
                        var oldFilePhysicalPath = Path.Combine(_env.WebRootPath, oldImagePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                        if (System.IO.File.Exists(oldFilePhysicalPath))
                        {
                            System.IO.File.Delete(oldFilePhysicalPath);
                        }
                    }
                }
            }

            _context.Entry(board).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return NoContent();
        }



        // 切換看板狀態
        [HttpPut("status/{id}")]
        public async Task<IActionResult> ToggleBoardStatus(int id, [FromBody] BoardStatusDto dto)
        {
            var board = await _context.CommunityBoards.FindAsync(id);
            if (board == null) return NotFound();

            board.BoardStatus = dto.BoardStatus; // active 或 inactive
            _context.Entry(board).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // 停用看板
        [HttpPut("deactivate/{id}")]
        public async Task<IActionResult> DeactivateBoard(int id)
        {
            var board = await _context.CommunityBoards.FindAsync(id);
            if (board == null) return NotFound();

            board.BoardStatus = "inactive";
            _context.Entry(board).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
