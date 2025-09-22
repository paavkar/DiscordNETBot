using Discord;
using Discord.Audio;
using DiscordNETBot.Application.Voice;
using DiscordNETBot.Domain.Voice;
using System.Collections.Concurrent;

namespace DiscordNETBot.Infrastructure
{
    public class VoiceService : IVoiceService
    {
        public ConcurrentDictionary<ulong, IAudioClient> AudioClients { get; set; } = new();
        public ConcurrentDictionary<ulong, GuildMusicState> MusicStates { get; set; } = new();

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
            catch (System.Net.WebSockets.WebSocketException)
            {
                return null!;
            }
        }
    }
}
