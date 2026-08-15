namespace Shared.DTOs;

public record RecipeDto(
    Guid Id,
    string Title,
    string VideoUrl,
    int Servings,
    bool IsBreakfast,
    bool IsLunch,
    bool IsDinner,
    bool IsSnack,
    List<IngredientDto> Ingredients,
    List<RecipeStepDto> Steps,
    List<PreparationTipDto>? PreparationTips,
    NutritionDto? Nutrition,
    Guid? SourceId = null,
    string? VariantTitle = null,
    List<RecipeVariantDto>? Variants = null,
    RecipeVariantDto? SourceRecipe = null
);
