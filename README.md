# Application overview

This application is a Discord bot created with [Discord.NET](https://docs.discordnet.dev/).
Clean Architecture is used in architecting the project structure.

# Running locally

## User secrets

This projects expects the following secrets to be present:

```
{
  "BotToken": "<YOUR_DISCORD_BOT_TOKEN>",
  "GuildId": <YOUR_DISCORD_SERVER_ID>,
  "VoiceId": <YOUR_DISCORD_SERVER_VOICE_CHANNEL_ID>,
  "Ollama": {
    "ModelId": "<OLLAMA_MODEL>",
    "Endpoint": "<OLLAMA_ENDPOINT>"
  }
}
```
The `GuildId` and `VoiceId` values are of type `ulong` that you need to copy from your Discord server.

`VoiceId` is used when connecting the bot to a Voice Channel.

You can get the `ModelId` from [Ollama](https://ollama.com/search) search.

The `Endpoint` should be the default of `http://localhost:11434`.

## Instructions
Follow the [instructions](https://docs.discordnet.dev/guides/getting_started/first-bot.html) for how to make a bot,
invite it to your server, and how to get the token you need for the User Secret.

Microsoft has [instructions](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Ollama%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp) for starting Ollama
or you can use the [quickstart](https://docs.ollama.com/quickstart) from Ollama.

# AI functionality

This bot has two commands that use the Ollama LLM. The slash command `ask` is simple in that it uses the Ollama LLM
to answer user's question/prompt.

The slash command `chat` remembers the user's previous messages so that you can have more meaningful conversations.

The AI functionality is created with Semantic Kernel and its Ollama integration. The specific model is up to the
developer to set in the Secrets.