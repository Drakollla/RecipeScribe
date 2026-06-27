using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies
{
    public interface ICallbackQuery
    {
        bool CanHandle(string data);

        Task ExecuteAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, UserStateInfo userStateInfo, CancellationToken cancellationToken);
    }
}