using Core.Contracts;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramBot.Strategies
{
    public class ConfirmMenuCallback : ICallbackQuery
    {
        private readonly IMealPlannerService _mealPlannerService;
        private readonly TelegramMealPlanFlow _mealPlanFlow;

        public ConfirmMenuCallback(IMealPlannerService mealPlannerService, TelegramMealPlanFlow mealPlanFlow)
        {
            _mealPlannerService = mealPlannerService;
            _mealPlanFlow = mealPlanFlow;
        }

        public bool CanHandle(string data) => data == "confirm_menu";

        public async Task ExecuteAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, UserStateInfo state, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message is not { } message)
                return;

            long chatId = message.Chat.Id;
            int messageId = message.Id;
            var targetDate = state.TargetDate == default ? DateOnly.FromDateTime(DateTime.Today) : state.TargetDate;
            var plan = await _mealPlannerService.GetPlanForDateAsync(chatId, targetDate);

            if (plan != null)
            {
                string formattedMenu = _mealPlanFlow.FormatMenuToMarkdown(plan) + "\n*Меню успешно подтверждено!*";

                var keyboard = _mealPlanFlow.GetMenuKeyboard(plan, isConfirmed: true);

                await botClient.EditMessageText(
                    chatId: chatId,
                    messageId: messageId,
                    text: formattedMenu,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken
                );
            }
        }
    }
}