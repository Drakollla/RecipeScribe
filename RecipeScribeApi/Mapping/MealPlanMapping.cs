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

    public static MealPlanDto ToDto(this MealPlan plan)
    {
        var items = plan.Items
            .OrderBy(i => MealOrder.GetValueOrDefault(i.MealType, 99))
            .Select(i => new MealPlanItemDto(
            i.MealType switch
            {
                MealType.Breakfast => "Завтрак",
                MealType.Lunch => "Обед",
                MealType.Dinner => "Ужин",
                MealType.Snack => "Перекус",
                _ => i.MealType.ToString()
            },
            new RecipeSummaryDto(i.Recipe.Id, i.Recipe.Title, i.Recipe.Ingredients.Select(ing => ing.Name).ToList()),
            i.Portions
        )).ToList();

        return new MealPlanDto(plan.Id, plan.Date.ToString("yyyy-MM-dd"), items);
    }
}
