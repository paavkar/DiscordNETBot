using Discord.Audio;
using Discord.WebSocket;
using System.Threading.Channels;

namespace DiscordNETBot.Domain.Voice
{
    public class GuildMusicState
    {
        public IAudioClient AudioClient { get; set; }
        public Channel<TrackInfo> Queue { get; } = Channel.CreateUnbounded<TrackInfo>();
        public List<TrackInfo> DisplayQueue { get; } = new();
        public bool IsPlaying { get; set; } = false;
        public ISocketMessageChannel PlaybackChannel { get; set; }
    }
}
