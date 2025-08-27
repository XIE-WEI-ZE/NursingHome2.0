using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models.Dto
{
    // 列表用：會員看到的訂單列
    public class OrderListItemDto
    {
        public long OrderId { get; set; }
        public string OrderNo { get; set; } = "";
        public DateTime OrderTime { get; set; }
        public int TotalAmount { get; set; }
        public string Status { get; set; } = "";
    }

    // 明細列
    public class OrderDetailViewDto
    {
        public long DetailId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public short Quantity { get; set; }
        public int UnitPrice { get; set; }
        public int Subtotal { get; set; }
    }

    // 單筆訂單（含主檔欄位＋明細）
    public class OrderViewDto
    {
        public long OrderId { get; set; }
        public string OrderNo { get; set; } = "";
        public DateTime OrderTime { get; set; }

        public string? BuyerName { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }

        public string? PaymentMethod { get; set; }
        public string? DeliveryMethod { get; set; }
        public string? DeliveryAddress { get; set; }

        public string? InvoiceTitle { get; set; }
        public string? InvoiceTax { get; set; }
        public string? InvoiceInMethod { get; set; }
        public string? CarrierNumber { get; set; }

        public string? Note { get; set; }
        public string? Status { get; set; }
        public int TotalAmount { get; set; }

        public List<OrderDetailViewDto> Details { get; set; } = new();
    }

    // 通用分頁包裝
    public class PagedResult<T>
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
    }
}

