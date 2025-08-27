namespace prjFinalProjectApi.Models.Dto
{
    public class OrderDetailDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public short Quantity { get; set; }
        public int UnitPrice { get; set; }
        public int Subtotal { get; set; }
    }
}
