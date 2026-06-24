using Core.Enums;

namespace Core.Models
{
    public class MealPlanItem
    {
        public Guid Id { get; set; }
        public Guid MealPlanId { get; set; }
        public MealPlan MealPlan { get; set; }
        public Guid RecipeId { get; set; }
        public Recipe Recipe { get; set; }
        public MealType MealType { get; set; }
        public int Portions { get; set; } = 1;
    }
}