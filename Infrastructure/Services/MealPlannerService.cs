using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Core.Helpers;
using Core.Models;
using Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Services;

public class MealPlannerService : IMealPlannerService
{
    private readonly IMealPlanRepository _repo;
    private readonly Kernel _kernel;
    private readonly LlmSettings _llmSettings;
    private readonly ILogger<MealPlannerService> _logger;

    public MealPlannerService(IMealPlanRepository repo,
        Kernel kernel,
        LlmSettings llmSettings,
        ILogger<MealPlannerService> logger)
    {
        _repo = repo;
        _kernel = kernel;
        _llmSettings = llmSettings;
        _logger = logger;
    }

    public async Task<MealPlan> CreatePlanManualAsync(long telegramChatId, DateOnly date, Dictionary<MealType, Guid> mealRecipes)
    {
        var user = await _repo.GetOrCreateUserAsync(telegramChatId);

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
                Portions = 1
            });
        }

        return await _repo.CreatePlanAsync(newPlan);
    }

    public async Task<MealPlan> GenerateSmartPlanAsync(long telegramChatId, DateOnly date, string userRequest)
    {
        var user = await _repo.GetOrCreateUserAsync(telegramChatId);

        var recentRecipeIds = await _repo.GetRecentRecipeIdsAsync(user.Id, date);
        string? primaryKeyword = GetPrimaryKeyword(userRequest);

        var breakfasts = await _repo.GetCategoryCandidatesAsync(r => r.IsBreakfast, recentRecipeIds, primaryKeyword);
        var lunches = await _repo.GetCategoryCandidatesAsync(r => r.IsLunch, recentRecipeIds, primaryKeyword);
        var dinners = await _repo.GetCategoryCandidatesAsync(r => r.IsDinner, recentRecipeIds, primaryKeyword);

        var allCandidates = breakfasts.Concat(lunches).Concat(dinners).DistinctBy(r => r.Id).ToList();

        if (!allCandidates.Any())
            throw new RecipeScribeException(ErrorType.ParseError, "No recipes found matching the meal type categories.");

        var llmResponse = await AskLlmForPlanAsync(allCandidates, userRequest);
        var mealRecipes = new Dictionary<MealType, Guid>();

        if (Guid.TryParse(llmResponse.Breakfast, out var breakfastId))
            mealRecipes[MealType.Breakfast] = breakfastId;

        if (Guid.TryParse(llmResponse.Lunch, out var lunchId))
            mealRecipes[MealType.Lunch] = lunchId;

        if (Guid.TryParse(llmResponse.Dinner, out var dinnerId))
            mealRecipes[MealType.Dinner] = dinnerId;

        return await CreatePlanManualAsync(telegramChatId, date, mealRecipes);
    }

    private async Task<LlmMealPlanResponse> AskLlmForPlanAsync(List<RecipeCandidate> candidates, string userRequest)
    {
        var recipesJson = JsonSerializer.Serialize(candidates, new JsonSerializerOptions { WriteIndented = false });
        string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "MealPlanner.md");
        string promptTemplate = await File.ReadAllTextAsync(promptPath);

        string prompt = promptTemplate
            .Replace("{recipesList}", recipesJson)
            .Replace("{userRequest}", userRequest)
            .Replace("{targetLanguage}", _llmSettings.TargetLanguage);

        var executionSettings = new OpenAIPromptExecutionSettings { Temperature = _llmSettings.Temperature };

        return await LlmRetryHelper.CallWithRetryAsync(
            async () =>
            {
                var rawResponse = await LlmRetryHelper.CallWithRetryAsync(_kernel, prompt, executionSettings, _logger, "Планировщик");
                var cleanJson = rawResponse.Replace("```json", "").Replace("```", "").Trim();

                var response = JsonSerializer.Deserialize<LlmMealPlanResponse>(cleanJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return response ?? throw new RecipeScribeException(ErrorType.LlmFailure, "LLM returned an empty response.");
            },
            logger: _logger,
            logPrefix: "Планировщик");
    }

    private static string? GetPrimaryKeyword(string userRequest)
    {
        var keywords = userRequest.ToLower()
            .Split(new[] { ' ', ',', '.', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .ToList();

        return keywords.FirstOrDefault();
    }

    public async Task<MealPlan?> GetPlanForDateAsync(long telegramChatId, DateOnly date)
    {
        return await _repo.GetPlanForDateAsync(telegramChatId, date);
    }

    public async Task<string> GetShoppingListAsync(Guid mealPlanId)
    {
        var planItems = await _repo.GetPlanItemsWithRecipesAsync(mealPlanId);

        var allIngredients = planItems
            .SelectMany(mpi => mpi.Recipe.Ingredients)
            .ToList();

        if (!allIngredients.Any())
            return "*Список покупок пуст.*";

        var flatList = allIngredients
            .GroupBy(i => i.Name.Trim().ToLowerInvariant())
            .Select(g =>
            {
                var amounts = g.Select(i => i.Amount.Trim())
                               .Where(a => !string.IsNullOrWhiteSpace(a))
                               .Distinct()
                               .ToList();

                var name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(g.Key);
                var amountText = amounts.Any() ? $" ({string.Join(" + ", amounts)})" : "";

                return $"{name}{amountText}";
            })
            .OrderBy(item => item)
            .ToList();

        var flatListString = string.Join("\n", flatList.Select(item => $"• {item}"));

        string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "ShoppingListCategorizer.md");
        string promptTemplate = await File.ReadAllTextAsync(promptPath);

        string prompt = promptTemplate
            .Replace("{flatListString}", flatListString)
            .Replace("{targetLanguage}", _llmSettings.TargetLanguage);

        try
        {
            var executionSettings = new OpenAIPromptExecutionSettings { Temperature = _llmSettings.Temperature };
            var rawResponse = await LlmRetryHelper.CallWithRetryAsync(_kernel, prompt, executionSettings, _logger, "Список покупок");
            var categorizedList = rawResponse.Trim();

            if (string.IsNullOrWhiteSpace(categorizedList))
                throw new RecipeScribeException(ErrorType.LlmFailure, "LLM returned an empty response.");

            return $"*СПИСОК ПОКУПОК ПО ОТДЕЛАМ:*\n\n{categorizedList}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Ошибка ИИ при категоризации списка покупок]");

            var listOfIngredients = new StringBuilder();
            listOfIngredients.AppendLine("*СПИСОК ПОКУПОК (без сортировки по отделам):*");
            listOfIngredients.AppendLine("=========================");
            listOfIngredients.AppendLine(flatListString);
            listOfIngredients.AppendLine();
            listOfIngredients.AppendLine("*Не удалось распределить по отделам из-за временного сбоя сети.*");

            return listOfIngredients.ToString();
        }
    }
}
