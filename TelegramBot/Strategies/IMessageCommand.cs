using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies
{
    public interface IMessageCommand
    {
        bool CanHandle(string text, BotState state);

        Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo userStateinfo, CancellationToken cancellationToken);
    }
}
