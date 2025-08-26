using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace prjFinalProjectApi.Services
{
    // HMAC 簽章  
    //簽章工具（HMAC-SHA256），這是 Line Pay API 規範要求的簽章流程，用來驗證你送到 LINE 的請求是真的由你商家發出，沒有被竄改。
    public static class LinePaySign
    {
        public static string CreateSignature(string channelSecret, string uri, string nonce, string body)
        {
            var raw = $"{channelSecret}{uri}{body}{nonce}";
            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(channelSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToBase64String(hash);
        }
    }

    // 回傳模型（簡化）
    public class LinePayRequestResult
    {
        public string returnCode { get; set; } = "";
        public string returnMessage { get; set; } = "";
        public JsonElement info { get; set; }
        public string raw { get; set; } = "";
    }

    // Service
    public class LinePayService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _cfg;

        public LinePayService(HttpClient http, IConfiguration cfg)
        {
            _http = http;
            _cfg = cfg;
        }

        private (string Base, string ChannelId, string Secret) Cfg()
        {
            var s = _cfg.GetSection("LinePay");
            return (s["ApiBase"]!, s["ChannelId"]!, s["ChannelSecret"]!);
        }

        public async Task<LinePayRequestResult> RequestAsync(string orderId, int amount, string productName)
        {
            var (baseUrl, channelId, secret) = Cfg();
            var uri = "/v3/payments/request";
            var url = $"{baseUrl}{uri}";

            var bodyObj = new
            {
                orderId,
                amount,
                currency = "TWD",
                packages = new[]
                {
                    new {
                        id = "pkg1",
                        amount,
                        name = "活動報名費",
                        products = new[] { new { name = productName, quantity = 1, price = amount } }
                    }
                },
                redirectUrls = new
                {
                    confirmUrl = _cfg["LinePay:ConfirmUrl"],
                    cancelUrl = _cfg["LinePay:CancelUrl"]
                }
            };
            var body = JsonSerializer.Serialize(bodyObj);

            var nonce = Guid.NewGuid().ToString();
            var sign = LinePaySign.CreateSignature(secret, uri, nonce, body);

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            req.Headers.Add("X-LINE-ChannelId", channelId);
            req.Headers.Add("X-LINE-Authorization-Nonce", nonce);
            req.Headers.Add("X-LINE-Authorization", sign);

            var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            var obj = JsonSerializer.Deserialize<LinePayRequestResult>(json)!;
            obj.raw = json;
            return obj;
        }

        public async Task<LinePayRequestResult> ConfirmAsync(string transactionId, int amount)
        {
            var (baseUrl, channelId, secret) = Cfg();
            var uri = $"/v3/payments/{transactionId}/confirm";
            var url = $"{baseUrl}{uri}";

            var bodyObj = new { amount, currency = "TWD" };
            var body = JsonSerializer.Serialize(bodyObj);

            var nonce = Guid.NewGuid().ToString();
            var sign = LinePaySign.CreateSignature(secret, uri, nonce, body);

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            req.Headers.Add("X-LINE-ChannelId", channelId);
            req.Headers.Add("X-LINE-Authorization-Nonce", nonce);
            req.Headers.Add("X-LINE-Authorization", sign);

            var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            var obj = JsonSerializer.Deserialize<LinePayRequestResult>(json)!;
            obj.raw = json;
            return obj;
        }
        public async Task<LinePayRequestResult> RequestRawAsync(string bodyJson)
        {
            var (baseUrl, channelId, secret) = Cfg();
            var uri = "/v3/payments/request";
            var url = $"{baseUrl}{uri}";

            var nonce = Guid.NewGuid().ToString();
            var sign = LinePaySign.CreateSignature(secret, uri, nonce, bodyJson);

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            req.Headers.Add("X-LINE-ChannelId", channelId);
            req.Headers.Add("X-LINE-Authorization-Nonce", nonce);
            req.Headers.Add("X-LINE-Authorization", sign);

            var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            var obj = JsonSerializer.Deserialize<LinePayRequestResult>(json) ?? new LinePayRequestResult();
            obj.raw = json;                 // 把 LINE 的原文一起帶回，方便除錯
            return obj;
        }
    }
}
