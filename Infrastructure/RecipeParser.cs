using Core.Contracts;
using Core.Models;
using Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure
{
    public class RecipeParser : IRecipeParser
    {
        private readonly LlmService _llmClient;
        private readonly LlmSettings _llmSettings;
        private readonly ILogger<RecipeParser> _logger;

        public RecipeParser(Kernel kernel, LlmSettings llmSettings, ILogger<RecipeParser> logger)
        {
            _llmClient = new LlmService(kernel);
            _llmSettings = llmSettings;
            _logger = logger;
        }

        public async Task<Recipe> ParseRecipeAsync(string transcript)
        {
            string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "RecipeParser.txt");
            string promptTemplate = await File.ReadAllTextAsync(promptPath);

            string fullPrompt = promptTemplate
                .Replace("{transcript}", transcript)
                .Replace("{language}", _llmSettings.TargetLanguage);

            _logger.LogDebug("Текст, отправленный в ИИ:\n{Transcript}", transcript);

            string responseText;
            try
            {
                responseText = await _llmClient.InitialChatAsync(fullPrompt);
            }
            catch (Exception ex)
            {
                throw new RecipeScribeException(ErrorType.LlmFailure,
                    "Ошибка при обращении к LLM", ex);
            }

            _logger.LogDebug("Сырой ответ от ИИ:\n{Response}", responseText);

            responseText = responseText.Trim();
            int firstBrace = responseText.IndexOf('{');
            int lastBrace = responseText.LastIndexOf('}');

            if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
                responseText = responseText.Substring(firstBrace, lastBrace - firstBrace + 1);

            responseText = Regex.Replace(responseText, @"\},\s*""([A-Za-z0-9_]+)""\s*:", @"},{""$1"":");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            try
            {
                var recipe = JsonSerializer.Deserialize<Recipe>(responseText, options);
                return recipe ?? new Recipe { Title = "Не удалось распознать рецепт" };
            }
            catch (JsonException ex)
            {
                throw new RecipeScribeException(ErrorType.ParseError,
                    $"LLM вернул невалидный JSON: {ex.Message}", ex);
            }
        }
    }
}
