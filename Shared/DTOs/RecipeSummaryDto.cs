namespace Shared.DTOs;

public record RecipeSummaryDto(Guid Id, string Title, List<string>? IngredientNames = null,
    bool IsBreakfast = false, bool IsLunch = false, bool IsDinner = false, bool IsSnack = false);
