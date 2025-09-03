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

        public int? SuppliesCategoryId { get; set; }

        public string? SuppliesCategoryName { get; set; }

        public int? SuppliesSupplierId { get; set; }
        
        public string? SuppliesSupplierName { get; set; }
    }
    //public class SuppliesSalesOrderDto
    //{
    //    public DateOnly? OrderDate { get; set; }
    //    public string? CustomerName { get; set; }
    //    public DateOnly? ReceivedDate { get; set; }
    //    public string? OrderStatus { get; set; }
    //    public List<SuppliesSalesOrderDetailDto> Details { get; set; } = new();
    //}


    //public class SuppliesSalesOrderDetailDto
    //{
    //    public int? SuppliesProductId { get; set; }
    //    public int? QuantityOfSales { get; set; }
    //    public DateOnly? ExpiryDate { get; set; }
    //}
    public class SuppliesSalesOrderDto
    {
        public DateTime? OrderDate { get; set; }
        public string? CustomerName { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public string? OrderStatus { get; set; }
        public List<SuppliesSalesOrderDetailDto> Details { get; set; } = new();
    }

    public class SuppliesSalesOrderDetailDto
    {
        public int? SuppliesProductId { get; set; }
        public int? QuantityOfSales { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    public class UpdateStatusDto
    {
        public string Status { get; set; }
    }
}
