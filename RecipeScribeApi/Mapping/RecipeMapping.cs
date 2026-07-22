using System.Text.Json;
using Core.Models;
using Shared.DTOs;

namespace RecipeScribeApi.Mapping;

public static class RecipeMapping
{
    public static RecipeDto ToDto(this Recipe recipe)
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

        var nutrition = MapNutrition(Core.Models.Nutrition.Deserialize(recipe.NutritionJson));

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
            nutrition
        );
    }

    private static NutritionDto? MapNutrition(Nutrition? nutrition)
    {
        if (nutrition == null) return null;

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
