using Core.Contracts;
using Core.Helpers;
using Core.Models;
using System.Text;

namespace Infrastructure.Exporters
{
    public class MarkdownRecipeExporter : IRecipeExporter
    {
        public string Format => "md";

        public async Task ExportAsync(Recipe recipe, string outputPath)
        {
            var markdown = RecipeMarkdownBuilder.Build(recipe);
            await File.WriteAllTextAsync(outputPath, markdown, Encoding.UTF8);
        }
    }
}