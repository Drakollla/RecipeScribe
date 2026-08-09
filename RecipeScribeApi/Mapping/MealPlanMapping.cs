using Core.Contracts;
using Core.Enums;
using Core.Models;
using Shared.DTOs;

namespace RecipeScribeApi.Mapping;

public static class MealPlanMapping
{
    private static readonly Dictionary<MealType, int> MealOrder = new()
    {
        { MealType.Breakfast, 0 },
        { MealType.Lunch, 1 },
        { MealType.Dinner, 2 },
        { MealType.Snack, 3 },
    };

    public static async Task<MealPlanDto> ToDtoAsync(this MealPlan plan, IRecipeRepository recipeRepository)
    {
        var itemVariants = new Dictionary<Guid, List<RecipeVariantDto>>();
       
        foreach (var item in plan.Items)
            itemVariants[item.Id] = await item.Recipe.ToVariantDtosAsync(recipeRepository);

        var items = plan.Items
            .OrderBy(i => MealOrder.GetValueOrDefault(i.MealType, 99))
            .Select(i => i.ToDto(itemVariants.GetValueOrDefault(i.Id)))
            .ToList();

        return new MealPlanDto(plan.Id, plan.Date.ToString("yyyy-MM-dd"), items);
    }

    public static async Task<MealPlanItemDto> ToDtoAsync(this MealPlanItem item, IRecipeRepository recipeRepository)
    {
        return item.ToDto(await item.Recipe.ToVariantDtosAsync(recipeRepository));
    }

    public static async Task<List<RecipeVariantDto>> ToVariantDtosAsync(this Recipe recipe, IRecipeRepository recipeRepository)
    {
        var variants = await recipeRepository.GetVariantsAsync(recipe.SourceId ?? recipe.Id);
        return recipe.ToVariantDtos(variants);
    }

    public static MealPlanItemDto ToDto(this MealPlanItem item, List<RecipeVariantDto>? variants = null)
    {
        var ingredients = item.Recipe.Ingredients.Select(ing => new IngredientDto(ing.Name, ing.Amount)).ToList();

        return new MealPlanItemDto(
            item.Id,
            item.MealType switch
            {
                MealType.Breakfast => "Завтрак",
                MealType.Lunch => "Обед",
                MealType.Dinner => "Ужин",
                MealType.Snack => "Перекус",
                _ => item.MealType.ToString()
            },
            new RecipeSummaryDto(item.Recipe.Id, item.Recipe.Title, item.Recipe.Ingredients.Select(ing => ing.Name).ToList()),
            item.Portions,
            item.Recipe.Servings,
            ingredients,
            variants
        );
    }
}
