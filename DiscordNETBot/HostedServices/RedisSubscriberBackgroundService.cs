using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DiscordNETBot
{
    public class RedisSubscriberBackgroundService(
        ILogger<RedisSubscriberBackgroundService> logger,
        IConnectionMultiplexer redis,
        DiscordSocketClient client,
        IConfiguration config) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ISubscriber subscriber = redis.GetSubscriber();
            await subscriber.SubscribeAsync("discord-events", async (channel, message) =>
            {
                try
                {
                    logger.LogInformation("Redis message: {Message}", message);

                    // Example: send to a specific channel in Discord
                    SocketGuild guild = client.GetGuild(ulong.Parse(config["GuildId"]!));
                    SocketTextChannel? textChannel = guild?.TextChannels
                        .FirstOrDefault(
                            c => c.Name.Equals(
                                config["TextChannelName"],
                                StringComparison.OrdinalIgnoreCase
                            )
                        );
                    if (textChannel != null)
                    {
                        await textChannel.SendMessageAsync($"[Redis] {message}");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error handling Redis message");
                }
            });

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}
