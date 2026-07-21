using System.Text;
using System.Text.Json;
using Core.Models;

namespace Core.Helpers;

public static class RecipeMarkdownBuilder
{
    public static string Build(Recipe recipe)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {recipe.Title}");
        sb.AppendLine();
        sb.AppendLine("### Ингредиенты:");

        foreach (var ing in recipe.Ingredients)
        {
            var amount = string.IsNullOrWhiteSpace(ing.Amount) ? "" : $" — {ing.Amount}";
            sb.AppendLine($"- {ing.Name}{amount}");
        }

        var tips = DeserializeTips(recipe.PreparationTips);

        if (tips is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("### Советы по подготовке:");

            foreach (var tip in tips)
                sb.AppendLine($"- **{tip.Ingredient}:** {tip.Tip}");
        }

        sb.AppendLine();
        sb.AppendLine("### Шаги приготовления:");

        foreach (var step in recipe.Steps.OrderBy(s => s.Number))
            sb.AppendLine($"{step.Number}. {step.Description}");

        return sb.ToString();
    }

    private static List<PreparationTip>? DeserializeTips(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<PreparationTip>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
