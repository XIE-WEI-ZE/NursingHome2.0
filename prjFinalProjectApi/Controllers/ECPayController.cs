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
            dto.ClientBackURL = _config["ECPay:ClientBackURL"]!;

            // 符合規範的交易編號 (20碼內, 英數字)
            dto.MerchantTradeNo = "T" + DateTime.Now.ToString("yyyyMMddHHmmss");

            dto.PaymentType = "aio";
            if (string.IsNullOrEmpty(dto.ChoosePayment))
                dto.ChoosePayment = "Credit"; // 預設信用卡

            // 這裡處理 ItemName，把多餘空白清掉
            if (!string.IsNullOrEmpty(dto.ItemName))
            {
                dto.ItemName = string.Join("#", dto.ItemName.Split('#', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Replace(" ", "").Trim()));

            }

            // ⚡ 產生檢查碼
            dto.CheckMacValue = GenerateCheckMacValue(dto);

            // Debug Log
            Console.WriteLine("=== [ECPay 請求參數] ===");
            foreach (var prop in dto.GetType().GetProperties())
            {
                Console.WriteLine($"{prop.Name}: {prop.GetValue(dto)}");
            }

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
            Console.WriteLine("=== [ECPay 回傳參數] ===");
            foreach (var key in form.Keys)
            {
                Console.WriteLine($"{key} = {form[key]}");
            }

            // TODO: 驗證 CheckMacValue、更新資料庫訂單狀態 (改成已付款)

            // 綠界規範：一定要回 "1|OK"，否則會一直重送通知
            return Content("1|OK");
        }




    }
}
