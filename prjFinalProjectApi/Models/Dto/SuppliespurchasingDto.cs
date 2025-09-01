namespace prjFinalProjectApi.Models.Dto
{
    public class SuppliespurchasingDto
    {
        public int SuppliesPurchasingOrderId { get; set; }

        public int? SuppliesSupplierId { get; set; }

        public DateOnly? ArrivalDate { get; set; }

        public int SuppliesPurchasingOrderDetailId { get; set; }

        public int? SuppliesProductId { get; set; }

        public string? SuppliesProductName { get; set; }

        public int? QuantityIn { get; set; }

        public DateOnly? ExpiryDate { get; set; }

        public string? SuppliesSupplierName { get; set; }

        public int SuppliesCategoryId { get; set; }

        public string? SuppliesCategoryName { get; set; }
    }

    public class SuppliesPurchasingOrderDto
    {
        public int? SuppliesSupplierId { get; set; }

        public DateOnly? ArrivalDate { get; set; }
        public List<SuppliesPurchasingOrderDetailDto> Details { get; set; } = new();
    }

    public class SuppliesPurchasingOrderDetailDto
    {
        public int? SuppliesPurchasingOrderId { get; set; }

        public int? SuppliesProductId { get; set; }

        public int? QuantityIn { get; set; }

        public DateOnly? ExpiryDate { get; set; }
    }
}
