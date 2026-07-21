namespace Core.Contracts;

public interface IIngredientSubstitutor
{
    Task<string> GetSubstitutionsAsync(string ingredient, string recipeTitle, CancellationToken cancellationToken = default);
}
