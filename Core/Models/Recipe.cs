namespace Core.Models
{
    public class Recipe
    {
        public string Title { get; set; } = string.Empty;
        public List<Ingredient> Ingredients { get; set; } = new();
        public List<RecipeStep> Steps { get; set; } = new();
    }
}