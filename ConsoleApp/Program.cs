using Core.Models;
using Infrastructure;
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
    var downloader = serviceProvider.GetRequiredService<YouTubeDownloader>();
    var transcriber = serviceProvider.GetRequiredService<WhisperTranscriber>();
    var parser = serviceProvider.GetRequiredService<RecipeParser>();

    Console.WriteLine("Начинаю загрузку видео...");
    var metadata = await downloader.DownloadAudioAsync(url);

    Console.WriteLine($"\nВидео успешно загружено.");
    Console.WriteLine($"Название: {metadata.Title}");

    string transcript = await transcriber.TranscribeAsync(metadata.AudioFilePath);
    Recipe recipe = await parser.ParseRecipeAsync(transcript);
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