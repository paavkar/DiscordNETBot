using Discord;
using Discord.Audio;
using DiscordNETBot.Domain.Voice;
using System.Collections.Concurrent;

namespace DiscordNETBot.Application.Voice
{
    public interface IVoiceService
    {
        public ConcurrentDictionary<ulong, IAudioClient> AudioClients { get; set; }
        public ConcurrentDictionary<ulong, GuildMusicState> MusicStates { get; set; }

        public void RemoveAudioClient(ulong guildId);
        public Task<IAudioClient> ConnectAsync(IVoiceChannel channel);
    }
}
