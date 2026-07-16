using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies;

public class HelpCommand : IMessageCommand
{
    public bool CanHandle(string text, BotState state) =>
        text.Equals("/help", StringComparison.OrdinalIgnoreCase);

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(message.Chat.Id,
            "Бот для управления рецептами. Команды:\n\n" +
            "/start — приветствие и запуск\n" +
            "/help — справка\n" +
            "/menu — показать меню на сегодня\n" +
            "/plan_ai — спланировать меню через ИИ\n" +
            "/search — поиск рецептов по ингредиентам\n" +
            "/cancel — отменить текущее действие\n\n" +
            "Просто прислать ссылку на YouTube-видео с рецептом для добавления в базу.",
            cancellationToken: cancellationToken);
    }
}
