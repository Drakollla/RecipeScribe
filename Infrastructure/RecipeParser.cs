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

            responseText = Regex.Replace(responseText, @"\},\s*""([A-Za-z0-9_]+)""\s*:", @"},{""$1"":");

            try
            {
                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;

                var recipe = new Recipe
                {
                    Title = root.TryGetProperty("Title", out var t) ? t.GetString() ?? "Неизвестный рецепт" : "Неизвестный рецепт",
                    IsBreakfast = root.TryGetProperty("IsBreakfast", out var bf) && bf.GetBoolean(),
                    IsLunch = root.TryGetProperty("IsLunch", out var l) && l.GetBoolean(),
                    IsDinner = root.TryGetProperty("IsDinner", out var d) && d.GetBoolean(),
                    IsSnack = root.TryGetProperty("IsSnack", out var s) && s.GetBoolean(),
                };

                if (root.TryGetProperty("PreparationTips", out var tips) && tips.ValueKind == JsonValueKind.Array)
                    recipe.PreparationTips = tips.GetRawText();

                if (root.TryGetProperty("Ingredients", out var ingredients) && ingredients.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in ingredients.EnumerateArray())
                    {
                        recipe.Ingredients.Add(new Ingredient
                        {
                            Name = item.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                            Amount = item.TryGetProperty("Amount", out var a) ? a.GetString() ?? string.Empty : string.Empty
                        });
                    }
                }

                if (root.TryGetProperty("Steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in steps.EnumerateArray())
                    {
                        recipe.Steps.Add(new RecipeStep
                        {
                            Number = item.TryGetProperty("Number", out var num) ? num.GetInt32() : 0,
                            Description = item.TryGetProperty("Description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty
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
    }
}
