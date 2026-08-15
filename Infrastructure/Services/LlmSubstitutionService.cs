using System.Text.Json;
using Core.Contracts;
using Core.Models;
using Infrastructure.Helpers;
using Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Infrastructure.Services;

public class LlmSubstitutionService : IIngredientSubstitutor
{
    private readonly Kernel _kernel;
    private readonly LlmSettings _llmSettings;
    private readonly ILogger<LlmSubstitutionService> _logger;

    public LlmSubstitutionService(Kernel kernel, LlmSettings llmSettings, ILogger<LlmSubstitutionService> logger)
    {
        _kernel = kernel;
        _llmSettings = llmSettings;
        _logger = logger;
    }

    public async Task<List<SubstitutionSuggestion>> GetSuggestionsAsync(
        string ingredient,
        string recipeTitle,
        IReadOnlyList<string>? otherIngredients = null,
        IReadOnlyList<string>? stepDescriptions = null,
        CancellationToken cancellationToken = default)
    {
        string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "IngredientSubstituter.md");
        string promptTemplate = await File.ReadAllTextAsync(promptPath, cancellationToken);

        string otherList = otherIngredients is { Count: > 0 }
            ? string.Join(", ", otherIngredients.Distinct())
            : "—";

        string relevantSteps = stepDescriptions is { Count: > 0 }
            ? string.Join("\n", stepDescriptions.Where(s => s.Contains(ingredient, StringComparison.OrdinalIgnoreCase)))
            : "—";
        if (string.IsNullOrWhiteSpace(relevantSteps))
            relevantSteps = "—";

        string prompt = promptTemplate
            .Replace("{ingredient}", ingredient)
            .Replace("{recipeTitle}", recipeTitle)
            .Replace("{otherIngredients}", otherList)
            .Replace("{relevantSteps}", relevantSteps)
            .Replace("{targetLanguage}", _llmSettings.TargetLanguage);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.3f
        };

        var result = await LlmRetryHelper.CallWithRetryAsync(_kernel, prompt, executionSettings, _logger, "Замена", cancellationToken);

        var json = JsonTextCleaner.StripCodeFence(result);

        try
        {
            var suggestions = JsonSerializer.Deserialize<List<SubstitutionSuggestion>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return suggestions ?? new List<SubstitutionSuggestion>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse substitution suggestions JSON: {Json}", json);
            return new List<SubstitutionSuggestion>
            {
                new() { Name = result, Description = "Предложенный вариант замены" }
            };
        }
    }
}
