namespace Shared.DTOs;

public record RewrittenStepsDto(
    List<RecipeStepDto> Steps,
    List<PreparationTipDto>? Tips);