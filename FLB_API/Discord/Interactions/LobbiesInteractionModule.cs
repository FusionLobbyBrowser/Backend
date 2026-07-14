using FLB_API.Discord.Commands;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

using static FLB_API.Controllers.LobbyListController;

namespace FLB_API.Discord.Interactions
{
    public class LobbiesInteractionModule : ComponentInteractionModule<ButtonInteractionContext>
    {
        public const string NO_MORE_PAGES = "There are no more pages!";

        public const string RETURN_MSG = "Returned to page {0}";

        public const string ADVANCE_MSG = "Advanced to page {0}";

        #region Check

        [ComponentInteraction("button_next")]
        public async Task<InteractionMessageProperties> NextPage(int platform, int page, int pages)
        {
            Platform _platform = (Platform)platform;
            if (page == pages)
                return DiscordBotManager.Error(NO_MORE_PAGES);
            page++;
            await Context.Message.ModifyAsync(x => x.Components = LobbiesCommandModule.Internal_Check(_platform, page).Components);
            return new InteractionMessageProperties().WithContent(string.Format(ADVANCE_MSG, page)).WithFlags(MessageFlags.Ephemeral);
        }

        [ComponentInteraction("button_previous")]
        public async Task<InteractionMessageProperties> PreviousPage(int platform, int page)
        {
            Platform _platform = (Platform)platform;
            if (page == 1)
                return DiscordBotManager.Error(NO_MORE_PAGES);
            page--;
            await Context.Message.ModifyAsync(x => x.Components = LobbiesCommandModule.Internal_Check(_platform, page).Components);
            return new InteractionMessageProperties().WithContent(string.Format(RETURN_MSG, page)).WithFlags(MessageFlags.Ephemeral);
        }

        [ComponentInteraction("button_currentPage")]
        public async Task<InteractionMessageProperties> PreviousPage(int platform)
        {
            Platform _platform = (Platform)platform;
            await Context.Message.ModifyAsync(x => x.Components = LobbiesCommandModule.Internal_Check(_platform, 1).Components);
            return new InteractionMessageProperties().WithContent(string.Format(RETURN_MSG, 1)).WithFlags(MessageFlags.Ephemeral);
        }

        [ComponentInteraction("button_refresh")]
        public async Task<InteractionMessageProperties> Refresh(int platform, int page)
        {
            Platform _platform = (Platform)platform;
            await Context.Message.ModifyAsync(x => x.Components = LobbiesCommandModule.Internal_Check(_platform, page).Components);
            return new InteractionMessageProperties().WithContent("Refreshed!").WithFlags(MessageFlags.Ephemeral);
        }

        #endregion Check

        #region Info

        [ComponentInteraction("button_nextInfo")]
        public async Task<InteractionMessageProperties> NextPageInfo(string id, int page, int pages)
        {
            if (page == pages)
                return DiscordBotManager.Error(NO_MORE_PAGES);
            page++;
            var info = await LobbiesCommandModule.Internal_Info(id, page);
            var components = info.Components;
            await Context.Message.ModifyAsync(x => x.Components = components);
            return new InteractionMessageProperties().WithContent(string.Format(ADVANCE_MSG, page)).WithFlags(MessageFlags.Ephemeral);
        }

        [ComponentInteraction("button_previousInfo")]
        public async Task<InteractionMessageProperties> PreviousPageInfo(string id, int page)
        {
            if (page == 1)
                return DiscordBotManager.Error(NO_MORE_PAGES);
            page--;
            var info = await LobbiesCommandModule.Internal_Info(id, page);
            var components = info.Components;
            await Context.Message.ModifyAsync(x => x.Components = components);
            return new InteractionMessageProperties().WithContent(string.Format(RETURN_MSG, page)).WithFlags(MessageFlags.Ephemeral);
        }

        [ComponentInteraction("button_currentPageInfo")]
        public async Task<InteractionMessageProperties> CurrentPageInfo(string id)
        {
            var info = await LobbiesCommandModule.Internal_Info(id, 1);
            var components = info.Components;
            await Context.Message.ModifyAsync(x => x.Components = components);
            return new InteractionMessageProperties().WithContent(string.Format(RETURN_MSG, 1)).WithFlags(MessageFlags.Ephemeral);
        }

        [ComponentInteraction("button_refreshInfo")]
        public async Task<InteractionMessageProperties> RefreshInfo(string id, int page)
        {
            var info = await LobbiesCommandModule.Internal_Info(id, page);
            var components = info.Components;
            await Context.Message.ModifyAsync(x => x.Components = components);
            return new InteractionMessageProperties().WithContent("Refreshed!").WithFlags(MessageFlags.Ephemeral);
        }

        #endregion Info
    }
}