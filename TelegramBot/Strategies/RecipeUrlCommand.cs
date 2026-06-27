using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies
{
    public class RecipeUrlCommand : IMessageCommand
    {
        private readonly TelegramRecipeFlow _recipeFlow;

        public RecipeUrlCommand(TelegramRecipeFlow recipeFlow)
        {
            _recipeFlow = recipeFlow;
        }

        public bool CanHandle(string text, BotState state) => state == BotState.None &&
            Uri.TryCreate(text, UriKind.Absolute, out var uriResult) &&
            (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
        {
            string text = message.Text?.Trim() ?? string.Empty;
            await _recipeFlow.ProcessVideoRecipeAsync(botClient, message.Chat.Id, text, cancellationToken);
        }
    }
}