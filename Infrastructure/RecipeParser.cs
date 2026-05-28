using Core.Contracts;
using Core.Models;
using Infrastructure.Settings;
using Microsoft.SemanticKernel;
using System.Text.Json;

namespace Infrastructure
{
    public class RecipeParser : IRecipeParser
    {
        private readonly LlmService _llmClient;
        private readonly LlmSettings _llmSettings;

        public RecipeParser(Kernel kernel, LlmSettings llmSettings)
        {
            _llmClient = new LlmService(kernel);
            _llmSettings = llmSettings;
        }

        public async Task<Recipe> ParseRecipeAsync(string transcript)
        {
            Console.WriteLine($"\n[DEBUG] Текст, отправленный в ИИ:\n{transcript}\n");

            string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "RecipeParser.txt");
            string promptTemplate = await File.ReadAllTextAsync(promptPath);

            string fullPrompt = promptTemplate
                .Replace("{transcript}", transcript)
                .Replace("{language}", _llmSettings.TargetLanguage);

            string responseText = await _llmClient.InitialChatAsync(fullPrompt);
            responseText = responseText.Trim();

            int firstBrace = responseText.IndexOf('{');
            int lastBrace = responseText.LastIndexOf('}');

            if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
                responseText = responseText.Substring(firstBrace, lastBrace - firstBrace + 1);

            Console.WriteLine($"\n[DEBUG] Сырой ответ от ИИ:\n{responseText}\n");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            return JsonSerializer.Deserialize<Recipe>(responseText, options)
                   ?? new Recipe { Title = "Не удалось распознать рецепт" };
        }
    }
}
