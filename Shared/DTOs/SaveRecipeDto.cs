namespace Shared.DTOs;

public record SaveRecipeDto(
    string Title,
    int Servings,
    bool IsBreakfast,
    bool IsLunch,
    bool IsDinner,
    bool IsSnack,
    List<IngredientDto> Ingredients,
    List<RecipeStepDto> Steps);
