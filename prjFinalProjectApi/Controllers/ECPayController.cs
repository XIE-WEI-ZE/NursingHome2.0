using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using prjFinalProjectApi.Models.Dto;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ECPayController : ControllerBase
    {
        private readonly IConfiguration _config;

        public ECPayController(IConfiguration config)
        {
            _config = config;
        }

        [HttpPost("CreateOrder")]
        public IActionResult CreateOrder([FromBody] ECPayRequestDto dto)
        {
            dto.MerchantID = _config["ECPay:MerchantID"]!;
            dto.MerchantTradeDate = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            dto.ReturnURL = _config["ECPay:ReturnURL"]!;

            // 若前端有帶，就用前端的；否則才用 appsettings 中預設的
            dto.ClientBackURL = string.IsNullOrWhiteSpace(dto.ClientBackURL)
                ? _config["ECPay:ClientBackURL"]!
                : dto.ClientBackURL;

            dto.MerchantTradeNo = "T" + DateTime.Now.ToString("yyyyMMddHHmmss");
            dto.PaymentType = "aio";
            if (string.IsNullOrEmpty(dto.ChoosePayment))
                dto.ChoosePayment = "Credit";

            if (!string.IsNullOrEmpty(dto.ItemName))
            {
                dto.ItemName = string.Join("#", dto.ItemName
                    .Split('#', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Replace(" ", "").Trim()));
            }

            dto.CheckMacValue = GenerateCheckMacValue(dto);

            return Ok(dto);
        }


        private string GenerateCheckMacValue(ECPayRequestDto dto)
        {
            var hashKey = _config["ECPay:HashKey"];
            var hashIV = _config["ECPay:HashIV"];

            // 1. 建立參數字典 (只放非空值)
            var dict = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(dto.ChoosePayment)) dict.Add("ChoosePayment", dto.ChoosePayment);
            if (!string.IsNullOrWhiteSpace(dto.EncryptType)) dict.Add("EncryptType", dto.EncryptType);
            if (!string.IsNullOrWhiteSpace(dto.ItemName)) dict.Add("ItemName", dto.ItemName);
            if (!string.IsNullOrWhiteSpace(dto.MerchantID)) dict.Add("MerchantID", dto.MerchantID);
            if (!string.IsNullOrWhiteSpace(dto.MerchantTradeDate)) dict.Add("MerchantTradeDate", dto.MerchantTradeDate);
            if (!string.IsNullOrWhiteSpace(dto.MerchantTradeNo)) dict.Add("MerchantTradeNo", dto.MerchantTradeNo);
            if (!string.IsNullOrWhiteSpace(dto.PaymentType)) dict.Add("PaymentType", dto.PaymentType);
            if (!string.IsNullOrWhiteSpace(dto.ReturnURL)) dict.Add("ReturnURL", dto.ReturnURL);
            if (dto.TotalAmount > 0) dict.Add("TotalAmount", dto.TotalAmount.ToString());
            if (!string.IsNullOrWhiteSpace(dto.TradeDesc)) dict.Add("TradeDesc", dto.TradeDesc);
            if (!string.IsNullOrWhiteSpace(dto.ClientBackURL)) dict.Add("ClientBackURL", dto.ClientBackURL);

            // 2. 按 Key 字母順序排序
            var sorted = dict.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase);

            // 3. 串接字串
            var raw = $"HashKey={hashKey}&{string.Join("&", sorted.Select(kv => $"{kv.Key}={kv.Value}"))}&HashIV={hashIV}";

            // 4. UrlEncode → 全小寫
            var encoded = HttpUtility.UrlEncode(raw, Encoding.UTF8).ToLower();

            // 5. 官方規範的特殊字元替換
            encoded = encoded.Replace("%2d", "-")
                             .Replace("%5f", "_")
                             .Replace("%2e", ".")
                             .Replace("%21", "!")
                             .Replace("%2a", "*")
                             .Replace("%28", "(")
                             .Replace("%29", ")")
                             .Replace("%20", "+");

            // 6. SHA256 → 轉大寫
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(encoded));
            var result = BitConverter.ToString(bytes).Replace("-", "").ToUpper();

            // Debug log
            Console.WriteLine("=== [CheckMacValue Debug] ===");
            Console.WriteLine($"Raw: {raw}");
            Console.WriteLine($"Encoded: {encoded}");
            Console.WriteLine($"CheckMacValue: {result}");

            return result;
        }



        [HttpPost("DebugCheckMac")]
        public IActionResult DebugCheckMac([FromBody] ECPayRequestDto dto)
        {
            var checkMac = GenerateCheckMacValue(dto);
            return Ok(new
            {
                RawData = dto,
                CheckMacValue = checkMac
            });
        }

        [HttpPost("Return")]
        public IActionResult PaymentReturn([FromForm] IFormCollection form)
        {
            // 1 取出必要欄位
            var merchantTradeNo = form["MerchantTradeNo"].ToString();
            var rtnCode = form["RtnCode"].ToString(); // 1=付款成功
            var checkMacValue = form["CheckMacValue"].ToString();

            // 2 依規則重新產生 CheckMacValue 與 form 內比對 (略) —— 你已有 GenerateCheckMacValue，可重用

            // 3 若驗證通過且 rtnCode == "1"：更新 DB 訂單狀態 => Paid
            // TODO: UpdateOrderStatus(merchantTradeNo, "Paid");

            // 4 綠界要求一定回傳 1|OK
            return Content("1|OK");
        }


        [HttpGet("Status")]
        public IActionResult GetOrderStatus([FromQuery] string merchantTradeNo)
        {
            // TODO: 從資料庫查這筆訂單狀態
            // 例如 var status = _db.Orders.Where(o => o.MerchantTradeNo == merchantTradeNo).Select(o => o.Status).FirstOrDefault();

            // 這裡先示意，請換成實際 DB 結果
            var status = "Paid"; // or "Created" / "COD" / "Canceled" ...

            return Ok(new { merchantTradeNo, status });
        }



    }
}
