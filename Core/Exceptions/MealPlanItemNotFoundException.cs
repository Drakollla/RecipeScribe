namespace Core.Exceptions;

public sealed class MealPlanItemNotFoundException : NotFoundException
{
    public MealPlanItemNotFoundException(Guid planItemId)
        : base($"Meal plan item {planItemId} not found.") { }
}
