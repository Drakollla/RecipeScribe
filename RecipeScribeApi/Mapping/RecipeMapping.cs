using System.Text.Json;
using Core.Models;
using Core.ValueObjects;
using Shared.DTOs;

namespace RecipeScribeApi.Mapping;

public static class RecipeMapping
{
    public static List<RecipeVariantDto> ToVariantDtos(this Recipe recipe, List<Recipe>? variants)
    {
        var sourceId = recipe.SourceId ?? recipe.Id;
        var list = new List<RecipeVariantDto>
        {
            new(sourceId, recipe.SourceId is null ? recipe.VariantTitle : null)
        };

        list.AddRange((variants ?? new List<Recipe>()).Select(v => new RecipeVariantDto(v.Id, v.VariantTitle)));
        return list.DistinctBy(v => v.Id).ToList();
    }

    public static RecipeDto ToDto(this Recipe recipe, List<RecipeVariantDto>? variants = null, RecipeVariantDto? sourceRecipe = null)
    {
        List<PreparationTipDto>? tips = null;

        if (!string.IsNullOrWhiteSpace(recipe.PreparationTips))
        {
            try
            {
                var source = JsonSerializer.Deserialize<List<PreparationTip>>(recipe.PreparationTips);
                tips = source?.Select(t => new PreparationTipDto(t.Ingredient, t.Tip)).ToList();
            }
            catch (JsonException) { }
        }

        var nutrition = MapNutrition(Nutrition.Deserialize(recipe.NutritionJson));

        return new RecipeDto(
            recipe.Id,
            recipe.Title,
            recipe.VideoUrl,
            recipe.Servings,
            recipe.IsBreakfast,
            recipe.IsLunch,
            recipe.IsDinner,
            recipe.IsSnack,
            recipe.Ingredients.Select(i => new IngredientDto(i.Name, i.Amount)).ToList(),
            recipe.Steps.OrderBy(s => s.Number).Select(s => new RecipeStepDto(s.Number, s.Description)).ToList(),
            tips,
            nutrition,
            recipe.SourceId,
            recipe.VariantTitle,
            variants,
            sourceRecipe
        );
    }

    private static NutritionDto? MapNutrition(Nutrition? nutrition)
    {
        if (nutrition == null)
            return null;

        return new NutritionDto(
            MapValues(nutrition.PerServing),
            MapValues(nutrition.Per100g),
            MapValues(nutrition.Total)
        );
    }

    private static NutritionValuesDto? MapValues(NutritionValues? v)
    {
        return v == null ? null : new NutritionValuesDto(v.Calories, v.Protein, v.Fat, v.Carbs, v.Fiber);
    }
}
