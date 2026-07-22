namespace Core.Models
{
public class User
{
    public Guid Id { get; set; }
    public long TelegramChatId { get; set; }
    public string Username { get; set; } = string.Empty;
    public int DefaultServings { get; set; } = 2;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<MealPlan> MealPlans { get; set; } = new();
}
}