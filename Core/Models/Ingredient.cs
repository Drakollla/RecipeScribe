namespace Core.Models
{
    public class Ingredient
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Amount { get; set; } = string.Empty;
        public Guid RecipeId { get; set; }
        public Recipe? Recipe { get; set; } 
    }
}