using System.Text.Json;
using Core.Models;

namespace Core.ValueObjects;

/// <summary>
/// Пересчитанные ингредиенты пункта меню хранятся на MealPlanItem как JSON-строка.
/// Рецепт всегда остаётся оригиналом; пункт меню держит свою копию под свои порции.
/// </summary>
public static class PlanItemIngredients
{
    public static string? Serialize(IEnumerable<Ingredient> ingredients)
    {
        var items = ingredients
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => new IngredientDtoJson(i.Name, i.Amount))
            .ToList();

        return items.Count == 0 ? null : JsonSerializer.Serialize(items);
    }

    public static List<Ingredient>? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<IngredientDtoJson>>(json)
                ?.Select(i => new Ingredient { Name = i.Name, Amount = i.Amount })
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private record IngredientDtoJson(string Name, string Amount);
}
