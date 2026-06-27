using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies
{
    public class SearchIngredientsCommand : IMessageCommand
    {
        private readonly TelegramRecipeFlow _recipeFlow;

        public SearchIngredientsCommand(TelegramRecipeFlow recipeFlow)
        {
            _recipeFlow = recipeFlow;
        }

        public bool CanHandle(string text, BotState state) => state == BotState.WaitingForSearchIngredients;

        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
        {
            state.State = BotState.None;
            string text = message.Text?.Trim() ?? string.Empty;

            await _recipeFlow.ProcessSearchByIngredientsAsync(botClient, message.Chat.Id, text, cancellationToken);
        }
    }
}