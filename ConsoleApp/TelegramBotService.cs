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

            if (stateInfo.State == BotState.WaitingForCustomDate)
            {
                await HandleWaitingForCustomDateAsync(botClient, chatId, text, stateInfo, cancellationToken);
                return;
            }

            if (stateInfo.State == BotState.WaitingForAiPreferences)
            {
                HandleWaitingForAiPreferences(botClient, chatId, text, stateInfo, cancellationToken);
                return;
            }

            if (text.StartsWith('/'))
            {
                await HandleCommandAsync(botClient, chatId, text, stateInfo, cancellationToken);
                return;
            }

            if (stateInfo.State == BotState.WaitingForSearchIngredients)
            {
                HandleWaitingForSearchIngredients(botClient, chatId, text, stateInfo, cancellationToken);
                return;
            }

            await HandleTextOrUrlAsync(botClient, chatId, text, cancellationToken);
        }

        private async Task HandleWaitingForCustomDateAsync(ITelegramBotClient botClient, long chatId, string text, UserStateInfo stateInfo, CancellationToken cancellationToken)
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
        }

        private void HandleWaitingForAiPreferences(ITelegramBotClient botClient, long chatId, string text, UserStateInfo stateInfo, CancellationToken cancellationToken)
        {
            stateInfo.State = BotState.None;
            stateInfo.LastPreferences = text;

            RunScoped(async sp =>
            {
                var flow = sp.GetRequiredService<TelegramMealPlanFlow>();
                await flow.ProcessAiPlanningAsync(botClient, chatId, stateInfo.TargetDate, text, cancellationToken);
            }, cancellationToken);
        }

        private void HandleWaitingForSearchIngredients(ITelegramBotClient botClient, long chatId, string text, UserStateInfo stateInfo, CancellationToken cancellationToken)
        {
            stateInfo.State = BotState.None;

            RunScoped(async sp =>
            {
                var flow = sp.GetRequiredService<TelegramRecipeFlow>();
                await flow.ProcessSearchByIngredientsAsync(botClient, chatId, text, cancellationToken);
            }, cancellationToken);
        }

        private async Task HandleCommandAsync(ITelegramBotClient botClient, long chatId, string text, UserStateInfo stateInfo, CancellationToken cancellationToken)
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
                    break;

                case "/plan_ai":
                    stateInfo.State = BotState.None;
                    await botClient.SendMessage(
                        chatId,
                        "На какой день вы хотите запланировать меню?",
                        replyMarkup: TelegramUiElements.GetDateSelectionKeyboard(),
                        cancellationToken: cancellationToken
                    );
                    break;

                case "/search":
                    stateInfo.State = BotState.WaitingForSearchIngredients;
                    await botClient.SendMessage(
                        chatId,
                        TelegramUiElements.SearchPrompt,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken
                    );
                    break;

                default:
                    await botClient.SendMessage(
                        chatId,
                        TelegramUiElements.DefaultCommandsPrompt,
                        cancellationToken: cancellationToken
                    );
                    break;
            }
        }

        private async Task HandleTextOrUrlAsync(ITelegramBotClient botClient, long chatId, string text, CancellationToken cancellationToken)
        {
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

            string data = callbackQuery.Data ?? string.Empty;

            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

            if (data.StartsWith("plan_date:"))
                await HandleDateSelectedAsync(botClient, message, data, cancellationToken);
            else if (data == "pref_none")
                await HandlePreferencesSkippedAsync(botClient, message, cancellationToken);
            else if (data == "regenerate_ai")
                await HandleRegenerateMenuAsync(botClient, message, cancellationToken);
            else if (data == "confirm_menu")
                HandleConfirmMenu(botClient, message, cancellationToken);
            else if (data.StartsWith("shopping_list:"))
                HandleGetShoppingList(botClient, message, data, cancellationToken);
            else if (data.StartsWith("show_recipe:"))
                HandleShowRecipe(botClient, message, data, cancellationToken);
        }

        private async Task HandleDateSelectedAsync(ITelegramBotClient botClient, Message message, string data, CancellationToken cancellationToken)
        {
            long chatId = message.Chat.Id;
            int messageId = message.Id;
            string dateType = data.Split(':')[1];
            DateOnly targetDate;

            if (dateType == "today")
                targetDate = DateOnly.FromDateTime(DateTime.Today);
            else if (dateType == "tomorrow")
                targetDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            else
            {
                var stateInfo = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());
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

            var userState = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());
            userState.TargetDate = targetDate;
            userState.State = BotState.WaitingForAiPreferences;

            await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);
            await botClient.SendMessage(
                chatId,
                TelegramUiElements.GetPreferencesPrompt(targetDate),
                parseMode: ParseMode.Markdown,
                replyMarkup: TelegramUiElements.GetPreferencesKeyboard(),
                cancellationToken: cancellationToken
            );
        }

        private async Task HandlePreferencesSkippedAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            long chatId = message.Chat.Id;
            int messageId = message.Id;

            var stateInfo = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());
            stateInfo.State = BotState.None;
            stateInfo.LastPreferences = TelegramUiElements.DefaultPreferences;

            await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);

            RunScoped(async sp =>
            {
                var flow = sp.GetRequiredService<TelegramMealPlanFlow>();
                await flow.ProcessAiPlanningAsync(botClient, chatId, stateInfo.TargetDate, TelegramUiElements.DefaultPreferences, cancellationToken);
            }, cancellationToken);
        }

        private async Task HandleRegenerateMenuAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            long chatId = message.Chat.Id;
            int messageId = message.Id;

            var stateInfo = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());
            string prefs = string.IsNullOrWhiteSpace(stateInfo.LastPreferences) ? TelegramUiElements.DefaultPreferences : stateInfo.LastPreferences;

            await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);

            RunScoped(async sp =>
            {
                var flow = sp.GetRequiredService<TelegramMealPlanFlow>();
                await flow.ProcessAiPlanningAsync(botClient, chatId, stateInfo.TargetDate, prefs, cancellationToken);
            }, cancellationToken);
        }

        private void HandleConfirmMenu(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            long chatId = message.Chat.Id;
            int messageId = message.Id;

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

        private void HandleGetShoppingList(ITelegramBotClient botClient, Message message, string data, CancellationToken cancellationToken)
        {
            long chatId = message.Chat.Id;
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

        private void HandleShowRecipe(ITelegramBotClient botClient, Message message, string data, CancellationToken cancellationToken)
        {
            long chatId = message.Chat.Id;
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