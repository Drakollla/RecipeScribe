using Core.Contracts;
using Core.Models;
using Infrastructure.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Text.Json;

namespace Infrastructure.Services;

public class ScalingService : IScalingService
{
    private readonly Kernel _kernel;
    private readonly ILogger<ScalingService> _logger;

    public ScalingService(Kernel kernel, ILogger<ScalingService> logger)
    {
        _kernel = kernel;
        _logger = logger;
    }

    public async Task<List<Ingredient>> ScaleIngredientsAsync(Recipe recipe, int targetServings, CancellationToken ct = default)
    {
        const int DefaultServings = 2;

        var originalServings = recipe.Servings > 0 ? recipe.Servings : DefaultServings;

        if (targetServings <= 0 || targetServings == originalServings)
            return recipe.Ingredients;

        string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "IngredientScaler.md");
        string promptTemplate = await File.ReadAllTextAsync(promptPath, ct);

        var ingredientsJson = JsonSerializer.Serialize(recipe.Ingredients.Select(i => new { i.Name, i.Amount }));
        string prompt = promptTemplate
            .Replace("{originalServings}", originalServings.ToString())
            .Replace("{targetServings}", targetServings.ToString())
            .Replace("{ingredientsJson}", ingredientsJson);

        _logger.LogInformation("[Scale] LLM scaling {Recipe} from {From} to {To}", recipe.Title, originalServings, targetServings);

        var result = await LlmRetryHelper.CallWithRetryAsync(_kernel, prompt, logPrefix: "Scaling", ct: ct);

        var clean = JsonTextCleaner.StripCodeFence(result);

        var scaled = JsonSerializer.Deserialize<List<ScaledIngredient>>(clean, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (scaled == null || scaled.Count == 0)
        {
            _logger.LogWarning("LLM scaling returned no valid ingredients for {Recipe} ({From}→{To})", recipe.Title, recipe.Servings, targetServings);
            return recipe.Ingredients;
        }

        return scaled.Select(s => new Ingredient { Name = s.Name, Amount = s.Amount }).ToList();
    }

    private record ScaledIngredient(string Name, string Amount);
}