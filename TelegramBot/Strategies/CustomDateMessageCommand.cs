using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramBot.Strategies
{
    public class CustomDateMessageCommand : IMessageCommand
    {
        private readonly TelegramMealPlanFlow _mealPlanFlow;

        public CustomDateMessageCommand(TelegramMealPlanFlow mealPlanFlow)
        {
            _mealPlanFlow = mealPlanFlow;
        }

        public bool CanHandle(string text, BotState state) => state == BotState.WaitingForCustomDate;

        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo userStateinfo, CancellationToken cancellationToken)
        {
            long chatId = message.Chat.Id;
            string text = message.Text?.Trim() ?? string.Empty;

            if (_mealPlanFlow.TryParseDate(text, out var parsedDate))
            {
                userStateinfo.TargetDate = parsedDate;
                userStateinfo.State = BotState.WaitingForAiPreferences;

                await botClient.SendMessage(
                    chatId,
                    TelegramUiElements.GetPreferencesPrompt(parsedDate),
                    parseMode: ParseMode.Markdown,
                    replyMarkup: TelegramUiElements.GetPreferencesKeyboard(),
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                await botClient.SendMessage(
                    chatId,
                    TelegramUiElements.InvalidDatePrompt,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
            }
        }
    }
}