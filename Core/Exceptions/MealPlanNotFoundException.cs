namespace Core.Exceptions;

public sealed class MealPlanNotFoundException : NotFoundException
{
    public MealPlanNotFoundException(DateOnly date)
        : base($"Meal plan for date {date:yyyy-MM-dd} not found.") { }
}
