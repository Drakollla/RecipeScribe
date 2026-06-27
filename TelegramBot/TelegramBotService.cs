using Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBot.Strategies;

namespace TelegramBot
{
    public class TelegramBotService : BackgroundService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ConcurrentDictionary<long, UserStateInfo> _userStates = new();

        public TelegramBotService(
            ITelegramBotClient botClient,
            IServiceScopeFactory scopeFactory)
        {
            _botClient = botClient;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var me = await _botClient.GetMe(stoppingToken);
            Console.WriteLine($"Бот @{me.Username} успешно запущен как BackgroundService.");

            await _botClient.SetMyCommands(
            [
                new BotCommand { Command = "menu", Description = "Показать меню на сегодня" },
                new BotCommand { Command = "plan_ai", Description = "Спланировать меню через ИИ" },
                new BotCommand { Command = "search", Description = "Поиск рецептов по ингредиентам" }
            ], cancellationToken: stoppingToken);

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
            };

            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandleErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken
            );

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Type == UpdateType.Message && update.Message is { } message)
                    await HandleMessageAsync(botClient, message, cancellationToken);
                else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is { } callbackQuery)
                    await HandleCallbackQueryAsync(botClient, callbackQuery, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Бот] Ошибка при обработке апдейта: {ex.Message}");
            }
        }

        private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            if (message.Text is not { } messageText)
                return;

            long chatId = message.Chat.Id;
            string text = messageText.Trim();

            Console.WriteLine($"[Бот] Получено сообщение от {chatId}: {text}");

            var stateInfo = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());

            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                try
                {
                    var commands = scope.ServiceProvider.GetServices<IMessageCommand>();
                    var command = commands.FirstOrDefault(c => c.CanHandle(text, stateInfo.State));

                    if (command != null)
                        await command.ExecuteAsync(botClient, message, stateInfo, cancellationToken);
                    else await botClient.SendMessage(chatId, TelegramUiElements.DefaultCommandsPrompt, cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Бот] Ошибка при фоновой обработке сообщения {chatId}: {ex.Message}");
                }
            }, cancellationToken);
        }

        private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message is not { } message)
                return;

            long chatId = message.Chat.Id;
            string data = callbackQuery.Data ?? string.Empty;

            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

            var stateInfo = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());

            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();

                try
                {
                    var callbacks = scope.ServiceProvider.GetServices<ICallbackQuery>();
                    var callback = callbacks.FirstOrDefault(c => c.CanHandle(data));

                    if (callback != null)
                        await callback.ExecuteAsync(botClient, callbackQuery, stateInfo, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Бот] Ошибка при фоновой обработке кнопки от {chatId}: {ex.Message}");
                }
            }, cancellationToken);
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Ошибка Telegram API: {exception.Message}");
            return Task.CompletedTask;
        }
    }
}