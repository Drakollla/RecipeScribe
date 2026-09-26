using Core.Enums;

namespace Core.Helpers;

public record ParsedAmount(decimal? Quantity, string? Unit, IngredientAmountKind Kind);
