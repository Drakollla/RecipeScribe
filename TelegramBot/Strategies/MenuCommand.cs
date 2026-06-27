using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies
{
    public class MenuCommand : IMessageCommand
    {
        private readonly TelegramMealPlanFlow _mealPlanFlow;

        public MenuCommand(TelegramMealPlanFlow mealPlanFlow)
        {
            _mealPlanFlow = mealPlanFlow;
        }

        public bool CanHandle(string text, BotState state) => text.Equals("/menu", StringComparison.OrdinalIgnoreCase);

        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken) =>
            await _mealPlanFlow.ShowTodayMenuAsync(botClient, message.Chat.Id, cancellationToken);
    }
}