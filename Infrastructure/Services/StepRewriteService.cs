using Core.Contracts;
using Core.Models;
using Core.ValueObjects;
using Infrastructure.Helpers;
using Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;

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

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<StepRewriteResult> RewriteStepsAsync(
        IReadOnlyList<IngredientSubstitution> substitutions,
        string recipeTitle,
        IReadOnlyList<string>? otherIngredients,
        IReadOnlyList<string>? stepDescriptions,
        IReadOnlyList<PreparationTip>? preparationTips,
        CancellationToken cancellationToken = default)
    {
        var prompt = await BuildPromptAsync(substitutions, recipeTitle, otherIngredients, stepDescriptions, preparationTips, cancellationToken);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.3f
        };

        var result = await LlmRetryHelper.CallWithRetryAsync(_kernel, prompt, executionSettings, _logger, "Переписать шаги", cancellationToken);

        return ParseResult(JsonTextCleaner.StripCodeFence(result));
    }

    private async Task<string> BuildPromptAsync(
        IReadOnlyList<IngredientSubstitution> substitutions,
        string recipeTitle,
        IReadOnlyList<string>? otherIngredients,
        IReadOnlyList<string>? stepDescriptions,
        IReadOnlyList<PreparationTip>? preparationTips,
        CancellationToken cancellationToken)
    {
        string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "IngredientStepRewriter.md");
        string template = await File.ReadAllTextAsync(promptPath, cancellationToken);

        return template
            .Replace("{substitutions}", FormatSubstitutions(substitutions))
            .Replace("{recipeTitle}", recipeTitle)
            .Replace("{otherIngredients}", Join(otherIngredients))
            .Replace("{allSteps}", FormatNumberedList(stepDescriptions))
            .Replace("{allTips}", FormatTips(preparationTips))
            .Replace("{targetLanguage}", _llmSettings.TargetLanguage);
    }

    private static string FormatSubstitutions(IReadOnlyList<IngredientSubstitution> substitutions) =>
        string.Join("\n", substitutions.Select(s => $"- {s.Ingredient} → {s.Replacement}"));

    private static string Join(IReadOnlyList<string>? items) =>
        items is { Count: > 0 }
            ? string.Join(", ", items.Distinct())
            : "—";

    private static string FormatNumberedList(IReadOnlyList<string>? items) =>
        items is { Count: > 0 }
            ? string.Join("\n", items.Select((s, i) => $"{i + 1}. {s}"))
            : "—";

    private static string FormatTips(IReadOnlyList<PreparationTip>? tips) =>
        tips is { Count: > 0 }
            ? string.Join("\n", tips.Select((t, i) => $"{i + 1}. {t.Ingredient}: {t.Tip}"))
            : "—";

    private StepRewriteResult ParseResult(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<StepRewriteResult>(json, JsonOptions);

            if (parsed is { Steps.Count: > 0 })
                return parsed;
        }
        catch (JsonException) { }

        var stepsArray = JsonTextCleaner.ExtractArrayMember(json, "steps");

        if (stepsArray != null)
        {
            var recoveredSteps = JsonSerializer.Deserialize<List<RecipeStep>>(stepsArray, JsonOptions);

            if (recoveredSteps is { Count: > 0 })
                return new StepRewriteResult(recoveredSteps, null);
        }

        _logger.LogWarning("Failed to parse rewritten steps JSON: {Json}", json);
        return new StepRewriteResult(new List<RecipeStep>(), null);
    }
}