using Core.Helpers;
using Core.Models;
using Shared.DTOs;
using System.Net;
using System.Text;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBot.Contracts;

namespace TelegramBot;

public class TelegramRecipeFlow
{
    private readonly IRecipeApiClient _recipeApi;
    private readonly ILogger<TelegramRecipeFlow> _logger;

    public TelegramRecipeFlow(IRecipeApiClient recipeApi, ILogger<TelegramRecipeFlow> logger)
    {
        _recipeApi = recipeApi;
        _logger = logger;
    }

    public async Task ProcessVideoRecipeAsync(ITelegramBotClient botClient, long chatId, string url, CancellationToken cancellationToken)
    {
        var statusMessage = await botClient.SendMessage(chatId, "Извлекаю рецепт из видео...", cancellationToken: cancellationToken);

        try
        {
            await botClient.EditMessageText(chatId, statusMessage.Id, "Обрабатываю видео...", cancellationToken: cancellationToken);

            var recipe = await _recipeApi.ExtractRecipeAsync(url, cancellationToken);

            if (recipe == null)
            {
                await botClient.EditMessageText(chatId, statusMessage.Id, "Ошибка: не удалось извлечь рецепт.", cancellationToken: cancellationToken);
                return;
            }

            await botClient.DeleteMessage(chatId, statusMessage.Id, cancellationToken: cancellationToken);

            var (stream, fileName) = BuildRecipeDocument(recipe);
            await using (stream.ConfigureAwait(false))
                await botClient.SendDocument(
                    chatId: chatId,
                    document: InputFile.FromStream(stream, fileName),
                    caption: $"<b>Рецепт:</b> <i>{HtmlHelper.Escape(recipe.Title)}</i>",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                    replyMarkup: ExportButton(recipe.Id),
                    cancellationToken: cancellationToken
                );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "API error extracting recipe from {Url}", url);
            var msg = ex.StatusCode switch
            {
                HttpStatusCode.NotFound => "Видео не найдено.",
                HttpStatusCode.BadGateway => "Внешний сервис временно недоступен (LLM или сеть). Попробуйте позже.",
                HttpStatusCode.UnprocessableEntity => "Рецепт не удалось распарсить из видео.",
                _ => $"Ошибка API: {ex.Message}"
            };
            await botClient.EditMessageText(chatId, statusMessage.Id, msg, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при извлечении {Url}", url);
            await botClient.EditMessageText(chatId, statusMessage.Id, "Произошла ошибка. Попробуйте другой URL или повторите позже.", cancellationToken: cancellationToken);
        }
    }

    public async Task ProcessSearchByIngredientsAsync(ITelegramBotClient botClient, long chatId, string inputText, CancellationToken cancellationToken)
    {
        try
        {
            var matchingRecipes = await _recipeApi.SearchRecipesAsync(inputText, cancellationToken);

            if (matchingRecipes.Count == 0)
            {
                await botClient.SendMessage(chatId, "Подходящих рецептов в базе не найдено.", cancellationToken: cancellationToken);
                return;
            }

            var buttons = new List<List<InlineKeyboardButton>>();

            foreach (var recipe in matchingRecipes)
                buttons.Add([InlineKeyboardButton.WithCallbackData(recipe.Title, $"show_recipe:{recipe.Id}")]);

            var keyboard = new InlineKeyboardMarkup(buttons);

            await botClient.SendMessage(
                chatId: chatId,
                text: $"Найдено подходящих рецептов: {matchingRecipes.Count}. Выберите один для просмотра:",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "API error searching recipes");
            await botClient.SendMessage(chatId, $"Ошибка при поиске: {ex.Message}", cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при поиске рецептов");
            await botClient.SendMessage(chatId, "Произошла ошибка при поиске.", cancellationToken: cancellationToken);
        }
    }

    public async Task SendRecipeDocumentAsync(ITelegramBotClient botClient, long chatId, Guid recipeId, CancellationToken cancellationToken)
    {
        try
        {
            var recipe = await _recipeApi.GetRecipeByIdAsync(recipeId, cancellationToken);

            if (recipe == null)
            {
                await botClient.SendMessage(chatId, "Не удалось найти рецепт в базе данных.", cancellationToken: cancellationToken);
                return;
            }

            var (stream, fileName) = BuildRecipeDocument(recipe);
            await using (stream.ConfigureAwait(false))
                await botClient.SendDocument(
                    chatId: chatId,
                    document: InputFile.FromStream(stream, fileName),
                    caption: $"<b>Рецепт:</b> <i>{HtmlHelper.Escape(recipe.Title)}</i>",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                    replyMarkup: ExportButton(recipe.Id),
                    cancellationToken: cancellationToken
                );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "API error getting recipe {Id}", recipeId);
            await botClient.SendMessage(chatId, $"Ошибка загрузки рецепта: {ex.Message}", cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при загрузке рецепта {Id}", recipeId);
            await botClient.SendMessage(chatId, "Произошла ошибка при загрузке рецепта.", cancellationToken: cancellationToken);
        }
    }

    public string FormatRecipeToMarkdown(RecipeDto dto)
    {
        var recipe = new Recipe
        {
            Title = dto.Title,
            Ingredients = dto.Ingredients.Select(i => new Ingredient { Name = i.Name, Amount = i.Amount }).ToList(),
            Steps = dto.Steps.Select(s => new RecipeStep { Number = s.Number, Description = s.Description }).ToList(),
            PreparationTips = dto.PreparationTips is { Count: > 0 }
                ? JsonSerializer.Serialize(dto.PreparationTips.Select(t => new PreparationTip { Ingredient = t.Ingredient, Tip = t.Tip }).ToList())
                : null
        };

        return RecipeMarkdownBuilder.Build(recipe);
    }

    private (MemoryStream Stream, string FileName) BuildRecipeDocument(RecipeDto recipe)
    {
        string formattedRecipe = FormatRecipeToMarkdown(recipe);
        byte[] mdBytes = Encoding.UTF8.GetBytes(formattedRecipe);

        var invalid = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(recipe.Title.Select(c => invalid.Contains(c) ? '_' : c));
        
        if (safeName.Length > 100) 
            safeName = safeName[..100];
        
        safeName = safeName.TrimEnd('.');
        
        if (string.IsNullOrWhiteSpace(safeName)) 
            safeName = "recipe";

        return (new MemoryStream(mdBytes), $"{safeName}.md");
    }

    private static InlineKeyboardMarkup ExportButton(Guid recipeId)
    {
        return new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("Сохранить в Obsidian", $"export_obsidian:{recipeId}")]
        ]);
    }
}
