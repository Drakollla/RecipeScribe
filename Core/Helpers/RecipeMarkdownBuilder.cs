using System.Text;
using System.Text.Json;
using Core.Models;
using Core.ValueObjects;

namespace Core.Helpers;

public static class RecipeMarkdownBuilder
{
    public static string Build(Recipe recipe)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {recipe.Title}");

        AppendIngredients(sb, recipe.Ingredients);
        AppendNutritionTable(sb, Nutrition.Deserialize(recipe.NutritionJson));
        AppendTips(sb, DeserializeTips(recipe.PreparationTips));
        AppendSteps(sb, recipe.Steps);

        return sb.ToString();
    }

    private static void AppendIngredients(StringBuilder sb, IEnumerable<Ingredient> ingredients)
    {
        sb.AppendLine();
        sb.AppendLine("### Ингредиенты:");

        foreach (var ing in ingredients)
        {
            var amount = string.IsNullOrWhiteSpace(ing.Amount) ? "" : $" — {ing.Amount}";
            sb.AppendLine($"- {ing.Name}{amount}");
        }
    }

    private static void AppendTips(StringBuilder sb, List<PreparationTip>? tips)
    {
        if (tips is not { Count: > 0 })
            return;

        sb.AppendLine();
        sb.AppendLine("### Советы по подготовке:");

        foreach (var tip in tips)
            sb.AppendLine($"- **{tip.Ingredient}:** {tip.Tip}");
    }

    private static void AppendSteps(StringBuilder sb, IEnumerable<RecipeStep> steps)
    {
        sb.AppendLine();
        sb.AppendLine("### Шаги приготовления:");

        foreach (var step in steps.OrderBy(s => s.Number))
            sb.AppendLine($"{step.Number}. {step.Description}");
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

    private static void AppendNutritionTable(StringBuilder sb, Nutrition? nutrition)
    {
        if (nutrition?.PerServing == null && nutrition?.Per100g == null && nutrition?.Total == null)
            return;

        var columns = new List<NutritionColumn>();

        if (nutrition.PerServing != null)
            columns.Add(new("На порцию", nutrition.PerServing));

        if (nutrition.Per100g != null)
            columns.Add(new("На 100 г", nutrition.Per100g));
        
        if (nutrition.Total != null) 
            columns.Add(new("Всё блюдо", nutrition.Total));

        var rows = new[]
        {
            ("Калории", "ккал", (Func<NutritionValues, double?>)(v => v.Calories)),
            ("Белки", "г", v => v.Protein),
            ("Жиры", "г", v => v.Fat),
            ("Углеводы", "г", v => v.Carbs),
            ("Клетчатка", "г", v => v.Fiber),
        };

        sb.AppendLine();
        sb.AppendLine("### Пищевая ценность");
        sb.Append("| |");
        
        foreach (var column in columns)
            sb.Append($" {column.Header} |");
        
        sb.AppendLine();

        sb.Append("|---|");
        
        foreach (var _ in columns)
            sb.Append("---|");
        
        sb.AppendLine();

        foreach (var (label, unit, select) in rows)
        {
            if (columns.All(c => select(c.Values) == null))
                continue;

            sb.Append($"| **{label}** |");

            foreach (var column in columns)
            {
                var value = select(column.Values);
                sb.Append($" {value?.ToString("F1") ?? "—"} {unit} |");
            }

            sb.AppendLine();
        }
    }

    private sealed record NutritionColumn(string Header, NutritionValues Values);
}
