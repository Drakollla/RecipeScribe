namespace Core.Models
{
    public class RecipeStep
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int Number { get; set; }
        public string Description { get; set; } = string.Empty;
        public Guid RecipeId { get; set; }
        public Recipe? Recipe { get; set; }
    }
}