# DiscordNETBot

## Table of Contents

- [Application overview](#application-overview)
- [Running Locally](#running-locally)
  - [User Secrets](#user-secrets)
  - [Instructions](#instructions)
- [AI Functionality](#ai-functionality)
- [Music Playback](#music-playback)
- [Commands](#commands)

## Application overview

This application is a Discord bot created with [Discord.NET](https://docs.discordnet.dev/).

The bot is also capable of music playback in a Voice Channel.

Clean Architecture is used in architecting the project structure.

## Running locally

### User secrets

This projects expects the following secrets to be present:

```
{
  "BotToken": "<YOUR_DISCORD_BOT_TOKEN>",
  "GuildId": <YOUR_DISCORD_SERVER_ID>,
  "VoiceId": <YOUR_DISCORD_SERVER_VOICE_CHANNEL_ID>,
  "Ollama": {
    "ModelId": "<OLLAMA_MODEL>",
    "Endpoint": "<OLLAMA_ENDPOINT>"
  },
  "Google": {
    "ApiKey": "<YOUR_GOOGLE_CUSTOM_SEARCH_API_KEY>",
    "SearchEngineId": "YOUR_GOOGLE_SEARCH_ENGINE_ID"
  },
  "AllowGoogleSearch": true
}
```
The `GuildId` and `VoiceId` values are of type `ulong` that you need to copy from your Discord server.

`VoiceId` is used when connecting the bot to a Voice Channel.

You can get the `ModelId` from [Ollama](https://ollama.com/search) search.

The `Endpoint` should be the default of `http://localhost:11434`.

Google API key and Search Engine Id are required for the usage of Google Custom Search JSON API.
You can see [this](https://developers.google.com/custom-search/v1/overview) to get your API key
and look [here](https://developers.google.com/custom-search/v1/using_rest) to create your Google
Search Engine (https://cse.google.com/all).

### Instructions
Follow the [instructions](https://docs.discordnet.dev/guides/getting_started/first-bot.html) for how to make a bot,
invite it to your server, and how to get the token you need for the User Secret.

Microsoft has [instructions](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Ollama%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp) for starting Ollama
or you can use the [quickstart](https://docs.ollama.com/quickstart) from Ollama.

## AI functionality

This bot has two commands that use the Ollama LLM. The slash command `ask` is simple in that it uses the Ollama LLM
to answer user's question/prompt.

The slash command `chat` remembers the user's previous messages so that you can have more meaningful conversations.

The AI functionality is created with Semantic Kernel and its Ollama integration. The specific model is up to the
developer to set in the Secrets.

To introduce the LLM with more recent information, Google Custom Search JSON API is used to search Google and get
site knowledge.

## Music playback

The music playback is a little buggy. It is made with [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode).
The bot joining the designated Voice Channel works best if you first make the bot to join the channel with
`/join` and then use `/play` with your YouTube link to play the track. Sometimes the music playback also works
with just `/play` when you are already in the Voice Channel. There could be a timing issue with how long after
starting the project you do the commands.

## Commands

- `/user-info`: Displays information about the user in an embed. Information displayed includes: username,
ID, account creation date and time, server join date and time.
- `/join`: Make the bot to join the designated Voice Channel.
- `/leave`: Make the bot leave the Voice Channel, if in one.
- `/play`: Make the bot play a video from YouTube (intended for songs). Requires the user to be in a VC.
- `queue`: Display the currently queued tracks.
- `/ask`: Ask the language model some question.
- `/chat`: Chat with AI. User prompts and AI responses are saved (in-memory, Dictionary).
- `/clear-chat`: Clear the chat history for this specific user and server combination.
- `/toggle-search`: Toggle whether the language model is allowed to use search.