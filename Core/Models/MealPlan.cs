namespace Core.Models
{
    public class MealPlan
    {
        public Guid Id { get; set; }
        public DateOnly Date { get; set; }
        public Guid UserId { get; set; }
        public User User { get; set; }
        public List<MealPlanItem> Items { get; set; } = new();
    }
}