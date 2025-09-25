using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordNETBot.Application.LLM;
using DiscordNETBot.Application.Voice;
using DiscordNETBot.Infrastructure;
using DiscordNETBot.Infrastructure.LLM;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace DiscordNETBot
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddUserSecrets<Program>(optional: true);
                })
                .ConfigureServices((context, services) =>
                {
                    IConfiguration configuration = context.Configuration;

                    DiscordSocketClient client = new(new DiscordSocketConfig
                    {
                        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
                    });

                    InteractionService interactionService = new(client.Rest);

                    services.AddSingleton(configuration);
                    services.AddSingleton(client);
                    services.AddSingleton(interactionService);
                    services.AddSingleton<IVoiceService, VoiceService>();
                    services.AddSingleton<ILlmService, LlmService>();

                    services.AddSingleton<IConnectionMultiplexer>(
                        ConnectionMultiplexer.Connect(configuration["RedisConnection"]!));

                    services.AddHostedService<DiscordBotHostedService>();
                    services.AddHostedService<RedisSubscriberBackgroundService>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}
