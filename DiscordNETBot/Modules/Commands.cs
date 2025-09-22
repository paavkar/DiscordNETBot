using Discord;
using Discord.Audio;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordNETBot.Application.Voice;
using DiscordNETBot.Domain.Voice;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace DiscordNETBot.Modules
{
    public class MyCommands : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly ulong VoiceId;
        private readonly IVoiceService VoiceService;
        private readonly YoutubeClient _youtube = new();

        public MyCommands(IVoiceService voiceService, IConfiguration config)
        {
            VoiceService = voiceService;
            VoiceId = ulong.Parse(config["VoiceId"]);
        }

        [SlashCommand("userinfo", "Get information about yourself")]
        public async Task UserInfo()
        {
            SocketUser user = Context.User;

            Embed embed = new EmbedBuilder()
                .WithTitle($"{user.Username}'s Info")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .AddField("Username", user.Username, true)
                .AddField("Discriminator", user.Discriminator, true)
                .AddField("ID", user.Id, true)
                .WithColor(Color.Blue)
                .WithFooter("Requested via /userinfo")
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(Context.User.Mention, embed: embed);
        }

        [SlashCommand("say", "Make the bot say something")]
        public async Task Say([Summary(description: "What should I say?")] string text)
        {
            await RespondAsync(text);
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

        [SlashCommand("button-demo", "Show a message with buttons")]
        public async Task ButtonDemo()
        {
            ComponentBuilder builder = new ComponentBuilder()
                .WithButton("Click Me!", "btn_click", ButtonStyle.Primary)
                .WithButton("Danger!", "btn_danger", ButtonStyle.Danger);

            await RespondAsync("Here are some buttons:", components: builder.Build());
        }

        // /join [channel]
        [SlashCommand("join", "Join your current voice channel", runMode: RunMode.Async)]
        public async Task JoinAsync()
        {
            SocketVoiceChannel channel = Context.Guild.GetVoiceChannel(VoiceId);

            IAudioClient? audioClient = await VoiceService.ConnectAsync(channel);
            if (audioClient is not null)
                await RespondAsync($"Joined **{channel.Name}** ✅");
        }


        [SlashCommand("leave", "Make the bot leave the current voice channel")]
        public async Task LeaveAsync()
        {
            var guildId = Context.Guild.Id;

            if (!VoiceService.AudioClients.TryGetValue(guildId, out IAudioClient? client))
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
                // Log if you have a logger, but don't crash the command
                Console.WriteLine($"Error while leaving voice in guild {guildId}: {ex}");
            }
            finally
            {
                // Always remove from service to prevent stale/disposed client reuse
                VoiceService.RemoveAudioClient(guildId);
            }

            await RespondAsync("Left the voice channel 👋");
        }

        public async Task EnqueueAsync(
            SocketGuild guild,
            IAudioClient audioClient,
            string url,
            ISocketMessageChannel channel)
        {
            GuildMusicState state = VoiceService.MusicStates.GetOrAdd(guild.Id, _ => new GuildMusicState());

            state.PlaybackChannel = channel;
            // Get video info
            Video video = await _youtube.Videos.GetAsync(url);
            TimeSpan duration = video.Duration ?? TimeSpan.Zero;
            TrackInfo track = new TrackInfo(video.Title, video.Id.Value, duration);

            await state.Queue.Writer.WriteAsync(track);
            state.DisplayQueue.Add(track);

            // Build queue list with durations
            List<string> queueList = state.DisplayQueue
                .Select((t, i) => $"{i + 1}. {t.Title} [{FormatDuration(t.Duration)}]")
                .ToList();
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

                        if (state.PlaybackChannel != null)
                        {
                            Embed nowPlayingEmbed = new EmbedBuilder()
                                .WithTitle("▶️ Now Playing")
                                .WithDescription($"**{track.Title}**")
                                .AddField("Duration", FormatDuration(track.Duration), true)
                                .AddField("Link", $"https://youtu.be/{track.Url}", true)
                                .WithColor(Color.Green)
                                .WithCurrentTimestamp()
                                .Build();

                            await state.PlaybackChannel.SendMessageAsync(embed: nowPlayingEmbed);
                        }

                        await PlayTrackAsync(state.AudioClient, track);
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

        private async Task PlayTrackAsync(IAudioClient client, TrackInfo track)
        {
            YoutubeClient youtube = new YoutubeClient();
            ValueTask<StreamManifest> manifestTask = youtube.Videos.Streams.GetManifestAsync(track.Url);
            StreamManifest manifest = await manifestTask;

            IStreamInfo audioStreamInfo = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
            var streamUrl = audioStreamInfo.Url;
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
            IGuildUser? user = Context.User as IGuildUser;
            IVoiceChannel? userChannel = user?.VoiceChannel;

            if (userChannel is null)
            {
                await RespondAsync("You need to be in a Voice Channel to play music.");
                return;
            }

            SocketVoiceChannel channel = Context.Guild.GetVoiceChannel(VoiceId);

            if (channel == null)
            {
                await RespondAsync("You need to be in a voice channel to play music.", ephemeral: true);
                return;
            }

            IAudioClient audioClient;
            if (!VoiceService.AudioClients.TryGetValue(channel.Guild.Id, out audioClient) ||
                audioClient.ConnectionState != ConnectionState.Connected)
            {
                audioClient = await VoiceService.ConnectAsync(channel);
                await Task.Delay(500); // handshake buffer
            }

            // Now enqueue the track
            await EnqueueAsync(Context.Guild, audioClient, query, Context.Channel);
        }


        [SlashCommand("queue", "Display the current queue")]
        public async Task DisplayQueue()
        {
            GuildMusicState state = VoiceService.MusicStates.GetOrAdd(Context.Guild.Id, _ => new GuildMusicState());
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
    }
}
