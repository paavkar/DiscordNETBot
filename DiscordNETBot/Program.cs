using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DiscordNETBot
{
    public class Program
    {
        private DiscordSocketClient _client;
        private InteractionService _interactionService;
        private IServiceProvider _services;
        private static readonly string BotToken;
        private static readonly ulong GuildId;
        private static readonly ulong VoiceId;

        static Program()
        {
            var config = new ConfigurationBuilder()
                .AddUserSecrets<Program>()
                .Build();

            BotToken = config["BotToken"];
            GuildId = ulong.Parse(config["GuildId"]);
            VoiceId = ulong.Parse(config["VoiceId"]);
        }

        public static Task Main(string[] args) => new Program().MainAsync();

        public async Task MainAsync()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            });

            _interactionService = new InteractionService(_client.Rest);

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_interactionService)
                .AddSingleton<VoiceService>()
                .BuildServiceProvider();

            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.InteractionCreated += HandleInteraction;

            _client.MessageReceived += OnMessageReceived;
            _client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;

            await _client.LoginAsync(TokenType.Bot, BotToken);
            await _client.StartAsync();

            await _interactionService.AddModulesAsync(typeof(Program).Assembly, _services);

            await Task.Delay(-1);
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            // Ignore system messages and messages from bots (including itself)
            if (message.Author.IsBot) return;

            // Example: send a reply in the same channel
            await message.Channel.SendMessageAsync($"{message.Author.Mention} said: {message.Content}");
        }

        private async Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            // Ignore bots
            if (user.IsBot) return;

            // User joined a voice channel
            if (before.VoiceChannel == null && after.VoiceChannel != null)
            {
                await HandleVoiceJoin(user, after.VoiceChannel);
            }
            // User left a voice channel
            else if (before.VoiceChannel != null && after.VoiceChannel == null)
            {
                await HandleVoiceLeave(user, before.VoiceChannel);
            }
            // User moved between channels
            else if (before.VoiceChannel != null && after.VoiceChannel != null &&
                     before.VoiceChannel.Id != after.VoiceChannel.Id)
            {
                await HandleVoiceMove(user, before.VoiceChannel, after.VoiceChannel);
            }
        }

        private Task HandleVoiceJoin(SocketUser user, SocketVoiceChannel channel)
        {
            Console.WriteLine($"{user.Username} joined {channel.Name}");
            // You could send a message to a text channel here if desired
            return Task.CompletedTask;
        }

        private Task HandleVoiceLeave(SocketUser user, SocketVoiceChannel channel)
        {
            Console.WriteLine($"{user.Username} left {channel.Name}");
            return Task.CompletedTask;
        }

        private Task HandleVoiceMove(SocketUser user, SocketVoiceChannel fromChannel, SocketVoiceChannel toChannel)
        {
            Console.WriteLine($"{user.Username} moved from {fromChannel.Name} to {toChannel.Name}");
            return Task.CompletedTask;
        }


        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        }

        private async Task ReadyAsync()
        {
            var commands = await _interactionService.RegisterCommandsToGuildAsync(GuildId);

            //var guild = _client.GetGuild(GuildId);
            //var voiceChannel = guild?.GetVoiceChannel(VoiceId);

            //if (voiceChannel != null)
            //{
            //    await voiceChannel.ConnectAsync();
            //    Console.WriteLine($"Auto-joined {voiceChannel.Name}");
            //}
            //else
            //{
            //    Console.WriteLine("Voice channel not found.");
            //}

            Console.WriteLine("Bot is ready!");
        }

        private async Task HandleInteraction(SocketInteraction interaction)
        {
            try
            {
                var ctx = new SocketInteractionContext(_client, interaction);
                await _interactionService.ExecuteCommandAsync(ctx, _services);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                if (interaction.Type == InteractionType.ApplicationCommand)
                {
                    var originalResponse = await interaction.GetOriginalResponseAsync();
                    await originalResponse.DeleteAsync();
                }
            }
        }
    }
}
