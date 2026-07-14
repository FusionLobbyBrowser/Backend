using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FLB_API.Discord.Interactions
{
    public class UniversalInteractionModule : ComponentInteractionModule<ButtonInteractionContext>
    {
        [ComponentInteraction("button_removeMsg")]
        public async Task<InteractionMessageProperties> NextPage()
        {
            await Context.Message.DeleteAsync();
            return new InteractionMessageProperties().WithContent("Deleted message").WithFlags(NetCord.MessageFlags.Ephemeral);
        }
    }
}