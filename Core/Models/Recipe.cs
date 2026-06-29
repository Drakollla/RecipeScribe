namespace Core.Models
{
    public class Recipe
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public bool IsBreakfast { get; set; }
        public bool IsLunch { get; set; }
        public bool IsDinner { get; set; }
        public bool IsSnack { get; set; }
        public List<Ingredient> Ingredients { get; set; } = new();
        public List<RecipeStep> Steps { get; set; } = new();
    }
}