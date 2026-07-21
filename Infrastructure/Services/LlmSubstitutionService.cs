using Core.Contracts;
using Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Infrastructure.Services;

public class LlmSubstitutionService : IIngredientSubstitutor
{
    private readonly Kernel _kernel;
    private readonly IOptions<LlmSettings> _llmSettings;
    private readonly ILogger<LlmSubstitutionService> _logger;

    public LlmSubstitutionService(Kernel kernel, IOptions<LlmSettings> llmSettings, ILogger<LlmSubstitutionService> logger)
    {
        _kernel = kernel;
        _llmSettings = llmSettings;
        _logger = logger;
    }

    public async Task<string> GetSubstitutionsAsync(string ingredient, string recipeTitle, CancellationToken cancellationToken = default)
    {
        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        var prompt = $"В рецепте \"{recipeTitle}\" есть ингредиент \"{ingredient}\". " +
                     $"Предложи 3 варианта замены с кратким пояснением почему. " +
                     $"Ответь строго на языке: {_llmSettings.Value.TargetLanguage}. " +
                     $"Формат ответа:\n1. {{вариант}} — {{почему}}\n2. {{вариант}} — {{почему}}\n3. {{вариант}} — {{почему}}";

        var history = new ChatHistory();
        history.AddUserMessage(prompt);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.3f
        };

        var response = await chatService.GetChatMessageContentAsync(history, executionSettings, _kernel);
        var result = response.Content ?? "Не удалось получить варианты замены.";

        _logger.LogInformation("Замена для {Ingredient} в рецепте {Recipe}: {Result}", ingredient, recipeTitle, result);

        return result;
    }
}
