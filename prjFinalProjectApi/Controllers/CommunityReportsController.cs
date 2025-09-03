using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CommunityReportsController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public CommunityReportsController(DbNursingHomeContext context)
        {
            _context = context;
        }

        // 前端送進來的 DTO
        public class ReportRequest
        {
            public string ContentType { get; set; } = null!; // "Post" or "Reply"
            public int ContentId { get; set; }
            public int ReporterMemberId { get; set; }
            public string ReasonType { get; set; } = null!;
        }

        // 處理檢舉的 DTO
        public class HandleReportRequest
        {
            public string NewStatus { get; set; } = null!; // "通過" 或 "駁回"
            public int EmployeeId { get; set; }
            public string? Result { get; set; }
        }

        // 後台用：列表項目 DTO
        public class ReportListItem
        {
            public int ReportId { get; set; }
            public string? ReportStatus { get; set; }
            public DateTime? ReportedAt { get; set; }
            public string? ReasonType { get; set; }
            public string? ReportedContentType { get; set; }
            public int? ContentId { get; set; } // PostId or ReplyId
            public int? ReporterMemberId { get; set; }
            public int? TargetMemberId { get; set; }
        }

        // 後台用：詳細 DTO
        public class ReportDetailDto
        {
            public int ReportId { get; set; }
            public string? ReportStatus { get; set; }
            public DateTime? ReportedAt { get; set; }
            public string? ReasonType { get; set; }
            public int? ReporterMemberId { get; set; }
            public int? TargetMemberId { get; set; }
            public int? ReportedContentId { get; set; }
            public string? ReportedContentType { get; set; }
            public int? PostId { get; set; }
            public int? ReplyId { get; set; }
            public int? HandledEmployeeId { get; set; }
            public DateTime? HandledAt { get; set; }
            public string? Result { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SubmitReport([FromBody] ReportRequest request)
        {
            if (request == null)
                return BadRequest("請提供檢舉資料");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                int reportedContentId;
                int targetMemberId = 0;

                // 檢查 CommunityReportedContent 是否已有紀錄
                var reportedContent = await _context.CommunityReportedContents
                    .FirstOrDefaultAsync(c =>
                        c.ReportedContentType == request.ContentType &&
                        ((request.ContentType == "Post" && c.PostId == request.ContentId) ||
                         (request.ContentType == "Reply" && c.ReplyId == request.ContentId)));

                if (reportedContent == null)
                {
                    reportedContent = new CommunityReportedContent
                    {
                        ReportedContentType = request.ContentType,
                        PostId = request.ContentType == "Post" ? request.ContentId : null,
                        ReplyId = request.ContentType == "Reply" ? request.ContentId : null,
                        CreatedAt = DateTime.Now
                    };

                    _context.CommunityReportedContents.Add(reportedContent);
                    await _context.SaveChangesAsync();
                }

                reportedContentId = reportedContent.ReportedContentId;

                // 查找被檢舉內容作者
                if (request.ContentType == "Post")
                {
                    var post = await _context.CommunityPosts.FindAsync(request.ContentId);
                    targetMemberId = post?.MemberId ?? 0;
                }
                else if (request.ContentType == "Reply")
                {
                    var reply = await _context.CommunityReplies.FindAsync(request.ContentId);
                    targetMemberId = reply?.MemberId ?? 0;
                }

                // 插入 CommunityReport
                var report = new CommunityReport
                {
                    ReportMemberId = request.ReporterMemberId,
                    ReportedContentId = reportedContentId,
                    TargetType = "Content",
                    TargetMemberId = targetMemberId,
                    ReportedAt = DateTime.Now,
                    ReportStatus = "待處理",
                    ReasonType = request.ReasonType
                };

                _context.CommunityReports.Add(report);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return Ok(new { message = "檢舉已提交成功" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"檢舉失敗: {ex.Message}");
            }
        }

        [HttpPut("{id}/handle")]
        [Authorize(Policy = "EmployeeCookieOnly")]
        public async Task<IActionResult> HandleReport(int id, [FromBody] HandleReportRequest request)
        {
            var report = await _context.CommunityReports.FindAsync(id);
            if (report == null)
                return NotFound("找不到檢舉");

            report.ReportStatus = request.NewStatus; // "通過" 或 "駁回"
            report.HandledEmployeeId = request.EmployeeId;
            report.HandledAt = DateTime.Now;
            report.Result = request.Result; // 備註，例如「已刪文」「警告會員」

            await _context.SaveChangesAsync();

            return Ok(new { message = "檢舉已處理完成" });
        }

        // 新增：後台取得檢舉列表（支援篩選與分頁）
        // 範例: GET /api/CommunityReports?status=待處理&contentType=Post&page=1&pageSize=20
        [HttpGet]
        [Authorize(Policy = "EmployeeCookieOnly")]
        public async Task<IActionResult> GetReports(
            [FromQuery] string? status,
            [FromQuery] string? contentType,
            [FromQuery] int? reporterMemberId,
            [FromQuery] int? targetMemberId,
            [FromQuery] string? reasonType,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;

            var query = from r in _context.CommunityReports
                        join rc in _context.CommunityReportedContents on r.ReportedContentId equals rc.ReportedContentId
                        select new { r, rc };

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.r.ReportStatus == status);

            if (!string.IsNullOrWhiteSpace(contentType))
                query = query.Where(x => x.rc.ReportedContentType == contentType);

            if (reporterMemberId.HasValue)
                query = query.Where(x => x.r.ReportMemberId == reporterMemberId.Value);

            if (targetMemberId.HasValue)
                query = query.Where(x => x.r.TargetMemberId == targetMemberId.Value);

            if (!string.IsNullOrWhiteSpace(reasonType))
                query = query.Where(x => x.r.ReasonType == reasonType);

            if (fromDate.HasValue)
                query = query.Where(x => x.r.ReportedAt >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(x => x.r.ReportedAt <= toDate.Value);

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(x => x.r.ReportedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new ReportListItem
                {
                    ReportId = x.r.ReportId,
                    ReportStatus = x.r.ReportStatus,
                    ReportedAt = x.r.ReportedAt,
                    ReasonType = x.r.ReasonType,
                    ReportedContentType = x.rc.ReportedContentType,
                    ContentId = x.rc.PostId ?? x.rc.ReplyId,
                    ReporterMemberId = x.r.ReportMemberId,
                    TargetMemberId = x.r.TargetMemberId
                })
                .ToListAsync();

            return Ok(new
            {
                total,
                page,
                pageSize,
                items
            });
        }

        // 新增：後台取得單一檢舉詳情
        // GET /api/CommunityReports/{id}
        [HttpGet("{id}")]
        [Authorize(Policy = "EmployeeCookieOnly")]
        public async Task<IActionResult> GetReportById(int id)
        {
            var report = await _context.CommunityReports.FindAsync(id);
            if (report == null)
                return NotFound("找不到檢舉");

            var rc = await _context.CommunityReportedContents.FindAsync(report.ReportedContentId);

            var detail = new ReportDetailDto
            {
                ReportId = report.ReportId,
                ReportStatus = report.ReportStatus,
                ReportedAt = report.ReportedAt,
                ReasonType = report.ReasonType,
                ReporterMemberId = report.ReportMemberId,
                TargetMemberId = report.TargetMemberId,
                ReportedContentId = report.ReportedContentId,
                ReportedContentType = rc?.ReportedContentType,
                PostId = rc?.PostId,
                ReplyId = rc?.ReplyId,
                HandledEmployeeId = report.HandledEmployeeId,
                HandledAt = report.HandledAt,
                Result = report.Result
            };

            // 新增：如果檢舉類型為 "Reply"，從 CommunityReplies 表中取得 PostId
            if (rc?.ReportedContentType == "Reply" && rc.ReplyId.HasValue)
            {
                var reply = await _context.CommunityReplies.FindAsync(rc.ReplyId.Value);
                detail.PostId = reply?.PostId;
            }

            return Ok(detail);
        }

    }
}
