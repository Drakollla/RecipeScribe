namespace Shared.DTOs;

public record MealPlanItemDto(
    Guid Id,
    string MealType,
    RecipeSummaryDto Recipe,
    int Portions,
    int Servings,
    List<IngredientDto>? Ingredients = null);
