namespace prjFinalProjectApi.Models.Dto
{
    // 前端送進來的欄位（白名單）
    public class RegistrationCreateDto
    {
        public int EventBatchId { get; set; }                 // 必填
        public int MemberId { get; set; }                     // 必填
        public decimal? AmountDue { get; set; }               // 可為空 (DECIMAL(10,0))
        public DateTime? RegistrationDateTime { get; set; }   // 不送就用 Now
        public int CurrentStatus { get; set; } = 1;           // 1=報名成功（依系統）
        public string? InternalRemarks { get; set; }          // 可為空(<=500字)
        public PaymentDto? Payment { get; set; }   // 付款資訊：可為 null
    }
    public class PaymentDto
    {
        public string PaymentMethod { get; set; } = default!;  // CASH / CARD / LINEPAY...
        public string InvoiceType { get; set; } = default!;    // 二聯式 / 三聯式 / 電子發票
        public string? InvoiceTitle { get; set; }
        public string? TaxId { get; set; }
        public string? EInvoiceCarrier { get; set; }
        public string? PaymentItem { get; set; }//繳費項目
        public decimal PaymentAmount { get; set; }//繳費金額
        public string? note { get; set; }//交易編號

        // 需要的話還可加：PaymentItem、PaymentAmount、TransactionId、LinePayTime...
    }

    // 後端回傳給前端
    public class RegistrationResDto
    {
        public int RegistrationId { get; set; }
        public string RegistrationNum { get; set; } = string.Empty; // REGyyyyMMddnnn
        public bool RequiresPayment { get; set; } // 供前端判斷是否顯示「去付款」
    }

    //取消報名 前台給予欄位資料
    public class CancelRegistrationDto
    {
        public int MemberId { get; set; }
        public int EventBatchId { get; set; }
        public string? Reason { get; set; } // 可選：取消原因，前台可傳
    }
}
