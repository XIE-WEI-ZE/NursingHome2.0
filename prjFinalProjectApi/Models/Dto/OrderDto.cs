using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models.Dto
{
    public class OrderDto
    {
        [Required(ErrorMessage = "訂購人姓名必填")]
        public string BuyerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "收件人姓名必填")]
        public string ReceiverName { get; set; } = string.Empty;

        [Required(ErrorMessage = "收件人電話必填")]
        [RegularExpression(@"^09\d{8}$", ErrorMessage = "手機格式不正確 (09 開頭，共 10 碼)")]
        public string ReceiverPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "付款方式必填")]
        public string PaymentMethod { get; set; } = string.Empty;

        [Required(ErrorMessage = "配送方式必填")]
        public string DeliveryMethod { get; set; } = string.Empty;

        [Required(ErrorMessage = "送貨地址必填")]
        public string DeliveryAddress { get; set; } = string.Empty;
        public string InvoiceType { get; set; } = string.Empty;
        public string? InvoiceTitle { get; set; }
        public string? InvoiceTax { get; set; }
        public string? CarrierNumber { get; set; }

        // 備註（非必填）
        public string? Note { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "總金額必須大於 0")]
        public int TotalAmount { get; set; }

        [MinLength(1, ErrorMessage = "訂單必須至少包含 1 個商品")]
        public List<OrderDetailDto> OrderDetails { get; set; } = new();
    }
}
