namespace Shared.DTOs;

public record RescaleRecipeDto(
    List<RescaleConstraintDto> Constraints);

public record RescaleConstraintDto(
    string Ingredient,
    decimal Available);
