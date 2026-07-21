using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramBot.Strategies
{
    public class PlanDateCallback : ICallbackQuery
    {
        public bool CanHandle(string data) => data.StartsWith("plan_date:");

        public async Task ExecuteAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, UserStateInfo state, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message is not { } message)
                return;

            long chatId = message.Chat.Id;
            int messageId = message.Id;
            string dateType = callbackQuery.Data!.Split(':')[1];
            DateOnly targetDate;

            if (dateType == "today")
                targetDate = DateOnly.FromDateTime(DateTime.Today);
            else if (dateType == "tomorrow")
                targetDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            else
            {
                state.State = BotState.WaitingForCustomDate;

                await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);
                await botClient.SendMessage(
                    chatId,
                    TelegramUiElements.EnterDatePrompt,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken
                );
                return;
            }

            state.TargetDate = targetDate;
            state.State = BotState.WaitingForAiPreferences;

            await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);
            await botClient.SendMessage(
                chatId,
                TelegramUiElements.GetPreferencesPrompt(targetDate),
                parseMode: ParseMode.Html,
                replyMarkup: TelegramUiElements.GetPreferencesKeyboard(),
                cancellationToken: cancellationToken
            );
        }
    }
}
