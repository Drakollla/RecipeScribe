using Core.Models;

namespace Core.Contracts
{
    public interface IRecipeExporter
    {
        string Format { get; }

        Task ExportAsync(Recipe recipe, string outputPath);
    }
}