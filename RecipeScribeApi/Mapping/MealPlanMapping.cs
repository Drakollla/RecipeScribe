using Core.Enums;
using Core.Models;
using Core.ValueObjects;
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

    public static MealPlanDto ToDto(this MealPlan plan)
    {
        var items = plan.Items
            .OrderBy(i => MealOrder.GetValueOrDefault(i.MealType, 99))
            .Select(i => i.ToDto())
            .ToList();

        return new MealPlanDto(plan.Id, plan.Date.ToString("yyyy-MM-dd"), items);
    }

    public static MealPlanItemDto ToDto(this MealPlanItem item)
    {
        var ingredients = PlanItemIngredients.Deserialize(item.IngredientsJson)
            ?.Select(ing => new IngredientDto(ing.Name, ing.Amount)).ToList()
            ?? item.Recipe.Ingredients.Select(ing => new IngredientDto(ing.Name, ing.Amount)).ToList();

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
            ingredients
        );
    }
}
