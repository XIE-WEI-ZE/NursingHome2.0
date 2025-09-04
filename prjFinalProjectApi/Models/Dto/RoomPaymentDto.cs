namespace prjFinalProjectApi.Models.Dto
{
    public class RoomPaymentDto
    {
        public int OccupancyId { get; set; }
        public int Amount { get; set; }
        public string PaypalOrderId { get; set; }
    }

    public class PaymentHistoryDto
    {
        public int MemberId { get; set; }
        public string Name { get; set; } = "未知";
        public int BillingAmount { get; set; } // 調整為 int，匹配 FBillingAmount
        public string BillingDate { get; set; } = "無";
        public PaymentHistory[] PaymentHistory { get; set; } = Array.Empty<PaymentHistory>();
    }

    public class PaymentHistory
    {
        public int FPaymentId { get; set; }
        public int FOccupancyId { get; set; }
        public int FBillingAmount { get; set; } // 調整為 int，匹配 FBillingAmount
        public DateTime? FBillingDate { get; set; }
        public string FPaymentMethod { get; set; } = "未知";
        public bool FBillingStatus { get; set; }
        public string FPaypalOrderId { get; set; } = "無";
    }
}