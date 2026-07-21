namespace Shared.DTOs;

public record RecipeDto(
    Guid Id,
    string Title,
    string VideoUrl,
    bool IsBreakfast,
    bool IsLunch,
    bool IsDinner,
    bool IsSnack,
    List<IngredientDto> Ingredients,
    List<RecipeStepDto> Steps,
    List<PreparationTipDto>? PreparationTips
);
