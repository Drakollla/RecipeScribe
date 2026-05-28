using Core.Contracts;
using Core.Models;
using Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

Console.WriteLine("RecipeScribe");
Console.Write("Введите ссылку с рецептом: ");
string? url = Console.ReadLine();

if (string.IsNullOrWhiteSpace(url))
{
    Console.WriteLine("Ошибка: Ссылка не может быть пустой.");
    return;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory) 
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddUserSecrets<Program>()
    .Build();

var serviceProvider = new ServiceCollection()
    .AddInfrastructureServices(configuration) 
    .BuildServiceProvider();

try
{
    var downloader = serviceProvider.GetRequiredService<IVideoDownloader>();
    var transcriber = serviceProvider.GetRequiredService<ITranscriber>();
    var parser = serviceProvider.GetRequiredService<IRecipeParser>();

    Console.WriteLine("Начинаю загрузку видео...");
    var metadata = await downloader.DownloadAudioAsync(url);

    Console.WriteLine($"\nВидео успешно загружено.");
    Console.WriteLine($"Название: {metadata.Title}");

    Recipe? recipe = null;

    if (!string.IsNullOrWhiteSpace(metadata.Description) && metadata.Description.Length > 100)
    {
        Console.WriteLine("\n[Быстрый путь 1] Пробую распарсить рецепт из описания...");
        recipe = await parser.ParseRecipeAsync(metadata.Description);
    }

    if (recipe == null || recipe.Ingredients.Count == 0 || recipe.Title == "Нет рецепта" || recipe.Title == "Ошибка парсинга JSON")
    {
        Console.WriteLine("\n[Быстрый путь 2] Проверяю закрепленный комментарий...");

        string? firstComment = await downloader.GetFirstCommentAsync(url);

        if (!string.IsNullOrWhiteSpace(firstComment))
        {
            Console.WriteLine("Нашелся комментарий! Пробую распарсить...");
            recipe = await parser.ParseRecipeAsync(firstComment);
        }
    }

    if (recipe == null || recipe.Ingredients.Count == 0 || recipe.Title == "Нет рецепта" || recipe.Title == "Ошибка парсинга JSON")
    {
        Console.WriteLine("\n[Медленный путь] Текст не найден. Запускаю транскрибацию...");

        string transcript = await transcriber.TranscribeAsync(metadata.AudioFilePath);
        recipe = await parser.ParseRecipeAsync(transcript);
    }

    Console.WriteLine($"РЕЦЕПТ: {recipe.Title.ToUpper()}");
    Console.WriteLine("\nИНГРЕДИЕНТЫ:");
    
    foreach (var ingredient in recipe.Ingredients)
    {
        string amountText = string.IsNullOrWhiteSpace(ingredient.Amount) ? "" : $" — {ingredient.Amount}";
        Console.WriteLine($"  • {ingredient.Name}{amountText}");
    }

    Console.WriteLine("\nШАГИ ПРИГОТОВЛЕНИЯ:");
    foreach (var step in recipe.Steps)
    {
        Console.WriteLine($"  {step.Number}. {step.Description}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n Произошла ошибка: {ex.Message}");
}