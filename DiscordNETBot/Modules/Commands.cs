using Discord;
using Discord.Audio;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace DiscordNETBot.Modules
{
    public class MyCommands : InteractionModuleBase<SocketInteractionContext>
    {
        readonly ulong VoiceChannelId = 1419338792543715349; 
        private readonly VoiceService _voiceService;
        private readonly YoutubeClient _youtube = new();

        public MyCommands(VoiceService voiceService)
        {
            _voiceService = voiceService;
        }

        [SlashCommand("userinfo", "Get information about yourself")]
        public async Task UserInfo()
        {
            var user = Context.User;

            var embed = new EmbedBuilder()
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
            var embed = new EmbedBuilder()
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
            var builder = new ComponentBuilder()
                .WithButton("Click Me!", "btn_click", ButtonStyle.Primary)
                .WithButton("Danger!", "btn_danger", ButtonStyle.Danger);

            await RespondAsync("Here are some buttons:", components: builder.Build());
        }

        // /join [channel]
        [SlashCommand("join", "Join your current voice channel")]
        public async Task JoinAsync()
        {
            var user = Context.User as IGuildUser;
            var channel = user?.VoiceChannel;

            if (channel == null)
            {
                await RespondAsync("You need to be in a voice channel first.", ephemeral: true);
                return;
            }

            try
            {
                await _voiceService.ConnectAsync(channel);
                await RespondAsync($"Joined **{channel.Name}** ✅");
            }
            catch (Exception ex)
            {
                _voiceService.RemoveAudioClient(channel.Guild.Id);
                await RespondAsync($"Failed to join: `{ex.Message}`", ephemeral: true);
            }
        }


        [SlashCommand("leave", "Make the bot leave the current voice channel")]
        public async Task LeaveAsync()
        {
            var guildId = Context.Guild.Id;

            if (!_voiceService.AudioClients.TryGetValue(guildId, out var client))
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
                _voiceService.RemoveAudioClient(guildId);
            }

            await RespondAsync("Left the voice channel 👋");
        }

        public async Task EnqueueAsync(
            SocketGuild guild,
            IAudioClient audioClient,
            string url,
            ISocketMessageChannel channel)
        {
            var state = _voiceService.MusicStates.GetOrAdd(guild.Id, _ => new GuildMusicState());

            state.PlaybackChannel = channel;
            // Get video info
            var video = await _youtube.Videos.GetAsync(url);
            var duration = video.Duration ?? TimeSpan.Zero;
            var track = new TrackInfo(video.Title, video.Id.Value, duration);
            await state.Queue.Writer.WriteAsync(track);
            state.DisplayQueue.Add(track);

            // Build queue list with durations
            var queueList = state.DisplayQueue
                .Select((t, i) => $"{i + 1}. {t.Title} [{FormatDuration(t.Duration)}]")
                .ToList();

            var totalTime = state.DisplayQueue.Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Duration);

            var embed = new EmbedBuilder()
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
                    while (state.Queue.Reader.TryRead(out var track))
                    {
                        // Remove from display queue
                        var idx = state.DisplayQueue.FindIndex(t => t.Url == track.Url);
                        if (idx >= 0)
                            state.DisplayQueue.RemoveAt(idx);

                        if (state.PlaybackChannel != null)
                        {
                            var nowPlayingEmbed = new EmbedBuilder()
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
            var youtube = new YoutubeClient();
            var manifestTask = youtube.Videos.Streams.GetManifestAsync(track.Url);
            var manifest = await manifestTask;

            var audioStreamInfo = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
            var streamUrl = audioStreamInfo.Url;
            using var ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel panic -reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5 -i \"{streamUrl}\" -ac 2 -f s16le -ar 48000 pipe:1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            using var output = ffmpeg.StandardOutput.BaseStream;
            using var discord = client.CreatePCMStream(AudioApplication.Music);
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

        [SlashCommand("play", "Play a track from a URL or search query")]
        public async Task PlayAsync(string query)
        {
            var user = Context.User as IGuildUser;
            var channel = user?.VoiceChannel;

            if (channel == null)
            {
                await RespondAsync("You need to be in a voice channel to play music.", ephemeral: true);
                return;
            }

            IAudioClient audioClient;
            if (!_voiceService.AudioClients.TryGetValue(channel.Guild.Id, out audioClient) ||
                audioClient.ConnectionState != ConnectionState.Connected)
            {
                try
                {
                    audioClient = await _voiceService.ConnectAsync(channel);
                    // Optional: small delay to ensure handshake completes
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    _voiceService.RemoveAudioClient(channel.Guild.Id);
                    await RespondAsync($"Could not connect: `{ex.Message}`", ephemeral: true);
                    return;
                }
            }

            // Now enqueue the track
            var state = _voiceService.MusicStates.GetOrAdd(channel.Guild.Id, _ => new GuildMusicState());
            state.AudioClient = audioClient;

            await EnqueueAsync(Context.Guild, audioClient, query, Context.Channel);
        }


        [SlashCommand("queue", "Display the current queue")]
        public async Task DisplayQueue()
        {
            var state = _voiceService.MusicStates.GetOrAdd(Context.Guild.Id, _ => new GuildMusicState());
            var queueList = state.DisplayQueue
                .Select((t, i) => $"{i + 1}. {t.Title} [{FormatDuration(t.Duration)}]")
                .ToList();
            var totalTime = state.DisplayQueue.Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Duration);

            var embed = new EmbedBuilder()
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
