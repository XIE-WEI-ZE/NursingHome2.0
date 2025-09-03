using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;

namespace prjFinalProjectApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]

    public class EventCouponController : ControllerBase
    {
        private readonly DbNursingHomeContext _db;

        public EventCouponController(DbNursingHomeContext db)
        {
            _db = db;
        }

        // GET api/EventCoupon
        [HttpGet("list/{userId}")]
        public async Task<ActionResult<IEnumerable<EventCouponDto>>> GetCoupons(int userId)
        {
            // 撈出所有票券規則
            var rules = await _db.EventCouponRules.ToListAsync();

            // 撈出該使用者的 RegistrationDetails（裡面存有 InternalRemarks = 使用過的 couponRuleId）
            var usedCoupons = await _db.RegistrationDetails
                .Where(r => r.MemberId == userId && r.InternalRemarks != null)
                .Select(r => r.InternalRemarks) // 假設 InternalRemarks 裡直接存 couponRuleId
                .ToListAsync();

            // 組合結果
            var result = rules.Select(r => new EventCouponDto
            {
                ruleId = r.ruleId,
                ruleName = r.ruleName,
                amount = r.amount,
                status = r.status,
                validFrom = r.validFrom,
                validTo = r.validTo,
                // 如果 InternalRemarks 有包含這張票券的 ruleId，則代表已經用過
                isUsed = usedCoupons.Contains(r.ruleId.ToString()) ? 1 : 0
            });

            return Ok(result);

        }
    }
}
