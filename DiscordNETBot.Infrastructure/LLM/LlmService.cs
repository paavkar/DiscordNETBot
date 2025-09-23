using DiscordNETBot.Application.LLM;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace DiscordNETBot.Infrastructure.LLM
{
    public class LlmService : ILlmService
    {
        private readonly Dictionary<(ulong, ulong), ChatHistory> ConversationHistories = [];
        private IChatCompletionService ChatCompletionService;
        private Kernel Kernel;
        OpenAIPromptExecutionSettings Settings = new()
        {
            MaxTokens = 300,
            Temperature = 0.7
        };
        private string SystemPrompt = """
            You are a helpful assistant. Here are your rules you need to adhere to:
            1. Keep your responses concise and to the point (under 300 tokens).
            """;
        public LlmService(IConfiguration config)
        {
            var modelId = config["Ollama:ModelId"];
            var endpoint = config["Ollama:Endpoint"];

            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOllamaChatCompletion(
                modelId: modelId,
                endpoint: new Uri(endpoint)
            );
            Kernel = kernelBuilder.Build();
            ChatCompletionService = Kernel.GetRequiredService<IChatCompletionService>();
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

            history.AddUserMessage(message);
            ChatMessageContent response = await ChatCompletionService.GetChatMessageContentAsync(
                    history,
                    Settings,
                    kernel: Kernel);
            history.AddAssistantMessage(response.Content!);
            return response.Content!;
        }
        public async Task<string> GetResponseAsync(string message)
        {
            ChatHistory history = [];
            history.AddDeveloperMessage(SystemPrompt);
            history.AddUserMessage(message);

            ChatMessageContent response = await ChatCompletionService.GetChatMessageContentAsync(
                    history,
                    Settings,
                    kernel: Kernel);

            return response.Content!;
        }
    }
}
