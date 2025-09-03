namespace prjFinalProjectApi.Models
{
    public class EventRuleCount
    {
        public int ledgerId { get; set; }                 // 計數編號(PK)
        public int ruleId { get; set; }                   // 規則編號
        public int memberId { get; set; }                 // 會員編號
        public string registrationId { get; set; } = "";  // 活動報名編號(NVARCHAR20)
        public int batchIndex { get; set; }               // 輪內第幾筆(1..threshold)
        public string? CouponId { get; set; }             // 達標時產生(可空)
    }
}
