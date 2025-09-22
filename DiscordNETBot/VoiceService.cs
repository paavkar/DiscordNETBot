using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DiscordNETBot
{
    public class VoiceService
    {
        public ConcurrentDictionary<ulong, IAudioClient> AudioClients { get; } = new();
        public ConcurrentDictionary<ulong, GuildMusicState> MusicStates { get; } = new();
        public void RemoveAudioClient(ulong guildId)
        {
            if (AudioClients.TryRemove(guildId, out var client))
            {
                try { client.Dispose(); } catch { /* ignore */ }
            }
            MusicStates.TryRemove(guildId, out _);
        }

        public async Task<IAudioClient> ConnectAsync(IVoiceChannel channel)
        {
            // Always remove stale client before connecting
            RemoveAudioClient(channel.Guild.Id);

            var audioClient = await channel.ConnectAsync();
            AudioClients[channel.Guild.Id] = audioClient;

            // Ensure GuildMusicState exists
            var state = MusicStates.GetOrAdd(channel.Guild.Id, _ => new GuildMusicState());
            state.AudioClient = audioClient;

            return audioClient;
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
