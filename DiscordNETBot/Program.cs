using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        private static readonly IConfiguration _config;

        static Program()
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddUserSecrets<Program>()
                .Build();
            _config = config;

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
                .AddSingleton<IConfiguration>(_config)
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
            IReadOnlyCollection<RestGuildCommand> commands = await _interactionService.RegisterCommandsToGuildAsync(GuildId);

            Console.WriteLine("Bot is ready!");

            foreach (var guild in _client.Guilds)
            {
                var botUser = guild.GetUser(_client.CurrentUser.Id);
                if (botUser?.VoiceChannel != null)
                {
                    Console.WriteLine($"[Voice] Bot was already in voice channel '{botUser.VoiceChannel.Name}' in guild '{guild.Name}', leaving...");
                }
            }
        }

        private async Task HandleInteraction(SocketInteraction interaction)
        {
            try
            {
                SocketInteractionContext ctx = new SocketInteractionContext(_client, interaction);
                await _interactionService.ExecuteCommandAsync(ctx, _services);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                if (interaction.Type == InteractionType.ApplicationCommand)
                {
                    RestInteractionMessage originalResponse = await interaction.GetOriginalResponseAsync();
                    await originalResponse.DeleteAsync();
                }
            }
        }
    }
}
