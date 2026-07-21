namespace Shared.DTOs;

public record MealPlanDto(Guid Id, string Date, List<MealPlanItemDto> Items);
