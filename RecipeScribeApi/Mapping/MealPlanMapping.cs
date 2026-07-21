using Core.Enums;
using Core.Models;
using Shared.DTOs;

namespace RecipeScribeApi.Mapping;

public static class MealPlanMapping
{
    public static MealPlanDto ToDto(this MealPlan plan)
    {
        var items = plan.Items.Select(i => new MealPlanItemDto(
            i.MealType switch
            {
                MealType.Breakfast => "Завтрак",
                MealType.Lunch => "Обед",
                MealType.Dinner => "Ужин",
                MealType.Snack => "Перекус",
                _ => i.MealType.ToString()
            },
            new RecipeSummaryDto(i.Recipe.Id, i.Recipe.Title)
        )).ToList();

        return new MealPlanDto(plan.Id, plan.Date.ToString("yyyy-MM-dd"), items);
    }
}
