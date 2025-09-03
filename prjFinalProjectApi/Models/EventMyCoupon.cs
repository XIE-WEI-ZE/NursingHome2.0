using Microsoft.AspNetCore.Mvc;

namespace prjFinalProjectApi.Models
{
    public class EventMyCoupon 
    {
        public string CouponId { get; set; } = string.Empty;   // 票券編號
        public int MemberId { get; set; }                      // 會員編號
        public DateTime AcquiredAt { get; set; }               // 獲得時間
        public DateTime ExpireAt { get; set; }                 // 到期時間
        public int Status { get; set; }                        // 0=未使用, 1=已使用
        public string? RegistrationNum { get; set; }           // 使用的訂單編號（可空）
    }
}
