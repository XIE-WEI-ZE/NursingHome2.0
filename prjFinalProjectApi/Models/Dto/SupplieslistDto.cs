namespace prjFinalProjectApi.Models.Dto
{
    public class SupplieslistDto
    {
        public int SuppliesProductID { get; set; }

        public string? SuppliesProductName { get; set; }

        public string? QuantityPerUnit { get; set; }

        public int? UnitsInStock { get; set; }

        public int? PricePerUnit { get; set; }

        public int? SupplierId { get; set; }

        public string? SuppliesSupplierName { get; set; }

        public int? SuppliesCategoryId { get; set; }

        public string? SuppliesCategoryName { get; set; }

        public bool? Exist { get; set; }
    }

    public class SuppliesImportResult
    {
        public int Inserted { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class ExcelImportRequest
    {
        public IFormFile File { get; set; } = default!;
    }
}
