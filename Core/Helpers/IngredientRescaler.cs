using System.Globalization;
using Core.Enums;
using Core.Models;

namespace Core.Helpers;

public static class IngredientRescaler
{
    public static decimal Factor(decimal available, decimal needed) =>
        needed > 0 ? available / needed : 1m;

    public static string Scale(string? amountText, decimal factor)
    {
        if (string.IsNullOrWhiteSpace(amountText) || factor <= 0)
            return amountText ?? string.Empty;

        if (factor == 1m)
            return amountText;

        var parsed = IngredientAmountParser.Parse(amountText);
        if (parsed.Quantity is not > 0 || parsed.Kind == IngredientAmountKind.Unscalable)
            return amountText;

        var scaled = parsed.Quantity.Value * factor;

        string formatted = parsed.Kind == IngredientAmountKind.Count
            ? Math.Max(1m, Math.Ceiling(scaled)).ToString("0", CultureInfo.InvariantCulture)
            : FormatWeightVolume(scaled);

        var match = IngredientAmountParser.NumberRegex.Match(amountText);

        if (!match.Success)
            return amountText;

        return amountText.Remove(match.Index, match.Length).Insert(match.Index, formatted);
    }

    public static List<Ingredient> ScaleIngredients(IEnumerable<Ingredient> ingredients, decimal factor) =>
        ingredients.Select(i => new Ingredient { Name = i.Name, Amount = Scale(i.Amount, factor) }).ToList();

    private static string FormatWeightVolume(decimal scaled)
    {
        var rounded = Math.Round(scaled, 0, MidpointRounding.AwayFromZero);

        if (rounded == 0 && scaled != 0)
        {
            var fraction = Math.Round(scaled, 2, MidpointRounding.AwayFromZero);
            return fraction.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
        }

        return rounded.ToString("0", CultureInfo.InvariantCulture);
    }
}
