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
        public int PaymentId { get; set; }
        public int OccupancyId { get; set; }
        public int MemberId { get; set; }
        public string Name { get; set; } = "未知";
        public string Phone { get; set; } = "無";
        public string Email { get; set; } = "無";
        public bool? ResidesInCareHomeStatus { get; set; }
        public bool BillingStatus { get; set; }
        public int BillingAmount { get; set; }
        public string BillingDate { get; set; } = "無";
        public string PaymentMethod { get; set; } = "未知";
        public string BillingStatusText { get; set; } = "未付款"; // 新增
        public string PaypalOrderId { get; set; } = "無"; // 新增
        public string CheckInDate { get; set; } = "無"; // 新增
        public string CheckOutDate { get; set; } = "無"; // 新增
        public PaymentHistory[] PaymentHistory { get; set; } = Array.Empty<PaymentHistory>();
    }

    public class PaymentHistory
    {
        public int FPaymentId { get; set; }
        public int FOccupancyId { get; set; }
        public int FBillingAmount { get; set; }
        public DateTime? FBillingDate { get; set; }
        public string FPaymentMethod { get; set; } = "未知";
        public bool FBillingStatus { get; set; }
        public string FPaypalOrderId { get; set; } = "無";
    }
}