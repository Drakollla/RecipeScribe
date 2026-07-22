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

        var nutrition = Nutrition.Deserialize(recipe.NutritionJson);
        AppendNutritionTable(sb, nutrition);

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

    private static void AppendNutritionTable(StringBuilder sb, Nutrition? nutrition)
    {
        if (nutrition?.PerServing == null && nutrition?.Per100g == null && nutrition?.Total == null)
            return;

        var rows = new[]
        {
            ("Калории", "ккал", nutrition.PerServing?.Calories, nutrition.Per100g?.Calories, nutrition.Total?.Calories),
            ("Белки", "г", nutrition.PerServing?.Protein, nutrition.Per100g?.Protein, nutrition.Total?.Protein),
            ("Жиры", "г", nutrition.PerServing?.Fat, nutrition.Per100g?.Fat, nutrition.Total?.Fat),
            ("Углеводы", "г", nutrition.PerServing?.Carbs, nutrition.Per100g?.Carbs, nutrition.Total?.Carbs),
            ("Клетчатка", "г", nutrition.PerServing?.Fiber, nutrition.Per100g?.Fiber, nutrition.Total?.Fiber),
        };

        bool hasPerServing = nutrition.PerServing != null;
        bool hasPer100g = nutrition.Per100g != null;
        bool hasTotal = nutrition.Total != null;

        sb.AppendLine();
        sb.AppendLine("### Пищевая ценность");

        sb.Append("| |");
        
        if (hasPerServing)
            sb.Append(" На порцию |");
        
        if (hasPer100g)
            sb.Append(" На 100 г |");
        
        if (hasTotal)
            sb.Append(" Всё блюдо |");

        sb.AppendLine();
        sb.Append("|---|");
        
        if (hasPerServing)
            sb.Append("---|");
        
        if (hasPer100g)
            sb.Append("---|");
        
        if (hasTotal)
            sb.Append("---|");
        sb.AppendLine();

        foreach (var (label, unit, perS, per100, total) in rows)
        {
            if (perS == null && per100 == null && total == null)
                continue;

            sb.Append($"| **{label}** |");
            
            if (hasPerServing) 
                sb.Append($" {perS?.ToString("F1") ?? "—"} {unit} |");
            
            if (hasPer100g)
                sb.Append($" {per100?.ToString("F1") ?? "—"} {unit} |");
            
            if (hasTotal)
                sb.Append($" {total?.ToString("F1") ?? "—"} {unit} |");
            
            sb.AppendLine();
        }
    }
}
