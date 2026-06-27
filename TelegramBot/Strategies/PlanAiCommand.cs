using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies
{
    public class PlanAiCommand : IMessageCommand
    {
        public bool CanHandle(string text, BotState state) => text.Equals("/plan_ai", StringComparison.OrdinalIgnoreCase);

        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo userStateinfo, CancellationToken cancellationToken)
        {
            userStateinfo.State = BotState.None;

            await botClient.SendMessage(
                message.Chat.Id,
                "На какой день вы хотите запланировать меню?",
                replyMarkup: TelegramUiElements.GetDateSelectionKeyboard(),
                cancellationToken: cancellationToken
            );
        }
    }
}