using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies
{
    public class ShowRecipeCallback : ICallbackQuery
    {
        private readonly TelegramRecipeFlow _recipeFlow;

        public ShowRecipeCallback(TelegramRecipeFlow recipeFlow)
        {
            _recipeFlow = recipeFlow;
        }

        public bool CanHandle(string data) => data.StartsWith("show_recipe:");

        public async Task ExecuteAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, UserStateInfo state, CancellationToken cancellationToken)
        {
            string recipeIdStr = callbackQuery.Data!.Split(':')[1];

            if (Guid.TryParse(recipeIdStr, out var recipeId))
                await _recipeFlow.SendRecipeDocumentAsync(botClient, callbackQuery.Message!.Chat.Id, recipeId, cancellationToken);
        }
    }
}