namespace Shared.DTOs;

public record NutritionDto(
    NutritionValuesDto? PerServing,
    NutritionValuesDto? Per100g,
    NutritionValuesDto? Total
);
