namespace Shared.DTOs;

public record MealPlanItemDto(string MealType, RecipeSummaryDto Recipe, int Portions);
