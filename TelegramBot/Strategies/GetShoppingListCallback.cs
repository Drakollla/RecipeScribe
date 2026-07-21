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
        private readonly ILogger<GetShoppingListCallback> _logger;

        public GetShoppingListCallback(IMealPlanApiClient planApi, ILogger<GetShoppingListCallback> logger)
        {
            _planApi = planApi;
            _logger = logger;
        }

        public bool CanHandle(string data) => data.StartsWith("shopping_list:");

        public async Task ExecuteAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, UserStateInfo state, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message is not { } message)
                return;

            long chatId = message.Chat.Id;
            string data = callbackQuery.Data ?? string.Empty;
            string planIdStr = data.Split(':')[1];

            if (!Guid.TryParse(planIdStr, out var planId))
                return;

            try
            {
                string shoppingListMarkdown = await _planApi.GetShoppingListAsync(planId, cancellationToken);

                await botClient.SendMessage(
                    chatId: chatId,
                    text: shoppingListMarkdown,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken
                );
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "API error getting shopping list for {PlanId}", planId);
                await botClient.SendMessage(chatId, $"Ошибка загрузки списка покупок: {ex.Message}", cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке списка покупок {PlanId}", planId);
                await botClient.SendMessage(chatId, "Произошла ошибка при загрузке списка покупок.", cancellationToken: cancellationToken);
            }
        }
    }
}
