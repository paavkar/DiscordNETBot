using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordNETBot
{
    public class DiscordBotHostedService(
        DiscordSocketClient client,
        InteractionService interactionService,
        IServiceProvider services,
        IConfiguration config,
        ILogger<DiscordBotHostedService> logger) : IHostedService
    {
        private readonly DiscordSocketClient _client = client;
        private readonly InteractionService _interactionService = interactionService;
        private readonly IServiceProvider _services = services;
        private readonly IConfiguration _config = config;
        private readonly ILogger<DiscordBotHostedService> _logger = logger;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Event hookups
            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.InteractionCreated += HandleInteraction;
            _client.MessageReceived += OnMessageReceived;
            _client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;

            _client.ModalSubmitted += async modal =>
            {
                try
                {
                    var parts = modal.Data.CustomId.Split(":");

                    if (parts.Length > 0)
                    {
                        switch (parts[0])
                        {
                            case "poll_modal":
                                await HandlePollModal(modal, parts);
                                break;
                            default:
                                _logger.LogInformation("Unknown modal submitted: {ModalId}", modal.Data.CustomId);
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling modal submission");
                }
            };

            // Load commands
            await _interactionService.AddModulesAsync(typeof(Program).Assembly, _services);

            // Login & start
            await _client.LoginAsync(TokenType.Bot, _config["BotToken"]);
            await _client.StartAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Discord bot...");
            await _client.StopAsync();
        }

        // Your existing event handler methods can move here or stay in separate classes
        private Task LogAsync(LogMessage msg)
        {
            //LogLevel level = msg.Severity switch
            //{
            //    LogSeverity.Critical => LogLevel.Critical,
            //    LogSeverity.Error => LogLevel.Error,
            //    LogSeverity.Warning => LogLevel.Warning,
            //    LogSeverity.Info => LogLevel.Information,
            //    LogSeverity.Verbose => LogLevel.Debug,
            //    LogSeverity.Debug => LogLevel.Trace,
            //    _ => LogLevel.Information
            //};

            //_logger.Log(level, msg.Exception, msg.Message);
            Console.WriteLine(msg);
            return Task.CompletedTask;
        }

        private async Task ReadyAsync()
        {
            IReadOnlyCollection<SocketGuild> guilds = _client.Guilds;
            foreach (SocketGuild? guild in guilds)
            {
                await _interactionService.RegisterCommandsToGuildAsync(guild.Id);
            }

            _logger.LogInformation("Bot is ready!");
        }

        private async Task HandleInteraction(SocketInteraction interaction)
        {
            try
            {
                SocketInteractionContext ctx = new(_client, interaction);
                IResult result = await _interactionService.ExecuteCommandAsync(ctx, _services);
                if (!result.IsSuccess)
                {
                    _logger.LogError("Interaction for type {InterActionType} failed: {Reason}", interaction.Type, result.ErrorReason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling interaction");
                if (interaction.Type == InteractionType.ApplicationCommand)
                {
                    RestInteractionMessage originalResponse = await interaction.GetOriginalResponseAsync();
                    await originalResponse.DeleteAsync();
                }
            }
        }

        private async Task HandlePollModal(SocketModal modal, string[] parts)
        {
            try
            {
                if (parts.Length < 5)
                {
                    await modal.RespondAsync("Invalid poll modal data.", ephemeral: true);
                    return;
                }

                var optionCount = int.Parse(parts[1]);
                var duration = uint.Parse(parts[2]);
                var multiSelect = bool.Parse(parts[3]);
                var question = Uri.UnescapeDataString(parts[4]);

                List<PollMediaProperties> options = [];

                for (var i = 1; i <= optionCount; i++)
                {
                    SocketMessageComponentData? comp = modal.Data.Components.FirstOrDefault(c => c.CustomId == $"option_{i}");
                    if (comp != null && !string.IsNullOrWhiteSpace(comp.Value))
                    {
                        options.Add(new PollMediaProperties { Text = comp.Value });
                    }
                }

                PollProperties poll = new()
                {
                    Question = new() { Text = question },
                    Duration = duration,
                    Answers = options,
                    AllowMultiselect = multiSelect,
                    LayoutType = PollLayout.Default
                };

                if (modal.Channel is ITextChannel textChannel)
                {
                    IUserMessage? pollMessage = await textChannel.SendMessageAsync(poll: poll);

                    if (pollMessage is not null)
                    {
                        await modal.RespondAsync(
                            $"Poll created successfully! [Jump to poll]({pollMessage.GetJumpUrl()})",
                            ephemeral: true
                        );
                    }
                }
                else
                {
                    await modal.RespondAsync("Unable to send poll in this channel.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling poll modal");
                await modal.RespondAsync("An error occurred while creating the poll.", ephemeral: true);
            }

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
            _logger.LogInformation("{User} joined the Voice Channel {Channel}", user.Username, channel.Name);
            // You could send a message to a text channel here if desired
            return Task.CompletedTask;
        }

        private Task HandleVoiceLeave(SocketUser user, SocketVoiceChannel channel)
        {
            _logger.LogInformation("{User} left the Voice Channel {Channel}", user.Username, channel.Name);
            return Task.CompletedTask;
        }

        private Task HandleVoiceMove(SocketUser user, SocketVoiceChannel fromChannel, SocketVoiceChannel toChannel)
        {
            _logger.LogInformation("{User} moved from {FromChannel} to {ToChannel}", user.Username, fromChannel.Name, toChannel.Name);
            return Task.CompletedTask;
        }
    }
}
