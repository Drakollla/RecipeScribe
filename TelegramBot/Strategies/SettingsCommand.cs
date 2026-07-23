using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramBot.Strategies;

public class SettingsCommand : IMessageCommand
{
    public bool CanHandle(string text, BotState state) => text.Equals("/settings", StringComparison.OrdinalIgnoreCase);

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
    {
        state.State = BotState.WaitingForSettings;

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "Сколько порций использовать по умолчанию? (число от 1 до 20)",
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken
        );
    }
}
