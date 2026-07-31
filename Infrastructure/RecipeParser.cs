using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Core.Models;
using Infrastructure.Helpers;
using Infrastructure.Services;
using Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure;

public class RecipeParser : IRecipeParser
{
    private readonly Kernel _kernel;
    private readonly LlmSettings _llmSettings;
    private readonly ILogger<RecipeParser> _logger;

    public RecipeParser(Kernel kernel, LlmSettings llmSettings, ILogger<RecipeParser> logger)
    {
        _kernel = kernel;
        _llmSettings = llmSettings;
        _logger = logger;
    }

    public async Task<List<Recipe>> ParseRecipesAsync(string transcript, CancellationToken ct = default)
    {
        _logger.LogDebug("Текст, отправленный в ИИ:\n{Transcript}", transcript);

        string fullPrompt = await BuildPromptAsync(transcript);
        string responseText = await CallLlmAsync(fullPrompt, ct);

        _logger.LogDebug("Сырой ответ от ИИ:\n{Response}", responseText);

        string validJson = RecoverJson(responseText);

        using var doc = JsonDocument.Parse(validJson);
        var root = doc.RootElement;

        return root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().Select(ParseSingleRecipe).ToList()
            : new List<Recipe> { ParseSingleRecipe(root) };
    }

    private async Task<string> BuildPromptAsync(string transcript)
    {
        string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "RecipeParser.md");
        string promptTemplate = await File.ReadAllTextAsync(promptPath);

        return promptTemplate
            .Replace("{transcript}", transcript)
            .Replace("{language}", _llmSettings.TargetLanguage);
    }

    private async Task<string> CallLlmAsync(string prompt, CancellationToken ct)
    {
        try
        {
            var settings = new OpenAIPromptExecutionSettings();
            return await LlmRetryHelper.CallWithRetryAsync(_kernel, prompt, settings, ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RecipeScribeException(ErrorType.LlmFailure,
                "Error calling LLM", ex);
        }
    }

    private static string RecoverJson(string raw)
    {
        var text = JsonTextCleaner.StripCodeFence(raw);
        text = Regex.Replace(text, @"\}(\s*)\{", @"},$1{");

        var parseError = TryParseJson(text);

        if (parseError != null && !text.StartsWith("["))
        {
            text = "[" + text + "]";
            parseError = TryParseJson(text);
        }

        if (parseError != null && text.StartsWith("["))
        {
            var truncated = JsonTextCleaner.TruncateToLastCompleteObject(text);
            if (truncated == null)
                throw new RecipeScribeException(ErrorType.ParseError, "LLM returned invalid JSON: empty");

            text = truncated;
            parseError = TryParseJson(text);
        }

        if (parseError != null)
            throw new RecipeScribeException(ErrorType.ParseError,
                $"LLM returned invalid JSON: {parseError}");

        return text;
    }

    private static string? TryParseJson(string s)
    {
        try { using var _ = JsonDocument.Parse(s); return null; }
        catch (JsonException e) { return e.Message; }
    }

    private static Recipe ParseSingleRecipe(JsonElement root)
    {
        var recipe = new Recipe
        {
            Title = GetProp(root, "Title", "Название")?.GetString() ?? "Неизвестный рецепт",
            Servings = TryGetInt(GetProp(root, "Servings", "Порций")) ?? 2,
            IsBreakfast = GetBool(GetProp(root, "IsBreakfast", "ДляЗавтрака")),
            IsLunch = GetBool(GetProp(root, "IsLunch", "ДляОбеда")),
            IsDinner = GetBool(GetProp(root, "IsDinner", "ДляУжина")),
            IsSnack = GetBool(GetProp(root, "IsSnack", "ДляПерекуса")),
        };

        recipe.PreparationTips = GetRawTextOfKind(root, JsonValueKind.Array, "PreparationTips", "СоветыПоПодготовке");
        recipe.NutritionJson = GetRawTextOfKind(root, JsonValueKind.Object, "Nutrition", "ПитательнаяЦенность");
        recipe.Ingredients.AddRange(ParseIngredients(GetArray(root, "Ingredients", "Ингредиенты")));
        recipe.Steps.AddRange(ParseSteps(GetArray(root, "Steps", "Шаги")));

        return recipe;
    }

    private static List<Ingredient> ParseIngredients(JsonElement? prop)
    {
        var ingredients = new List<Ingredient>();
        
        if (prop == null) 
            return ingredients;

        foreach (var item in prop.Value.EnumerateArray())
        {
            ingredients.Add(new Ingredient
            {
                Name = GetProp(item, "Name", "НазваниеИнгредиента", "Ингредиент")?.GetString() ?? string.Empty,
                Amount = GetProp(item, "Amount", "Количество")?.GetString() ?? string.Empty
            });
        }

        return ingredients;
    }

    private static List<RecipeStep> ParseSteps(JsonElement? prop)
    {
        var steps = new List<RecipeStep>();
        
        if (prop == null) 
            return steps;

        foreach (var item in prop.Value.EnumerateArray())
        {
            steps.Add(new RecipeStep
            {
                Number = GetStepNumber(item),
                Description = GetProp(item, "Description", "Описание")?.GetString() ?? string.Empty
            });
        }

        return steps;
    }

    private static int GetStepNumber(JsonElement item) =>
        item.TryGetProperty("Number", out var num) ? num.GetInt32()
        : item.TryGetProperty("Номер", out var numRu) ? numRu.GetInt32() : 0;

    private static JsonElement? GetArray(JsonElement element, params string[] names)
    {
        var prop = GetProp(element, names);
        return prop.HasValue && prop.Value.ValueKind == JsonValueKind.Array ? prop : null;
    }

    private static string? GetRawTextOfKind(JsonElement element, JsonValueKind kind, params string[] names)
    {
        var prop = GetProp(element, names);
        return prop.HasValue && prop.Value.ValueKind == kind ? prop.Value.GetRawText() : null;
    }

    private static JsonElement? GetProp(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value))
                return value;
        return null;
    }

    private static int? TryGetInt(JsonElement? element)
    {
        if (element.HasValue && element.Value.TryGetInt32(out var val))
            return val;
        return null;
    }

    private static bool GetBool(JsonElement? element) =>
        element.HasValue && element.Value.GetBoolean();
}
