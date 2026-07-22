namespace Core.Models
{
public class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
    public int Servings { get; set; } = 2;
    public bool IsBreakfast { get; set; }
    public bool IsLunch { get; set; }
    public bool IsDinner { get; set; }
    public bool IsSnack { get; set; }
    public string? PreparationTips { get; set; }
    public string? NutritionJson { get; set; }
    public List<Ingredient> Ingredients { get; set; } = new();
    public List<RecipeStep> Steps { get; set; } = new();
}
}