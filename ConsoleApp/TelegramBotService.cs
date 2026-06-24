using Core.Contracts;
using Core.Enums;
using Core.Helpers;
using Infrastructure.Helpers;
using Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ConsoleApp
{
    public class TelegramBotService : BackgroundService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ConcurrentDictionary<long, UserStateInfo> _userStates = new();

        public TelegramBotService(ITelegramBotClient botClient,
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
                new BotCommand { Command = "plan_ai", Description = "Спланировать меню через ИИ" }
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

            if (stateInfo.State == BotState.WaitingForCustomDate)
            {
                using var scope = _scopeFactory.CreateScope();
                var mealPlanFlow = scope.ServiceProvider.GetRequiredService<TelegramMealPlanFlow>();

                if (mealPlanFlow.TryParseDate(text, out var parsedDate))
                {
                    stateInfo.TargetDate = parsedDate;
                    stateInfo.State = BotState.WaitingForAiPreferences;

                    await botClient.SendMessage(
                        chatId,
                        TelegramUiElements.GetPreferencesPrompt(parsedDate),
                        parseMode: ParseMode.Markdown,
                        replyMarkup: TelegramUiElements.GetPreferencesKeyboard(),
                        cancellationToken: cancellationToken
                    );
                }
                else
                {
                    await botClient.SendMessage(
                        chatId,
                        TelegramUiElements.InvalidDatePrompt,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken
                    );
                }
                return;
            }

            if (stateInfo.State == BotState.WaitingForAiPreferences)
            {
                stateInfo.State = BotState.None;
                stateInfo.LastPreferences = text;

                RunScoped(async sp =>
                {
                    var flow = sp.GetRequiredService<TelegramMealPlanFlow>();
                    await flow.ProcessAiPlanningAsync(botClient, chatId, stateInfo.TargetDate, text, cancellationToken);
                }, cancellationToken);

                return;
            }

            if (text.StartsWith('/'))
            {
                string command = text.ToLower().Split(' ')[0];
                switch (command)
                {
                    case "/menu":
                        RunScoped(async sp =>
                        {
                            var flow = sp.GetRequiredService<TelegramMealPlanFlow>();
                            await flow.ShowTodayMenuAsync(botClient, chatId, cancellationToken);
                        }, cancellationToken);
                        return;

                    case "/plan_ai":
                        stateInfo.State = BotState.None;

                        await botClient.SendMessage(
                            chatId,
                            "На какой день вы хотите запланировать меню?",
                            replyMarkup: TelegramUiElements.GetDateSelectionKeyboard(),
                            cancellationToken: cancellationToken
                        );
                        return;

                    default:
                        await botClient.SendMessage(
                            chatId,
                            TelegramUiElements.DefaultCommandsPrompt,
                            cancellationToken: cancellationToken
                        );
                        return;
                }
            }

            bool isUrl = Uri.TryCreate(text, UriKind.Absolute, out var uriResult)
                         && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

            if (!isUrl)
            {
                await botClient.SendMessage(chatId, "Пожалуйста, отправьте корректную ссылку или выберите команду из меню.", cancellationToken: cancellationToken);
                return;
            }

            RunScoped(async sp =>
            {
                var flow = sp.GetRequiredService<TelegramRecipeFlow>();
                await flow.ProcessVideoRecipeAsync(botClient, chatId, text, cancellationToken);
            }, cancellationToken);
        }

        private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message is not { } message)
                return;

            long chatId = message.Chat.Id;
            int messageId = message.Id;
            string data = callbackQuery.Data ?? string.Empty;

            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

            var stateInfo = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());

            if (data.StartsWith("plan_date:"))
            {
                string dateType = data.Split(':')[1];
                DateOnly targetDate;

                if (dateType == "today")
                {
                    targetDate = DateOnly.FromDateTime(DateTime.Today);
                }
                else if (dateType == "tomorrow")
                {
                    targetDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
                }
                else
                {
                    stateInfo.State = BotState.WaitingForCustomDate;
                    await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);
                    await botClient.SendMessage(
                        chatId,
                        TelegramUiElements.EnterDatePrompt,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken
                    );
                    return;
                }

                stateInfo.TargetDate = targetDate;
                stateInfo.State = BotState.WaitingForAiPreferences;

                await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);
                await botClient.SendMessage(
                    chatId,
                    TelegramUiElements.GetPreferencesPrompt(targetDate),
                    parseMode: ParseMode.Markdown,
                    replyMarkup: TelegramUiElements.GetPreferencesKeyboard(),
                    cancellationToken: cancellationToken
                );
            }
            else if (data == "pref_none")
            {
                stateInfo.State = BotState.None;
                stateInfo.LastPreferences = TelegramUiElements.DefaultPreferences;

                await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);

                RunScoped(async sp =>
                {
                    var flow = sp.GetRequiredService<TelegramMealPlanFlow>();
                    await flow.ProcessAiPlanningAsync(botClient, chatId, stateInfo.TargetDate, TelegramUiElements.DefaultPreferences, cancellationToken);
                }, cancellationToken);
            }
            else if (data == "regenerate_ai")
            {
                string prefs = string.IsNullOrWhiteSpace(stateInfo.LastPreferences) ? TelegramUiElements.DefaultPreferences : stateInfo.LastPreferences;

                await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);

                RunScoped(async sp =>
                {
                    var flow = sp.GetRequiredService<TelegramMealPlanFlow>();
                    await flow.ProcessAiPlanningAsync(botClient, chatId, stateInfo.TargetDate, prefs, cancellationToken);
                }, cancellationToken);
            }
            else if (data == "confirm_menu")
            {
                RunScoped(async sp =>
                {
                    var mealPlannerService = sp.GetRequiredService<IMealPlannerService>();
                    var mealPlanFlow = sp.GetRequiredService<TelegramMealPlanFlow>();

                    var today = DateOnly.FromDateTime(DateTime.Today);
                    var plan = await mealPlannerService.GetPlanForDateAsync(chatId, today);

                    if (plan != null)
                    {
                        string formattedMenu = mealPlanFlow.FormatMenuToMarkdown(plan) + "\nМеню успешно подтверждено!";
                        var keyboard = mealPlanFlow.GetMenuKeyboard(plan, isConfirmed: true);

                        await botClient.EditMessageText(chatId, messageId, formattedMenu, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: cancellationToken);
                    }
                }, cancellationToken);
            }
            else if (data.StartsWith("shopping_list:"))
            {
                string planIdStr = data.Split(':')[1];
                if (Guid.TryParse(planIdStr, out var planId))
                {
                    RunScoped(async sp =>
                    {
                        var mealPlannerService = sp.GetRequiredService<IMealPlannerService>();
                        string shoppingListMarkdown = await mealPlannerService.GetShoppingListAsync(planId);

                        await botClient.SendMessage(
                            chatId,
                            shoppingListMarkdown,
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken
                        );
                    }, cancellationToken);
                }
            }
            else if (data.StartsWith("show_recipe:"))
            {
                string recipeIdStr = data.Split(':')[1];

                if (Guid.TryParse(recipeIdStr, out var recipeId))
                {
                    RunScoped(async sp =>
                    {
                        var recipeFlow = sp.GetRequiredService<TelegramRecipeFlow>();
                        await recipeFlow.SendRecipeDocumentAsync(botClient, chatId, recipeId, cancellationToken);
                    }, cancellationToken);
                }
            }
        }

        private void RunScoped(Func<IServiceProvider, Task> action, CancellationToken cancellationToken)
        {
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();

                try
                {
                    await action(scope.ServiceProvider);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Бот] Ошибка при выполнении фоновой задачи: {ex.Message}");
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