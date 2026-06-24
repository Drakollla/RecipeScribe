using Core.Enums;

namespace Core.Exceptions
{
    public class RecipeScribeException : Exception
    {
        public ErrorType Type { get; }

        public RecipeScribeException(ErrorType type, string message, Exception? inner = null)
            : base(message, inner) => Type = type;
    }
}
