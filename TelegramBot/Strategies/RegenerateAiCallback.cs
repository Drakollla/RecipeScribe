using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies
{
    public class RegenerateAiCallback : ICallbackQuery
    {
        private readonly TelegramMealPlanFlow _mealPlanFlow;

        public RegenerateAiCallback(TelegramMealPlanFlow mealPlanFlow)
        {
            _mealPlanFlow = mealPlanFlow;
        }

        public bool CanHandle(string data) => data == "regenerate_ai";

        public async Task ExecuteAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, UserStateInfo state, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message is not { } message)
                return;

            long chatId = message.Chat.Id;
            int messageId = message.Id;

            string prefs = string.IsNullOrWhiteSpace(state.LastPreferences) ? TelegramUiElements.DefaultPreferences : state.LastPreferences;

            await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);

            await _mealPlanFlow.ProcessAiPlanningAsync(botClient, chatId, state.TargetDate, prefs, cancellationToken);
        }
    }
}