namespace prjFinalProjectApi.Services
{
    public interface IAIService
    {
        Task<string> GetReplyAsync(string userMessage);
    }
}
