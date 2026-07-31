using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Core.Models;
using Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Globalization;
using System.Text;

namespace Infrastructure.Services;

public class MealPlannerService : IMealPlannerService
{
    private readonly IMealPlanRepository _repo;
    private readonly IScalingService _scalingService;
    private readonly Kernel _kernel;
    private readonly LlmSettings _llmSettings;
    private readonly ILogger<MealPlannerService> _logger;

    public MealPlannerService(IMealPlanRepository repo,
        IScalingService scalingService,
        Kernel kernel,
        LlmSettings llmSettings,
        ILogger<MealPlannerService> logger)
    {
        _repo = repo;
        _scalingService = scalingService;
        _kernel = kernel;
        _llmSettings = llmSettings;
        _logger = logger;
    }

    public async Task<MealPlan> CreatePlanManualAsync(long telegramChatId, DateOnly date, Dictionary<MealType, Guid> mealRecipes)
    {
        var user = await _repo.GetOrCreateUserAsync(telegramChatId);
        var portions = user.DefaultServings > 0 ? user.DefaultServings : 2;

        var newPlan = new MealPlan
        {
            Id = Guid.NewGuid(),
            Date = date,
            UserId = user.Id
        };

        foreach (var (mealType, recipeId) in mealRecipes)
        {
            newPlan.Items.Add(new MealPlanItem
            {
                Id = Guid.NewGuid(),
                RecipeId = recipeId,
                MealType = mealType,
                Portions = portions
            });
        }

        return await _repo.CreatePlanAsync(newPlan);
    }

    public async Task<MealPlan> GenerateAutoPlanAsync(long telegramChatId, DateOnly date, string userRequest)
    {
        var user = await _repo.GetOrCreateUserAsync(telegramChatId);
        var portions = user.DefaultServings > 0 ? user.DefaultServings : 2;

        var selectedIds = new List<Guid>();
        var mealRecipes = new Dictionary<MealType, Guid>();

        var mealTypes = new[] { MealType.Breakfast, MealType.Lunch, MealType.Dinner };

        foreach (var mealType in mealTypes)
        {
            var recipe = await _repo.GetRecipeByMealTypeAsync(mealType, selectedIds);
            if (recipe != null)
            {
                selectedIds.Add(recipe.Id);
                mealRecipes[mealType] = recipe.Id;
            }
        }

        if (mealRecipes.Count == 0)
            throw new RecipeScribeException(ErrorType.ParseError, "No recipes with meal-type flags found. Add flags to recipes first.");

        var plan = await CreatePlanManualAsync(telegramChatId, date, mealRecipes);

        foreach (var id in selectedIds)
            await _repo.UpdateRecipeLastPlannedAtAsync(id);

        return plan;
    }

    public async Task<MealPlan?> GetPlanForDateAsync(long telegramChatId, DateOnly date) =>
        await _repo.GetPlanForDateAsync(telegramChatId, date);

    public async Task<string> GetShoppingListAsync(Guid mealPlanId)
    {
        var planItems = await _repo.GetPlanItemsWithRecipesAsync(mealPlanId);
        var scaledIngredients = await ScaleAllIngredientsAsync(planItems);

        if (!scaledIngredients.Any())
            return "*Список покупок пуст.*";

        var flatListString = BuildFlatIngredientList(scaledIngredients);

        try
        {
            var categorizedList = await CategorizeShoppingListAsync(flatListString);
            return $"*СПИСОК ПОКУПОК ПО ОТДЕЛАМ:*\n\n{FormatCategorizedList(categorizedList)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Ошибка ИИ при категоризации списка покупок]");
            return BuildFallbackShoppingList(flatListString);
        }
    }

    private async Task<List<Ingredient>> ScaleAllIngredientsAsync(List<MealPlanItem> planItems)
    {
        var scaledIngredients = new List<Ingredient>();

        foreach (var item in planItems)
        {
            var ingredients = await _scalingService.ScaleIngredientsAsync(item.Recipe, item.Portions);
            scaledIngredients.AddRange(ingredients);
        }

        return scaledIngredients;
    }

    private static string BuildFlatIngredientList(List<Ingredient> ingredients)
    {
        var flatList = ingredients
            .GroupBy(i => i.Name.Trim().ToLowerInvariant())
            .Select(FormatIngredientGroup)
            .OrderBy(item => item)
            .ToList();

        return string.Join("\n", flatList.Select(item => $"• {item}"));
    }

    private static string FormatIngredientGroup(IGrouping<string, Ingredient> group)
    {
        var amounts = group.Select(i => i.Amount.Trim())
                           .Where(a => !string.IsNullOrWhiteSpace(a))
                           .Distinct()
                           .ToList();

        var name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(group.Key);
        var amountText = amounts.Any() ? $" ({string.Join(" + ", amounts)})" : "";

        return $"{name}{amountText}";
    }

    private async Task<string> CategorizeShoppingListAsync(string flatListString)
    {
        string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "ShoppingListCategorizer.md");
        string promptTemplate = await File.ReadAllTextAsync(promptPath);

        string prompt = promptTemplate
            .Replace("{flatListString}", flatListString)
            .Replace("{targetLanguage}", _llmSettings.TargetLanguage);

        var executionSettings = new OpenAIPromptExecutionSettings { Temperature = _llmSettings.Temperature };
        var rawResponse = await LlmRetryHelper.CallWithRetryAsync(_kernel, prompt, executionSettings, _logger, "Список покупок");

        var categorizedList = rawResponse.Trim();

        if (string.IsNullOrWhiteSpace(categorizedList))
            throw new RecipeScribeException(ErrorType.LlmFailure, "LLM returned an empty response.");

        return categorizedList;
    }

    private static string FormatCategorizedList(string categorizedList)
    {
        var formatted = new StringBuilder();

        foreach (var rawLine in categorizedList.Split('\n'))
        {
            var line = rawLine.TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (IsDepartmentHeader(line))
            {
                formatted.AppendLine();
                formatted.AppendLine(BoldHeader(line));
            }
            else
            {
                formatted.AppendLine(line);
            }
        }

        return formatted.ToString().Trim();
    }

    private static bool IsDepartmentHeader(string line) =>
        !line.StartsWith('•') && !line.StartsWith('-') && !line.StartsWith('*');

    private static string BoldHeader(string line) =>
        line.StartsWith('*') && line.EndsWith('*') ? line : $"*{line.Trim()}*";

    private static string BuildFallbackShoppingList(string flatListString)
    {
        var listOfIngredients = new StringBuilder();
        listOfIngredients.AppendLine("*СПИСОК ПОКУПОК (без сортировки по отделам):*");
        listOfIngredients.AppendLine("=========================");
        listOfIngredients.AppendLine(flatListString);
        listOfIngredients.AppendLine();
        listOfIngredients.AppendLine("*Не удалось распределить по отделам из-за временного сбоя сети.*");

        return listOfIngredients.ToString();
    }
}
