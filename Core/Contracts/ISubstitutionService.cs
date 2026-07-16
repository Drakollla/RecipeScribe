namespace Core.Contracts;

public interface ISubstitutionService
{
    Task<string> GetSubstitutionsAsync(string ingredient, string recipeTitle, CancellationToken cancellationToken = default);
}
