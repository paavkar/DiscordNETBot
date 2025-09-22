using Discord.Interactions;

namespace DiscordNETBot.Modules
{
    public class ButtonHandler : InteractionModuleBase<SocketInteractionContext>
    {
        [ComponentInteraction("btn_click")]
        public async Task HandleClick()
        {
            await RespondAsync("You clicked the primary button! 🎉", ephemeral: true);
        }

        [ComponentInteraction("btn_danger")]
        public async Task HandleDanger()
        {
            await RespondAsync("⚠️ You clicked the danger button!", ephemeral: true);
        }
    }
}
