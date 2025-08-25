namespace prjFinalProjectApi.Models.Dto
{
    public class ECPayRequestDto
    {
        public string MerchantID { get; set; } = "";
        public string MerchantTradeNo { get; set; } = "";
        public string MerchantTradeDate { get; set; } = "";
        public int TotalAmount { get; set; }
        public string TradeDesc { get; set; } = "OrderPayment";
        public string ItemName { get; set; } = "";
        public string ReturnURL { get; set; } = "";
        public string? ClientBackURL { get; set; } = "";
        public string ChoosePayment { get; set; } = "Credit";
        public string EncryptType { get; set; } = "1";
        public string CheckMacValue { get; set; } = "";
        public string PaymentType { get; set; } = "aio";
    }
}
