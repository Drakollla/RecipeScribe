using System.Text.Json;

namespace Core.Models;

public class Nutrition
{
    public NutritionValues? PerServing { get; set; }
    public NutritionValues? Per100g { get; set; }
    public NutritionValues? Total { get; set; }

    public static Nutrition? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("PerServing", out _))
                return JsonSerializer.Deserialize<Nutrition>(json);

            var flat = JsonSerializer.Deserialize<NutritionValues>(json);

            return flat == null ? null : new Nutrition { PerServing = flat };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
