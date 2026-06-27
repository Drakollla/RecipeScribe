using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies
{
    public class PreferencesNoneCallback : ICallbackQuery
    {
        private readonly TelegramMealPlanFlow _mealPlanFlow;

        public PreferencesNoneCallback(TelegramMealPlanFlow mealPlanFlow)
        {
            _mealPlanFlow = mealPlanFlow;
        }

        public bool CanHandle(string data) => data == "pref_none";

        public async Task ExecuteAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, UserStateInfo state, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message is not { } message)
                return;

            long chatId = message.Chat.Id;
            int messageId = message.Id;

            state.State = BotState.None;
            state.LastPreferences = TelegramUiElements.DefaultPreferences;

            await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);

            await _mealPlanFlow.ProcessAiPlanningAsync(botClient, chatId, state.TargetDate, TelegramUiElements.DefaultPreferences, cancellationToken);
        }
    }
}