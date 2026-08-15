using Core.Models;

namespace Core.ValueObjects;

public record StepRewriteResult(
    List<RecipeStep> Steps,
    List<PreparationTip>? Tips);