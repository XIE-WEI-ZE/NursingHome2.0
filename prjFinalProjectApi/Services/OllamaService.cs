using System.Net.Http.Json;

namespace prjFinalProjectApi.Services
{
    public class OllamaService : IAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _ollamaEndpoint;

        public OllamaService(HttpClient httpClient, string ollamaEndpoint)
        {
            _httpClient = httpClient;
            _ollamaEndpoint = ollamaEndpoint;
        }

        public async Task<string> GetReplyAsync(string userMessage)
        {
            var response = await _httpClient.PostAsJsonAsync(_ollamaEndpoint, new
            {
                model = "gemma3:4b",
                prompt = $"請用繁體中文回答以下內容：\n{userMessage}",
                stream = false
            });

            if (!response.IsSuccessStatusCode)
                return $"Ollama API 呼叫失敗: {response.StatusCode}";

            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
            return result?.Response ?? "無回覆";
        }
    }

    public class OllamaResponse
    {
        public string Response { get; set; } = string.Empty;
    }
}
