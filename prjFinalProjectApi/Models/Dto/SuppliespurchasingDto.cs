namespace prjFinalProjectApi.Models.Dto
{
    public class SuppliespurchasingDto
    {
        public int SuppliesPurchasingOrderID { get; set; }

        public int? SuppliesSupplierID { get; set; }

        public DateOnly? ArrivalDate { get; set; }

        public int SuppliesPurchasingOrderDetailID { get; set; }

        public int? SuppliesProductID { get; set; }

        public int? QuantityIn { get; set; }

        public DateOnly? ExpiryDate { get; set; }
    }
}
