namespace Core.Exceptions;

public sealed class RecipeNotFoundException : NotFoundException
{
    public RecipeNotFoundException(Guid id)
        : base($"Recipe with id: {id} doesn't exist in the database.") { }
}
