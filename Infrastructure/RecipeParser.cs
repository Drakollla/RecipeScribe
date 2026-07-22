using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Core.Models;
using Infrastructure.Services;
using Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure
{
    public class RecipeParser : IRecipeParser
    {
        private readonly Kernel _kernel;
        private readonly LlmSettings _llmSettings;
        private readonly ILogger<RecipeParser> _logger;

        public RecipeParser(Kernel kernel, LlmSettings llmSettings, ILogger<RecipeParser> logger)
        {
            _kernel = kernel;
            _llmSettings = llmSettings;
            _logger = logger;
        }

        public async Task<Recipe> ParseRecipeAsync(string transcript, CancellationToken ct = default)
        {
            string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "RecipeParser.md");
            string promptTemplate = await File.ReadAllTextAsync(promptPath);

            string fullPrompt = promptTemplate
                .Replace("{transcript}", transcript)
                .Replace("{language}", _llmSettings.TargetLanguage);

            _logger.LogDebug("Текст, отправленный в ИИ:\n{Transcript}", transcript);

            string responseText;
            try
            {
                responseText = await LlmRetryHelper.CallWithRetryAsync(_kernel, fullPrompt, ct: ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new RecipeScribeException(ErrorType.LlmFailure,
                    "Error calling LLM", ex);
            }

            _logger.LogDebug("Сырой ответ от ИИ:\n{Response}", responseText);

            responseText = responseText.Trim();
            int firstBrace = responseText.IndexOf('{');
            int lastBrace = responseText.LastIndexOf('}');

            if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
                responseText = responseText.Substring(firstBrace, lastBrace - firstBrace + 1);

            responseText = Regex.Replace(responseText, @"\}(\s*)\{", @"},$1{");

            try
            {
                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;

                var recipe = new Recipe
                {
                    Title = GetProp(root, "Title", "Название")?.GetString() ?? "Неизвестный рецепт",
                    Servings = TryGetInt(GetProp(root, "Servings", "Порций")) ?? 2,
                    IsBreakfast = GetBool(GetProp(root, "IsBreakfast", "ДляЗавтрака")),
                    IsLunch = GetBool(GetProp(root, "IsLunch", "ДляОбеда")),
                    IsDinner = GetBool(GetProp(root, "IsDinner", "ДляУжина")),
                    IsSnack = GetBool(GetProp(root, "IsSnack", "ДляПерекуса")),
                };

                var tipsProp = GetProp(root, "PreparationTips", "СоветыПоПодготовке");
                if (tipsProp.HasValue && tipsProp.Value.ValueKind == JsonValueKind.Array)
                    recipe.PreparationTips = tipsProp.Value.GetRawText();

                var nutritionProp = GetProp(root, "Nutrition", "ПитательнаяЦенность");
                if (nutritionProp.HasValue && nutritionProp.Value.ValueKind == JsonValueKind.Object)
                    recipe.NutritionJson = nutritionProp.Value.GetRawText();

                var ingredientsProp = GetProp(root, "Ingredients", "Ингредиенты");
                if (ingredientsProp.HasValue && ingredientsProp.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in ingredientsProp.Value.EnumerateArray())
                    {
                        recipe.Ingredients.Add(new Ingredient
                        {
                            Name = GetProp(item, "Name", "НазваниеИнгредиента", "Ингредиент")?.GetString() ?? string.Empty,
                            Amount = GetProp(item, "Amount", "Количество")?.GetString() ?? string.Empty
                        });
                    }
                }

                var stepsProp = GetProp(root, "Steps", "Шаги");
                if (stepsProp.HasValue && stepsProp.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in stepsProp.Value.EnumerateArray())
                    {
                        recipe.Steps.Add(new RecipeStep
                        {
                            Number = item.TryGetProperty("Number", out var num) ? num.GetInt32()
                                : item.TryGetProperty("Номер", out var numRu) ? numRu.GetInt32() : 0,
                            Description = GetProp(item, "Description", "Описание")?.GetString() ?? string.Empty
                        });
                    }
                }

                return recipe;
            }
            catch (JsonException ex)
            {
                throw new RecipeScribeException(ErrorType.ParseError,
                    $"LLM returned invalid JSON: {ex.Message}", ex);
            }
        }

        private static JsonElement? GetProp(JsonElement element, params string[] names)
        {
            foreach (var name in names)
                if (element.TryGetProperty(name, out var value))
                    return value;
            return null;
        }

        private static int? TryGetInt(JsonElement? element)
        {
            if (element.HasValue && element.Value.TryGetInt32(out var val))
                return val;
            return null;
        }

        private static bool GetBool(JsonElement? element) =>
            element.HasValue && element.Value.GetBoolean();
    }
}
