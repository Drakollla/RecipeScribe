using System.Text.Json;
using Core.Contracts;
using Core.Models;
using Infrastructure.Helpers;
using Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Infrastructure.Services;

public class StepRewriteService : IStepRewriter
{
    private readonly Kernel _kernel;
    private readonly LlmSettings _llmSettings;
    private readonly ILogger<StepRewriteService> _logger;

    public StepRewriteService(Kernel kernel, LlmSettings llmSettings, ILogger<StepRewriteService> logger)
    {
        _kernel = kernel;
        _llmSettings = llmSettings;
        _logger = logger;
    }

    public async Task<List<RecipeStep>> RewriteStepsAsync(
        string ingredient,
        string replacement,
        string recipeTitle,
        IReadOnlyList<string>? otherIngredients,
        IReadOnlyList<string>? stepDescriptions,
        CancellationToken cancellationToken = default)
    {
        string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "IngredientStepRewriter.md");
        string promptTemplate = await File.ReadAllTextAsync(promptPath, cancellationToken);

        string otherList = otherIngredients is { Count: > 0 }
            ? string.Join(", ", otherIngredients.Distinct())
            : "—";

        string allSteps = stepDescriptions is { Count: > 0 }
            ? string.Join("\n", stepDescriptions.Select((s, i) => $"{i + 1}. {s}"))
            : "—";

        string prompt = promptTemplate
            .Replace("{ingredient}", ingredient)
            .Replace("{replacement}", replacement)
            .Replace("{recipeTitle}", recipeTitle)
            .Replace("{otherIngredients}", otherList)
            .Replace("{allSteps}", allSteps)
            .Replace("{targetLanguage}", _llmSettings.TargetLanguage);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.3f
        };

        var result = await LlmRetryHelper.CallWithRetryAsync(_kernel, prompt, executionSettings, _logger, "Переписать шаги", cancellationToken);
        var json = JsonTextCleaner.StripCodeFence(result);

        try
        {
            var steps = JsonSerializer.Deserialize<List<RecipeStep>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return steps ?? new List<RecipeStep>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse rewritten steps JSON: {Json}", json);
            return new List<RecipeStep>();
        }
    }
}
