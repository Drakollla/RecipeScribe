using Core.Enums;
using Core.Exceptions;
using Core.Helpers;
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
        private readonly ILogger<TelegramBotService> _logger;

        private readonly ConcurrentDictionary<long, UserStateInfo> _userStates = new();
        private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLocks = new();

        public TelegramBotService(
            ITelegramBotClient botClient,
            IServiceScopeFactory scopeFactory,
            ILogger<TelegramBotService> logger)
        {
            _botClient = botClient;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var me = await _botClient.GetMe(stoppingToken);
            _logger.LogInformation("Бот @{Username} запущен и работает.", me.Username);

            await _botClient.SetMyCommands(
            [
                new BotCommand { Command = "start", Description = "Запустить бота" },
                new BotCommand { Command = "help", Description = "Показать помощь" },
                new BotCommand { Command = "menu", Description = "Показать меню на сегодня" },
                new BotCommand { Command = "plan_ai", Description = "Спланировать меню через ИИ" },
                new BotCommand { Command = "search", Description = "Поиск рецептов по ингредиентам" },
                new BotCommand { Command = "substitute", Description = "Замена ингредиента в рецепте" },
                new BotCommand { Command = "cancel", Description = "Отменить текущий сценарий" }
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
                _logger.LogError(ex, "Ошибка при обработке обновления");
            }
        }

        private Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            if (message.Text is not { } messageText)
                return Task.CompletedTask;

            long chatId = message.Chat.Id;
            string text = messageText.Trim();

            _logger.LogInformation("Received message from {ChatId}: {Text}", chatId, text);

            var stateInfo = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());
            var semaphore = _chatLocks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));

            _ = Task.Run(async () =>
            {
                try
                {
                    await semaphore.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var commands = scope.ServiceProvider.GetServices<IMessageCommand>();
                    var command = commands.FirstOrDefault(c => c.CanHandle(text, stateInfo.State));

                    if (command != null)
                        await command.ExecuteAsync(botClient, message, stateInfo, cancellationToken);
                    else await botClient.SendMessage(chatId, TelegramUiElements.DefaultCommandsPrompt, cancellationToken: cancellationToken);
                }
                catch (RecipeScribeException ex)
                {
                    await NotifyErrorAsync(botClient, chatId, ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message from {ChatId}", chatId);
                    await botClient.SendMessage(chatId, "Произошла ошибка. Попробуйте другой URL или команду.", cancellationToken: CancellationToken.None);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        private Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message is not { } message)
                return Task.CompletedTask;

            long chatId = message.Chat.Id;
            string data = callbackQuery.Data ?? string.Empty;

            var stateInfo = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());
            var semaphore = _chatLocks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));

            _ = Task.Run(async () =>
            {
                try
                {
                    await semaphore.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

                    using var scope = _scopeFactory.CreateScope();
                    var callbacks = scope.ServiceProvider.GetServices<ICallbackQuery>();
                    var callback = callbacks.FirstOrDefault(c => c.CanHandle(data));

                    if (callback != null)
                        await callback.ExecuteAsync(botClient, callbackQuery, stateInfo, cancellationToken);
                }
                catch (RecipeScribeException ex)
                {
                    await NotifyErrorAsync(botClient, chatId, ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing callback from {ChatId}", chatId);
                    await botClient.SendMessage(chatId, "Произошла ошибка. Попробуйте ещё раз.", cancellationToken: CancellationToken.None);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Ошибка Telegram API");
            return Task.CompletedTask;
        }

        private async Task NotifyErrorAsync(ITelegramBotClient botClient, long chatId, RecipeScribeException ex)
        {
            _logger.LogError(ex, "Error processing request for {ChatId}: {ErrorType}", chatId, ex.Type);

            string msg = ex.Type switch
            {
                ErrorType.Network => "Произошла ошибка при загрузке видео",
                ErrorType.VideoNotFound => "Видео не найдено или недоступно",
                ErrorType.LlmFailure => "Не удалось обработать рецепт (ошибка ИИ)",
                ErrorType.ParseError => "Бот не смог распознать структуру рецепта",
                ErrorType.TranscriptionFailed => "Не удалось распознать речь",
                _ => "Неизвестная ошибка"
            };

            await botClient.SendMessage(chatId, $"{msg}", cancellationToken: CancellationToken.None);
        }
    }
}
