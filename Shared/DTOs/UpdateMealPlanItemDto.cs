namespace Shared.DTOs;

public record UpdateMealPlanItemDto(int Portions, List<IngredientDto>? Ingredients = null);
