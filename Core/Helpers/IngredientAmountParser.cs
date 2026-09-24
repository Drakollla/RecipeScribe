using System.Globalization;
using System.Text.RegularExpressions;
using Core.Enums;

namespace Core.Helpers;

public static class IngredientAmountParser
{
    private const string FractionChars = "½¼¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝¾";

    public static readonly Regex NumberRegex = new(
        $@"(?:\d+(?:[/.,]\d+)*[{FractionChars}]?|[{FractionChars}])",
        RegexOptions.Compiled);

    private static readonly Dictionary<char, decimal> Fractions = new()
    {
        ['½'] = 0.5m,
        ['¼'] = 0.25m,
        ['¾'] = 0.75m,
        ['⅓'] = 1m / 3m,
        ['⅔'] = 2m / 3m,
        ['⅕'] = 0.2m,
        ['⅖'] = 0.4m,
        ['⅗'] = 0.6m,
        ['⅘'] = 0.8m,
        ['⅙'] = 1m / 6m,
        ['⅚'] = 5m / 6m,
        ['⅛'] = 0.125m,
        ['⅜'] = 0.375m,
        ['⅝'] = 0.625m,
        ['⅞'] = 0.875m
    };

    public static ParsedAmount Parse(string? amount)
    {
        if (string.IsNullOrWhiteSpace(amount))
            return Unscalable();

        var text = amount.Trim();

        foreach (var phrase in IngredientUnits.Unscaled)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return Unscalable();
        }

        var match = NumberRegex.Match(text);

        if (!match.Success)
            return Unscalable();

        var quantity = ParseNumber(match.Value);

        if (quantity is null or <= 0)
            return Unscalable();

        var unitNorm = NormalizeUnit(text[(match.Index + match.Length)..]);
        if (unitNorm.Length == 0)

            return Unscalable();

        var unit = MatchUnit(unitNorm, IngredientUnits.Weight);

        if (unit != null)
            return new ParsedAmount(quantity, unit, IngredientAmountKind.Weight);

        unit = MatchUnit(unitNorm, IngredientUnits.Volume);

        if (unit != null)
            return new ParsedAmount(quantity, unit, IngredientAmountKind.Volume);

        unit = MatchUnit(unitNorm, IngredientUnits.Count);

        if (unit != null)
            return new ParsedAmount(quantity, unit, IngredientAmountKind.Count);

        return Unscalable();
    }

    private static ParsedAmount Unscalable() => new(null, null, IngredientAmountKind.Unscalable);

    private static decimal? ParseNumber(string raw)
    {
        decimal fraction = 0;

        if (raw.Length > 0 && Fractions.TryGetValue(raw[^1], out var fracValue))
        {
            fraction = fracValue;
            raw = raw[..^1];
        }

        decimal baseValue = 0;

        if (raw.Length > 0)
        {
            if (raw.Contains('/'))
            {
                var parts = raw.Split('/');

                if (parts.Length != 2
                    || !TryParse(parts[0], out var numerator)
                    || !TryParse(parts[1], out var denominator)
                    || denominator == 0)
                {
                    return null;
                }

                baseValue = numerator / denominator;
            }
            else if (!TryParse(raw, out baseValue))
            {
                return null;
            }
        }
        else if (fraction == 0)
        {
            return null;
        }

        var total = baseValue + fraction;

        return total > 0 ? total : null;
    }

    private static bool TryParse(string raw, out decimal value) =>
        decimal.TryParse(
            raw.Replace(',', '.'),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);

    private static string NormalizeUnit(string s) =>
        s.Trim().TrimEnd('.').Trim().ToLowerInvariant();

    private static string? MatchUnit(string unitNorm, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var norm = NormalizeUnit(candidate);
            if (norm.Length == 0)
                continue;

            if (unitNorm == norm)
                return candidate;

            if (unitNorm.StartsWith(norm, StringComparison.Ordinal))
            {
                var rest = unitNorm[norm.Length..];
                if (rest.Length == 0 || !char.IsLetter(rest[0]))
                    return candidate;
            }
        }

        return null;
    }
}
