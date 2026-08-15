using System.Text;
using System.Text.Json;
using Core.Models;
using Core.ValueObjects;

namespace Core.Helpers;

public static class RecipeTextBuilder
{
    public static string Build(Recipe recipe)
    {
        var sb = new StringBuilder();

        sb.Append($"🍽 {recipe.Title}");

        if (recipe.Servings > 0)
            sb.Append($" (на {recipe.Servings} порций)");

        AppendIngredients(sb, recipe.Ingredients);
        AppendTips(sb, DeserializeTips(recipe.PreparationTips));
        AppendSteps(sb, recipe.Steps);

        return sb.ToString();
    }

    private static void AppendIngredients(StringBuilder sb, IEnumerable<Ingredient> ingredients)
    {
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("🥣 Ингредиенты:");

        foreach (var ing in ingredients)
        {
            var amount = string.IsNullOrWhiteSpace(ing.Amount) ? "" : $" — {ing.Amount}";
            sb.AppendLine($"• {ing.Name}{amount}");
        }
    }

    private static void AppendTips(StringBuilder sb, List<PreparationTip>? tips)
    {
        if (tips is not { Count: > 0 })
            return;

        sb.AppendLine();
        sb.AppendLine("💡 Советы по подготовке:");

        foreach (var tip in tips)
            sb.AppendLine($"• {tip.Ingredient}: {tip.Tip}");
    }

    private static void AppendSteps(StringBuilder sb, IEnumerable<RecipeStep> steps)
    {
        sb.AppendLine();
        sb.AppendLine("👨‍🍳 Шаги приготовления:");

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
}