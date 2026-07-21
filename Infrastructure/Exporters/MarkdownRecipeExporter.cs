using Core.Contracts;
using Core.Models;
using System.Text;

namespace Infrastructure.Exporters
{
    public class MarkdownRecipeExporter : IRecipeExporter
    {
        public string Format => "md";

        public async Task ExportAsync(Recipe recipe, string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {recipe.Title}");
            sb.AppendLine();
            sb.AppendLine("## Ингридиенты");

            foreach (var ingredient in recipe.Ingredients)
            {
                string amount = string.IsNullOrEmpty(ingredient.Amount) ? "" : $" - {ingredient.Amount}";
                sb.AppendLine($"* {ingredient.Name} {amount}");
            }

            sb.AppendLine();
            sb.AppendLine("## Шаги приготовления");

            foreach (var step in recipe.Steps)
                sb.AppendLine($"{step.Number}. {step.Description}");

            await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
        }
    }
}