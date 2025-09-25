using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;

namespace DiscordNETBot.Modules
{
    public class ButtonHandler : InteractionModuleBase<SocketInteractionContext>
    {
        [ComponentInteraction("btn-create-channel")]
        public async Task CreateChannel()
        {
            SocketGuild guild = Context.Guild;
            List<SocketRole?> allowedRoles = [.. new[]
            {
                guild.Roles.FirstOrDefault(r => r.Name.Equals("Moderators", StringComparison.OrdinalIgnoreCase)),
                guild.Roles.FirstOrDefault(r => r.Name.Equals("VIP", StringComparison.OrdinalIgnoreCase)),
                guild.Roles.FirstOrDefault(r => r.Name.Equals("bot", StringComparison.OrdinalIgnoreCase))
            }.Where(r => r != null)];

            List<Overwrite> overwrites =
            [
                new(
                    guild.EveryoneRole.Id,
                    PermissionTarget.Role,
                    new OverwritePermissions(viewChannel: PermValue.Deny)
                )
            ];

            foreach (SocketRole? role in allowedRoles)
            {
                overwrites.Add(new(
                    role!.Id,
                    PermissionTarget.Role,
                    new OverwritePermissions(
                        viewChannel: PermValue.Allow,
                        sendMessages: PermValue.Allow
                    )
                ));
            }

            var channelCount = guild.Channels.Count(c => c.Name.StartsWith("test-channel"));

            RestTextChannel newChannel = await guild.CreateTextChannelAsync(
                $"test-channel-{channelCount + 1}",
                props => props.PermissionOverwrites = overwrites);
            await RespondAsync($"Created new channel: {newChannel.Mention}", ephemeral: true);
        }
    }
}
