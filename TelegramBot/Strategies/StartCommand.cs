using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies;

public class StartCommand : IMessageCommand
{
    public bool CanHandle(string text, BotState state) =>
        text.Equals("/start", StringComparison.OrdinalIgnoreCase);

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
    {
        state.State = BotState.None;
        await botClient.SendMessage(message.Chat.Id,
            "Привет! Я помогу тебе управлять рецептами с YouTube.\n\n" +
            TelegramUiElements.DefaultCommandsPrompt,
            cancellationToken: cancellationToken);
    }
}
