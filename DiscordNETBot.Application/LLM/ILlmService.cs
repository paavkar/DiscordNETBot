namespace DiscordNETBot.Application.LLM
{
    public interface ILlmService
    {
        Task<string> GetResponseAsync(string message);
        Task<string> GetChatResponseAsync(ulong guildId, ulong userId, string message);
    }
}
