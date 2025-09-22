using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DiscordNETBot
{
    public class VoiceService
    {
        public ConcurrentDictionary<ulong, IAudioClient> AudioClients { get; } = new();
        public ConcurrentDictionary<ulong, GuildMusicState> MusicStates { get; } = new();
        public void RemoveAudioClient(ulong guildId)
        {
            if (AudioClients.TryRemove(guildId, out IAudioClient? client))
            {
                try { client.Dispose(); } catch { /* ignore */ }
            }
            MusicStates.TryRemove(guildId, out _);
        }

        public async Task<IAudioClient> ConnectAsync(IVoiceChannel channel)
        {
            RemoveAudioClient(channel.Guild.Id);

            try
            {
                IAudioClient audioClient = await channel.ConnectAsync();
                AudioClients[channel.Guild.Id] = audioClient;
                // Ensure GuildMusicState exists
                GuildMusicState state = MusicStates.GetOrAdd(channel.Guild.Id, _ => new GuildMusicState());
                state.AudioClient = audioClient;
                await Task.Delay(500); // handshake buffer

                audioClient.Disconnected += async ex =>
                {
                    Console.WriteLine($"Voice disconnected in guild {channel.Guild.Id}: {ex?.Message}");
                    RemoveAudioClient(channel.Guild.Id);
                    await Task.CompletedTask;
                };

                return audioClient;
            }
            catch (System.Net.WebSockets.WebSocketException ex)
            {
                return null!;
            }
        }
    }

    public class GuildMusicState
    {
        public IAudioClient AudioClient { get; set; }
        public Channel<TrackInfo> Queue { get; } = Channel.CreateUnbounded<TrackInfo>();
        public List<TrackInfo> DisplayQueue { get; } = new();
        public bool IsPlaying { get; set; } = false;
        public ISocketMessageChannel PlaybackChannel { get; set; }
    }

    public record TrackInfo(string Title, string Url, TimeSpan Duration);
}
