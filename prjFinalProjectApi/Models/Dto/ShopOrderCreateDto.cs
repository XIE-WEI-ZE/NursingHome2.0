using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models.Dto
{
    public class OrderCreateDetailDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public short Quantity { get; set; }
        public int UnitPrice { get; set; }
        // 不用 Discount；Subtotal 由後端重算
    }

    public class OrderCreateDto
    {
        public int MemberId { get; set; }

        [Required] public string BuyerName { get; set; } = string.Empty;
        [Required] public string ReceiverName { get; set; } = string.Empty;

        [Required, RegularExpression(@"^09\d{8}$", ErrorMessage = "手機格式不正確")]
        public string ReceiverPhone { get; set; } = string.Empty;

        [Required] public string PaymentMethod { get; set; } = string.Empty;
        [Required] public string DeliveryMethod { get; set; } = string.Empty;
        [Required] public string DeliveryAddress { get; set; } = string.Empty;

        // 發票資料
        public string InvoiceType { get; set; } = string.Empty; // 對應 DB: InvoiceInMethod
        public string? CarrierNumber { get; set; }
        public string? InvoiceTitle { get; set; }
        public string? InvoiceTax { get; set; }

        public string? Note { get; set; }

        [MinLength(1)]
        public List<OrderCreateDetailDto> OrderDetails { get; set; } = new();
        // 前端送的小計+運費（與 checkout.component 的 totalAmount 對齊）
        [Range(0, int.MaxValue)]
        public int TotalAmount { get; set; }
    }
}
