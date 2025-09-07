using prjFinalProjectApi.Services;

public class RemoteAIService : IAIService
{
    private readonly HttpClient _httpClient;

    public RemoteAIService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetReplyAsync(string userMessage)
    {
        var payload = new { Question = userMessage };
        var response = await _httpClient.PostAsJsonAsync("/api/customer/ask", payload);

        if (!response.IsSuccessStatusCode)
            return $"遠端 AI 呼叫失敗: {response.StatusCode}";

        var result = await response.Content.ReadFromJsonAsync<RemoteAIResponse>();
        return result?.Answer ?? "無回覆";
    }
}

public class RemoteAIResponse
{
    public string Answer { get; set; } = string.Empty;
}
