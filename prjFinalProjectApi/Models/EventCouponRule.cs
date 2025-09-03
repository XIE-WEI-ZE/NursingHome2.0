using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models
{
    public class EventCouponRule
    {
        [Key]
        public int ruleId { get; set; }                 // 規則編號(PK)
        public string ruleName { get; set; } = "";      // 規則名稱
        public decimal amount { get; set; }             // 金額
        public int status { get; set; }                 // 0=停用,1=啟用
        public DateTime validFrom { get; set; }         // 規則開始
        public DateTime validTo { get; set; }           // 規則結束
        public DateTime createdAt { get; set; }         // 建立時間
        public int? createdBy { get; set; }             // 建立人
    }
}
