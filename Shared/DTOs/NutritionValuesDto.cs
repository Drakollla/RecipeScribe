namespace Shared.DTOs;

public record NutritionValuesDto(
    double? Calories,
    double? Protein,
    double? Fat,
    double? Carbs,
    double? Fiber
);
