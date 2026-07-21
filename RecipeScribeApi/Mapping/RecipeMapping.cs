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

        return new RecipeDto(
            recipe.Id,
            recipe.Title,
            recipe.VideoUrl,
            recipe.IsBreakfast,
            recipe.IsLunch,
            recipe.IsDinner,
            recipe.IsSnack,
            recipe.Ingredients.Select(i => new IngredientDto(i.Name, i.Amount)).ToList(),
            recipe.Steps.OrderBy(s => s.Number).Select(s => new RecipeStepDto(s.Number, s.Description)).ToList(),
            tips
        );
    }
}
