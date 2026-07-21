using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramBot.Strategies
{
    public class SearchCommand : IMessageCommand
    {
        public bool CanHandle(string text, BotState state) => text.Equals("/search", StringComparison.OrdinalIgnoreCase);

        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
        {
            state.State = BotState.WaitingForSearchIngredients;

            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: TelegramUiElements.SearchPrompt,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken
            );
        }
    }
}
