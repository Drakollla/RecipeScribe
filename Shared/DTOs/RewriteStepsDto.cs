namespace Shared.DTOs;

public record IngredientReplacementDto(
    string Ingredient,
    string Replacement);

public record RewriteStepsDto(
    List<IngredientReplacementDto> Substitutions);