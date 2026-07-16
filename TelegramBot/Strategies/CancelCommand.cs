using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies;

public class CancelCommand : IMessageCommand
{
    public bool CanHandle(string text, BotState state)
    {
        if (!text.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
            return false;

        return state != BotState.None;
    }

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
    {
        state.State = BotState.None;
        state.LastPreferences = string.Empty;
        state.LastSubstituteIngredient = null;
        await botClient.SendMessage(message.Chat.Id, "Текущее действие отменено.", cancellationToken: cancellationToken);
    }
}
