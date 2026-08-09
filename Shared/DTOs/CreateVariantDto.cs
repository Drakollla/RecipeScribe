namespace Shared.DTOs;

public record CreateVariantDto(
    string? VariantTitle,
    List<IngredientDto> Ingredients,
    List<RecipeStepDto> Steps);