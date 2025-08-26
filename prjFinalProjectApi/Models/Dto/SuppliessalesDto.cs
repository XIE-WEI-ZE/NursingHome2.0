namespace prjFinalProjectApi.Models.Dto
{
    public class SuppliessalesDto
    {
        public int SuppliesSalesOrderId { get; set; }

        public DateOnly? OrderDate { get; set; }

        public string? CustomerName { get; set; }

        public DateOnly? ReceivedDate { get; set; }

        public string? OrderStatus { get; set; }

        public int SuppliesSalesOrderDetailId { get; set; }

        public int? SuppliesProductId { get; set; }

        public int? QuantityOfSales { get; set; }

        public DateOnly? ExpiryDate { get; set; }

        public string? SuppliesProductName { get; set; }
    }
}
