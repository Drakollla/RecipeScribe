using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBot.Contracts;

namespace TelegramBot.Strategies
{
    public class GetShoppingListCallback : ICallbackQuery
    {
        private readonly IMealPlanApiClient _planApi;

        public GetShoppingListCallback(IMealPlanApiClient planApi)
        {
            _planApi = planApi;
        }

        public bool CanHandle(string data) => data.StartsWith("shopping_list:");

        public async Task ExecuteAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, UserStateInfo state, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message is not { } message)
                return;

            long chatId = message.Chat.Id;
            string data = callbackQuery.Data ?? string.Empty;

            string planIdStr = data.Split(':')[1];

            if (Guid.TryParse(planIdStr, out var planId))
            {
                string shoppingListMarkdown = await _planApi.GetShoppingListAsync(planId, cancellationToken);

                await botClient.SendMessage(
                    chatId: chatId,
                    text: shoppingListMarkdown,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
            }
        }
    }
}
