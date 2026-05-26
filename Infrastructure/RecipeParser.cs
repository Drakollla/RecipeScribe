using Core.Models;
using System.Text.Json;

namespace Infrastructure
{
    public class RecipeParser
    {
        private readonly LlmService _llmClient;

        public RecipeParser(LlmService llmClient)
        {
            _llmClient = llmClient;
        }

        public async Task<Recipe> ParseRecipeAsync(string transcript)
        {
            string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "RecipeParser.txt");
            string promptTemplate = await File.ReadAllTextAsync(promptPath);
            string fullPrompt = promptTemplate.Replace("{transcript}", transcript);

            string responseText = await _llmClient.InitialChatAsync(fullPrompt);
            responseText = responseText.Trim();

            int firstBrace = responseText.IndexOf('{');
            int lastBrace = responseText.LastIndexOf('}');

            if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
                responseText = responseText.Substring(firstBrace, lastBrace - firstBrace + 1);

            //Console.WriteLine($"\n[DEBUG] Сырой ответ от ИИ:\n{responseText}\n");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            return JsonSerializer.Deserialize<Recipe>(responseText, options)
                   ?? new Recipe { Title = "Не удалось распознать рецепт" };
        }
    }
}
