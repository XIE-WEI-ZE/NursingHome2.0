using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using prjFinalProjectApi.Models;

[ApiController]
[Route("api/EventTemplate")] // => /api/EventTemplate
public class EventTemplatesController : ControllerBase
{
    private readonly DbNursingHomeContext _db;
    public EventTemplatesController(DbNursingHomeContext db) => _db = db;

    // GET /api/EventTemplate
    // 支援：keyword（EventName/Subtitle）、categoryId、status、分頁
    [HttpGet("list")]
    public async Task<IActionResult> List()
    {
        var items = await _db.EventTemplates
            .AsNoTracking()
            .OrderBy(t => t.EventId)
            .Select(t => new
            {
                t.EventId,
                t.EventSlug,
                t.EventName,
                t.Organizer,
                t.TargetAudience,
                t.CategoryId,
                t.Status,
                t.ContactPersonId,
                t.ContactPhone,
                t.EventLocation,
                t.Quota,
                t.Description,
                t.MedicalAid,
                t.Amount,
                t.CreatedAt,
                t.CreatedBy,
                t.LastModifiedAt,
                t.LastModifiedBy,
                t.Subtitle,
                t.DurationMinutes,
                //t.CoverImageUrl,  //已前端網域抓取(預設的)
                CoverImageUrl = $"{Request.Scheme}://{Request.Host}{t.CoverImageUrl}",//這樣才會給予後端的



                // 用 EventId 串接批次
                EventBatches = _db.EventBatches
                .AsNoTracking()
                .Where(b => b.EventId == t.EventId)
                .OrderBy(b => b.BatchId)
                .Select(b => new {
                    b.BatchId,
                    b.EventId,
                    b.EventDateTimeStart,
                    b.EventDateTimeEnd,
                    b.RegistrationDateStart,
                    b.RegistrationDateEnd,
                    b.Status,
                    // 前端列表直接拿來當連結
                    CanonicalPath = $"{t.EventSlug}-{t.EventId}/b/{b.BatchId}"
                })
                .ToList()
            })
            .ToListAsync();

        return Ok(items);
    }

    //URL 用 slug，但查詢用 id 最穩
    // GET /api/EventTemplate/by-batch/{batchId}
    [HttpGet("by-batch/{batchId:int}")]
    public async Task<IActionResult> GetByBatch(int batchId)
    {
        // 先抓到該批次
        var chosen = await _db.EventBatches
            .AsNoTracking()
            .Where(b => b.BatchId == batchId)
            .Select(b => new {
                b.BatchId,
                b.EventId,
                //b.EventDateTimeStart,
                //b.EventDateTimeEnd,
                //b.RegistrationDateStart,
                //b.RegistrationDateEnd,
                //b.Status,
                //b.Organizer,
                //b.TargetAudience,
                //b.ContactPersonId,
                //b.ContactPhone,
                //b.EventLocation,
                //b.Quota,
                //b.Description,
                //b.MedicalAid,
                //b.Amount,
                //b.CreatedAt,
                //b.CreatedBy,
                //b.LastModifiedAt,
                //b.LastModifiedBy
            })
            .FirstOrDefaultAsync();

        if (chosen is null) return NotFound(new { message = "找不到指定的批次。" });

        // 取活動主檔 + 該活動的所有梯次
        var template = await _db.EventTemplates
            .AsNoTracking()
            .Where(t => t.EventId == chosen.EventId)
            .Select(t => new
            {
                t.EventId,
                t.EventSlug,
                t.EventName,
                t.Organizer,
                t.TargetAudience,
                t.CategoryId,
                t.Status,
                t.ContactPersonId,
                t.ContactPhone,
                t.EventLocation,
                t.Quota,
                t.Description,
                t.MedicalAid,
                t.Amount,
                t.CreatedAt,
                t.CreatedBy,
                t.LastModifiedAt,
                t.LastModifiedBy,
                t.Subtitle,
                t.DurationMinutes,
                CoverImageUrl = $"{Request.Scheme}://{Request.Host}{t.CoverImageUrl}",
                EventBatches = _db.EventBatches
                    .AsNoTracking()
                    .Where(b => b.EventId == t.EventId)
                    .OrderBy(b => b.EventDateTimeStart)
                    .Select(b => new {
                        b.BatchId,
                        b.EventId,
                        b.EventDateTimeStart,
                        b.EventDateTimeEnd,
                        b.RegistrationDateStart,
                        b.RegistrationDateEnd,
                        b.Status,
                        b.Organizer,
                        b.TargetAudience,
                        b.ContactPersonId,
                        b.ContactPhone,
                        b.EventLocation,
                        b.Quota,
                        b.Description,
                        b.MedicalAid,
                        b.Amount,
                        b.CreatedAt,
                        b.CreatedBy,
                        b.LastModifiedAt,
                        b.LastModifiedBy
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        if (template is null) return NotFound();
        var canonicalPath = $"{template.EventSlug}-{template.EventId}/b/{chosen.BatchId}";

        // 合併回傳（多一個 SelectedBatch，方便前端直接用）
        return Ok(new
        {
            template.EventId,
            template.EventSlug,
            template.EventName,
            template.Organizer,
            template.TargetAudience,
            template.CategoryId,
            template.Status,
            template.ContactPersonId,
            template.ContactPhone,
            template.EventLocation,
            template.Quota,
            template.Description,
            template.MedicalAid,
            template.Amount,
            template.CreatedAt,
            template.CreatedBy,
            template.LastModifiedAt,
            template.LastModifiedBy,
            template.Subtitle,
            template.DurationMinutes,
            template.CoverImageUrl,
            template.EventBatches,
        });
    }


}
