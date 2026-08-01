using System.Text.Json;

namespace Core.ValueObjects;

/// <summary>
/// Пересчитанные ингредиенты пункта меню хранятся на MealPlanItem как JSON-строка.
/// Рецепт всегда остаётся оригиналом; пункт меню держит свою копию под свои порции.
/// Замена ингредиента хранится прямо в нём через OriginalName (имя из рецепта),
/// поэтому при LLM-пересчёте замена восстанавливается без отдельного маппинга.
/// </summary>
public static class PlanItemIngredients
{
    public record PlanIngredient(string Name, string Amount, string? OriginalName = null);

    public static string? Serialize(IEnumerable<PlanIngredient> ingredients)
    {
        var items = ingredients
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .ToList();

        return items.Count == 0 ? null : JsonSerializer.Serialize(items);
    }

    public static List<PlanIngredient>? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            // Старый формат [{ Name, Amount }] совместим автоматически — OriginalName станет null
            return JsonSerializer.Deserialize<List<PlanIngredient>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
