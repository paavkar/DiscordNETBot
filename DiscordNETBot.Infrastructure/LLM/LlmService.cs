using DiscordNETBot.Application.LLM;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text;
using System.Text.Json;

namespace DiscordNETBot.Infrastructure.LLM
{
    public class LlmService : ILlmService
    {
        private readonly Dictionary<(ulong, ulong), ChatHistory> ConversationHistories = [];
        private IChatCompletionService ChatCompletionService;
        private Kernel Kernel;
        private readonly string GoogleApiKey;
        private readonly string GoogleSearchEngineId;
        private readonly HttpClient HttpClient;
        private bool AllowGoogleSearch;
        OpenAIPromptExecutionSettings Settings = new()
        {
            MaxTokens = 300,
            Temperature = 0.7
        };

        private string SystemPrompt = """
            You are a helpful assistant. Here are your rules you need to adhere to:
            1. Keep your responses under 300 tokens. Consider the user prompt for how long
            your response should be.
            2. You will be responding to messages in a Discord server. Make sure
            your responses include Discord markdown formatting where appropriate.
            """;
        public LlmService(IConfiguration config)
        {
            var modelId = config["Ollama:ModelId"];
            var endpoint = config["Ollama:Endpoint"];
            bool.TryParse(config["AllowGoogleSearch"], out AllowGoogleSearch);

            GoogleApiKey = config["Google:ApiKey"] ?? "";
            GoogleSearchEngineId = config["Google:SearchEngineId"] ?? "";

            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOllamaChatCompletion(
                modelId: modelId!,
                endpoint: new Uri(endpoint!)
            );
            Kernel = kernelBuilder.Build();
            ChatCompletionService = Kernel.GetRequiredService<IChatCompletionService>();

            HttpClient = new();
        }

        private async Task<string?> PerformGoogleSearchAsync(string query, int topResults = 5)
        {
            if (string.IsNullOrWhiteSpace(GoogleApiKey) || string.IsNullOrWhiteSpace(GoogleSearchEngineId))
            {
                return null;
            }

            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                var requestUri = $"https://www.googleapis.com/customsearch/v1?key={GoogleApiKey}&cx={GoogleSearchEngineId}&q={encodedQuery}&num={topResults}";
                using HttpResponseMessage resp = await HttpClient.GetAsync(requestUri);
                if (!resp.IsSuccessStatusCode)
                {
                    return null;
                }

                using Stream stream = await resp.Content.ReadAsStreamAsync();
                using JsonDocument doc = await JsonDocument.ParseAsync(stream);

                if (!doc.RootElement.TryGetProperty("items", out JsonElement items))
                {
                    return null;
                }

                StringBuilder sb = new();
                var index = 1;
                foreach (JsonElement item in items.EnumerateArray())
                {
                    if (index > topResults) break;

                    var title = item.TryGetProperty("title", out JsonElement t) ? t.GetString() : null;
                    var snippet = item.TryGetProperty("snippet", out JsonElement s) ? s.GetString() : null;
                    var link = item.TryGetProperty("link", out JsonElement l) ? l.GetString() : null;

                    sb.AppendLine($"Result {index}:");
                    if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine($"Title: {title}");
                    if (!string.IsNullOrWhiteSpace(snippet)) sb.AppendLine($"Snippet: {snippet}");
                    if (!string.IsNullOrWhiteSpace(link)) sb.AppendLine($"Link: {link}");
                    sb.AppendLine();

                    index++;
                }

                var resultText = sb.ToString().Trim();
                // Keep result reasonably short for prompt injection
                if (resultText.Length > 2800) resultText = string.Concat(resultText.AsSpan(0, 2800), "…");
                return resultText;
            }
            catch
            {
                // Swallow exceptions; search is best-effort to provide recent context.
                return null;
            }
        }

        public async Task<string> GetChatResponseAsync(ulong guildId, ulong userId, string message)
        {
            (ulong guildId, ulong userId) key = (guildId, userId);
            if (!ConversationHistories.TryGetValue(key, out ChatHistory? history))
            {
                history = [];
                history.AddDeveloperMessage(SystemPrompt);
                ConversationHistories[key] = history;
            }

            ChatHistory tempHistory = [.. history];

            var webContext = "";
            if (AllowGoogleSearch)
                webContext = await PerformGoogleSearchAsync(message);

            var timeString = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            var extraInfo = $"The current date and time is {timeString}.\n";
            if (!string.IsNullOrWhiteSpace(webContext))
            {
                extraInfo += "Recent web search results (Google Custom Search). Use these to inform your answer and cite links when relevant:\n" +
                    webContext;
            }
            tempHistory.AddDeveloperMessage(extraInfo);

            history.AddUserMessage(message);
            tempHistory.AddUserMessage(message);
            ChatMessageContent response = await ChatCompletionService.GetChatMessageContentAsync(
                    tempHistory,
                    Settings,
                    kernel: Kernel);
            history.AddAssistantMessage(response.Content!);
            return response.Content!;
        }

        public async Task<string> GetResponseAsync(string message)
        {
            ChatHistory history = [];
            history.AddDeveloperMessage(SystemPrompt);

            var webContext = "";
            if (AllowGoogleSearch)
                webContext = await PerformGoogleSearchAsync(message);

            var timeString = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            var extraInfo = $"The current date and time is {timeString}.\n";
            if (!string.IsNullOrWhiteSpace(webContext))
            {
                extraInfo += "Recent web search results (Google Custom Search). Use these to inform your answer and cite links when relevant:\n" +
                    webContext;
            }
            history.AddDeveloperMessage(extraInfo);

            history.AddUserMessage(message);

            ChatMessageContent response = await ChatCompletionService.GetChatMessageContentAsync(
                    history,
                    Settings,
                    kernel: Kernel);

            return response.Content!;
        }

        public async Task<bool> DeleteChatHistoryAsync(ulong guildId, ulong userId)
        {
            (ulong guildId, ulong userId) key = (guildId, userId);
            return ConversationHistories.Remove(key);
        }

        public Task<string> SetAllowSearchAsync()
        {
            if (AllowGoogleSearch)
            {
                AllowGoogleSearch = false;
                return Task.FromResult("Google search has been disabled.");
            }
            else
            {
                AllowGoogleSearch = true;
                return Task.FromResult("Google search has been enabled.");
            }
        }
    }
}
