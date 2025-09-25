using AngleSharp;
using AngleSharp.Dom;
using DiscordNETBot.Application.LLM;
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
        public LlmService(Microsoft.Extensions.Configuration.IConfiguration config)
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
                    //if (!string.IsNullOrWhiteSpace(snippet)) sb.AppendLine($"Snippet: {snippet}");
                    if (!string.IsNullOrWhiteSpace(link)) sb.AppendLine($"Link: {link}");

                    var extract = await ExtractMainTextWithAngleSharpAsync(link);
                    if (string.IsNullOrWhiteSpace(extract))
                        extract = "(No readable content extracted)";

                    if (extract.Length > 1500) extract = extract[..1500] + "…";
                    sb.AppendLine($"Extract: {extract}");

                    sb.AppendLine();
                    index++;
                }

                var resultText = sb.ToString().Trim();
                // Keep result reasonably short for prompt injection
                //if (resultText.Length > 2800) resultText = string.Concat(resultText.AsSpan(0, 2800), "…");
                return resultText;
            }
            catch
            {
                // Swallow exceptions; search is best-effort to provide recent context.
                return null;
            }
        }

        private async Task<string?> ExtractMainTextWithAngleSharpAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            HttpRequestMessage req = new(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");

            using HttpResponseMessage resp = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead);
            if (!resp.IsSuccessStatusCode) return null;

            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html")) return null;

            var html = await resp.Content.ReadAsStringAsync();

            // Parse HTML with AngleSharp
            AngleSharp.IConfiguration config = Configuration.Default;
            IBrowsingContext context = BrowsingContext.New(config);
            IDocument doc = await context.OpenAsync(req => req.Content(html).Address(url));

            // Remove obvious noise
            RemoveNodes(doc, "script, style, noscript, svg, canvas");
            RemoveNodes(doc, ".advert, .ads, .ad, .cookie, .cookie-banner, .header, header, .nav, nav, .footer, footer");

            // Try to find main content
            IElement? main =
                doc.QuerySelector("article") ??
                doc.QuerySelector("main") ??
                doc.QuerySelector("[role='main']") ??
                FindLargestTextContainer(doc.Body);

            if (main is null) return null;

            var text = NormalizeWhitespace(main.TextContent);
            if (text.Length < 200) return null; // skip if too short

            return text;
        }

        private static void RemoveNodes(IDocument doc, string selector)
        {
            foreach (IElement node in doc.QuerySelectorAll(selector))
                node.Remove();
        }

        private static IElement? FindLargestTextContainer(IElement? root)
        {
            if (root == null) return null;
            IElement? best = null;
            var bestLen = 0;

            foreach (IElement el in root.QuerySelectorAll("div, section, article, main"))
            {
                var len = el.TextContent?.Length ?? 0;
                if (len > bestLen)
                {
                    bestLen = len;
                    best = el;
                }
            }
            return best;
        }

        private static string NormalizeWhitespace(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
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
