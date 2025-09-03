namespace prjFinalProjectApi.Models.Dto
{
    public class EventCouponDto 
    {
        public int ruleId { get; set; }
        public string ruleName { get; set; } = "";
        public decimal amount { get; set; }
        public int status { get; set; }
        public DateTime validFrom { get; set; }
        public DateTime validTo { get; set; }
        public int isUsed { get; set; } = 0;
    }
}
