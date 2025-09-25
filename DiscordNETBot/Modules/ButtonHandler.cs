using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;

namespace DiscordNETBot.Modules
{
    public class ButtonHandler : InteractionModuleBase<SocketInteractionContext>
    {
        [ComponentInteraction("btn-create-channel", runMode: RunMode.Async)]
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

            var lastIndex = 0;
            var categoryName = "Test category";
            SocketCategoryChannel? existingCategory = guild.CategoryChannels
                .FirstOrDefault(
                c => c.Name.Equals(
                    categoryName,
                    StringComparison.OrdinalIgnoreCase
                    )
                );
            if (existingCategory is null)
            {
                RestCategoryChannel? createdCategory = await guild.CreateCategoryChannelAsync(
                    categoryName,
                    props =>
                    {
                        props.Position = 0;
                    }
                    );
                if (createdCategory is not null)
                {
                    existingCategory = guild.GetCategoryChannel(createdCategory.Id);
                }
            }
            if (existingCategory is null)
            {
                await RespondAsync("Channel creation encountered and error with the channel category.", ephemeral: true);
                return;
            }
            IEnumerable<SocketGuildChannel> channels = guild.Channels
                            .Where(c => c.Name.StartsWith("test-channel"));
            if (channels.Any())
            {
                IEnumerable<int> indexes = channels.Select(c => int.TryParse(c.Name.Split('-').Last(), out var i) ? i : 0);
                lastIndex = indexes.Max();
            }

            RestTextChannel newChannel = await guild.CreateTextChannelAsync(
                $"test-channel-{lastIndex + 1}",
                props =>
                {
                    props.PermissionOverwrites = overwrites;
                    props.CategoryId = existingCategory!.Id;
                }
            );
            await RespondAsync($"Created new channel: {newChannel.Mention}", ephemeral: true);
        }
    }
}
