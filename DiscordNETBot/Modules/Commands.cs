using Discord;
using Discord.Audio;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordNETBot.Application.LLM;
using DiscordNETBot.Application.Voice;
using DiscordNETBot.Domain.Voice;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using System.Diagnostics;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace DiscordNETBot.Modules
{
    public class MyCommands(
        IVoiceService voiceService,
        IConfiguration config,
        ILlmService llmService,
        IConnectionMultiplexer redis) : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly YoutubeClient _youtube = new();

        [SlashCommand("poll", "Create a poll", runMode: RunMode.Async)]
        public async Task Poll2(
            [Summary(description: "Your question for the poll")] string question,
            [Summary(description: "How many options? (2-10)")] int optionCount,
            [Summary(description: "Poll duration (hours)")] uint duration,
            [Summary(description: "Allow multiselect")] bool multiSelect
)
        {
            if (optionCount is < 2 or > 10)
            {
                await RespondAsync("Option count must be between 2 and 10.", ephemeral: true);
                return;
            }

            var safeQuestion = Uri.EscapeDataString(question);
            // Build modal dynamically
            ModalBuilder modalBuilder = new ModalBuilder()
                .WithTitle("Enter poll options")
                .WithCustomId($"poll_modal:{optionCount}:{duration}:{multiSelect}:{safeQuestion}");

            for (var i = 1; i <= optionCount; i++)
            {
                modalBuilder.AddTextInput(
                    label: $"Option {i}",
                    customId: $"option_{i}",
                    placeholder: $"Enter option {i} text",
                    required: true,
                    style: TextInputStyle.Short
                );
            }

            await RespondWithModalAsync(modalBuilder.Build());
        }

        [SlashCommand(
            "create-channel-button",
            "Create a button that creates a private channel."
        )]
        public async Task CreateChannelButton()
        {
            ComponentBuilder builder = new ComponentBuilder()
                .WithButton("Create Channel", "btn-create-channel", ButtonStyle.Primary);

            await RespondAsync(
                "Press this button to create a private channel:",
                components: builder.Build()
            );
        }

        [SlashCommand("redis-test", "Test Redis pub/sub functionality")]
        public async Task RedisTest()
        {
            ISubscriber sub = redis.GetSubscriber();
            var msg = $"Redis test at {DateTimeOffset.UtcNow} from {Context.User.Username}";
            await sub.PublishAsync("discord-events", msg);
            await RespondAsync("Published test message to Redis.", ephemeral: true);
        }

        [SlashCommand("user-info", "Get information about yourself")]
        public async Task UserInfo()
        {
            SocketUser user = Context.User;

            Embed embed = new EmbedBuilder()
                .WithTitle($"{user.Username}'s Info")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .AddField("Username", user.Username, true)
                .AddField("Discriminator", user.Discriminator, true)
                .AddField("ID", user.Id, true)
                .AddField("Created At", user.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), false)
                .AddField("Joined At", (user as SocketGuildUser)?.JoinedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A", false)
                .WithColor(Color.Blue)
                .WithFooter("Requested via /userinfo")
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(Context.User.Mention, embed: embed);
        }

        [SlashCommand("multioptions", "Command with multiple parameters")]
        public async Task MultiOptions(
            [Summary(description: "A string parameter")] string text,
            [Summary(description: "A number parameter")] int number,
            [Summary(description: "Choose an option")] MyChoices choice)
        {
            Embed embed = new EmbedBuilder()
                .WithTitle("Multi Options Command")
                .AddField("Text", text)
                .AddField("Number", number)
                .AddField("Choice", choice.ToString())
                .WithColor(Color.Green)
                .Build();

            await RespondAsync(embed: embed);
        }

        public enum MyChoices
        {
            OptionA,
            OptionB,
            OptionC
        }

        // /join [channel]
        [SlashCommand("join", "Join your current voice channel", runMode: RunMode.Async)]
        public async Task JoinAsync()
        {
            var voiceName = config["VoiceChannelName"];
            SocketVoiceChannel channel = Context.Guild.VoiceChannels
                .FirstOrDefault(c => c.Name.Equals(voiceName, StringComparison.OrdinalIgnoreCase))!;
            if (channel is null)
            {
                await Context.Guild.CreateVoiceChannelAsync(voiceName);
                channel = Context.Guild.VoiceChannels
                    .FirstOrDefault(c => c.Name.Equals(voiceName, StringComparison.OrdinalIgnoreCase))!;
            }
            if (channel is null)
            {
                await RespondAsync("Voice channel not found and could not be created.", ephemeral: true);
                return;
            }
            IAudioClient? audioClient = await voiceService.ConnectAsync(channel);
            if (audioClient is not null)
                await RespondAsync($"Joined **{channel.Name}** ✅");
        }

        [SlashCommand("leave", "Make the bot leave the current voice channel")]
        public async Task LeaveAsync()
        {
            var guildId = Context.Guild.Id;

            if (!voiceService.AudioClients.TryGetValue(guildId, out IAudioClient? client))
            {
                await RespondAsync("I'm not in a voice channel.", ephemeral: true);
                return;
            }

            try
            {
                // Politely disconnect from voice
                await client.StopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while leaving voice in guild {guildId}: {ex}");
            }
            finally
            {
                // Always remove from service to prevent stale/disposed client reuse
                voiceService.RemoveAudioClient(guildId);
            }

            await RespondAsync("Left the voice channel 👋");
        }

        public async Task EnqueueAsync(
            SocketGuild guild,
            IAudioClient audioClient,
            string url,
            ISocketMessageChannel channel)
        {
            GuildMusicState state = voiceService.MusicStates.GetOrAdd(guild.Id, _ => new GuildMusicState());

            state.PlaybackChannel = channel;
            // Get video info
            Video video = await _youtube.Videos.GetAsync(url);
            var thumbnailUrl = video.Thumbnails.TryGetWithHighestResolution()?.Url;
            TimeSpan duration = video.Duration ?? TimeSpan.Zero;
            TrackInfo track = new(video.Title, video.Id.Value, duration, thumbnailUrl);

            await state.Queue.Writer.WriteAsync(track);
            state.DisplayQueue.Add(track);

            // Build queue list with durations
            List<string> queueList = [.. state.DisplayQueue.Select((t, i) => $"{i + 1}. {t.Title} [{FormatDuration(t.Duration)}]")];
            TimeSpan totalTime = state.DisplayQueue.Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Duration);

            Embed embed = new EmbedBuilder()
                .WithTitle("🎶 Added to Queue")
                .WithDescription($"**{track.Title}** [{FormatDuration(track.Duration)}] has been added to the queue.")
                .AddField("Current Queue", queueList.Count > 0 ? string.Join("\n", queueList) : "*(empty)*")
                .AddField("⏱ Total Length", queueList.Count > 0 ? FormatDuration(totalTime) : "00:00")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(embed: embed);

            // Start playback if not already playing
            if (!state.IsPlaying)
            {
                state.AudioClient = audioClient;
                state.IsPlaying = true;
                _ = Task.Run(() => PlayQueueAsync(guild.Id, state)); // fire-and-forget playback loop
            }
        }

        private static string FormatDuration(TimeSpan? duration)
        {
            return duration?.TotalHours >= 1
                ? duration?.ToString(@"hh\:mm\:ss")
                : duration?.ToString(@"mm\:ss");
        }

        private async Task PlayQueueAsync(ulong guildId, GuildMusicState state)
        {
            try
            {
                while (await state.Queue.Reader.WaitToReadAsync())
                {
                    while (state.Queue.Reader.TryRead(out TrackInfo? track))
                    {
                        // Remove from display queue
                        var idx = state.DisplayQueue.FindIndex(t => t.Url == track.Url);
                        if (idx >= 0)
                            state.DisplayQueue.RemoveAt(idx);

                        await PlayTrackAsync(state.AudioClient, track, state);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Playback error in guild {guildId}: {ex}");
            }
            finally
            {
                state.IsPlaying = false; // allow restart when new track is queued
            }
        }

        private async Task PlayTrackAsync(IAudioClient client, TrackInfo track, GuildMusicState state)
        {
            YoutubeClient youtube = new();
            ValueTask<StreamManifest> manifestTask = youtube.Videos.Streams.GetManifestAsync(track.Url);
            StreamManifest manifest = await manifestTask;

            IStreamInfo audioStreamInfo = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
            var streamUrl = audioStreamInfo.Url;

            if (state.PlaybackChannel != null)
            {
                Embed nowPlayingEmbed = new EmbedBuilder()
                    .WithTitle("▶️ Now Playing")
                    .WithDescription($"**{track.Title}**")
                    .WithThumbnailUrl(track.ThumbnailUrl)
                    .AddField("Duration", FormatDuration(track.Duration), true)
                    .AddField("Link", $"https://youtu.be/{track.Url}", true)
                    .WithColor(Color.Green)
                    .WithCurrentTimestamp()
                    .Build();

                await state.PlaybackChannel.SendMessageAsync(embed: nowPlayingEmbed);
            }

            using Process? ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel panic -reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5 -i \"{streamUrl}\" -ac 2 -f s16le -ar 48000 pipe:1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            using Stream output = ffmpeg.StandardOutput.BaseStream;
            using AudioOutStream discord = client.CreatePCMStream(AudioApplication.Music);
            try
            {
                await output.CopyToAsync(discord);
            }
            finally
            {
                await discord.FlushAsync();
                ffmpeg.WaitForExit();
                var err = await ffmpeg.StandardError.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(err))
                    Console.WriteLine($"FFmpeg error: {err}");
            }
        }

        [SlashCommand("play", "Play a track from a URL or search query", runMode: RunMode.Async)]
        public async Task PlayAsync(string query)
        {
            var voiceName = config["VoiceChannelName"];
            IGuildUser? user = Context.User as IGuildUser;
            IVoiceChannel? userChannel = user?.VoiceChannel;

            if (userChannel is null)
            {
                await RespondAsync("You need to be in a Voice Channel to play music.");
                return;
            }

            SocketVoiceChannel channel = Context.Guild.VoiceChannels
                .FirstOrDefault(c => c.Name.Equals(voiceName, StringComparison.OrdinalIgnoreCase))!;

            if (channel is null)
            {
                await Context.Guild.CreateVoiceChannelAsync(voiceName);
                channel = Context.Guild.VoiceChannels
                    .FirstOrDefault(c => c.Name.Equals(voiceName, StringComparison.OrdinalIgnoreCase))!;
            }
            if (channel is null)
            {
                await RespondAsync("Voice channel not found and could not be created.", ephemeral: true);
                return;
            }

            if (channel == null)
            {
                await RespondAsync("You need to be in a voice channel to play music.", ephemeral: true);
                return;
            }

            if (!voiceService.AudioClients.TryGetValue(channel.Guild.Id, out IAudioClient? audioClient) ||
                audioClient.ConnectionState != ConnectionState.Connected)
            {
                audioClient = await voiceService.ConnectAsync(channel);
                await Task.Delay(500); // handshake buffer
            }

            // Now enqueue the track
            await EnqueueAsync(Context.Guild, audioClient, query, Context.Channel);
        }

        [SlashCommand("queue", "Display the current queue")]
        public async Task DisplayQueue()
        {
            GuildMusicState state = voiceService.MusicStates.GetOrAdd(Context.Guild.Id, _ => new GuildMusicState());
            List<string> queueList = state.DisplayQueue
                .Select((t, i) => $"{i + 1}. {t.Title} [{FormatDuration(t.Duration)}]")
                .ToList();
            TimeSpan totalTime = state.DisplayQueue.Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Duration);

            Embed embed = new EmbedBuilder()
                .WithTitle("🎶 Added to Queue")
                .WithDescription($"The current queue:")
                .AddField("Current Queue", queueList.Count > 0 ? string.Join("\n", queueList) : "*(empty)*")
                .AddField("⏱ Total Length", queueList.Count > 0 ? FormatDuration(totalTime) : "00:00")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(embed: embed);
        }

        [SlashCommand("ask", "Ask the AI a question", runMode: RunMode.Async)]
        public async Task AskAsync([Summary(description: "Your question")] string question)
        {
            await DeferAsync(); // Acknowledge the command to avoid timeout
            var response = await llmService.GetResponseAsync(question);
            await FollowupAsync($"{Context.User.Mention} {response}");
        }

        [SlashCommand("chat", "Chat with the AI", runMode: RunMode.Async)]
        public async Task ChatAsync([Summary(description: "Your message")] string message)
        {
            await DeferAsync(); // Acknowledge the command to avoid timeout
            var response = await llmService.GetChatResponseAsync(Context.Guild.Id, Context.User.Id, message);
            await FollowupAsync($"{Context.User.Mention} {response}");
        }

        [SlashCommand("clear-chat", "Clear your chat history with the AI", runMode: RunMode.Async)]
        public async Task ClearChatAsync()
        {
            await DeferAsync(); // Acknowledge the command to avoid timeout
            var success = await llmService.DeleteChatHistoryAsync(Context.Guild.Id, Context.User.Id);
            if (success)
                await FollowupAsync($"{Context.User.Mention} Your chat history has been cleared.");
            else
                await FollowupAsync($"{Context.User.Mention} Failed to clear chat history. Please try again later.");
        }

        [SlashCommand("toggle-search", "Toggle AI web search capability", runMode: RunMode.Async)]
        public async Task ToggleSearchAsync()
        {
            await DeferAsync(); // Acknowledge the command to avoid timeout
            var response = await llmService.SetAllowSearchAsync();
            await FollowupAsync($"{Context.User.Mention} {response}");
        }
    }
}
