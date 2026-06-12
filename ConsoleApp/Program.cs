using Core.Contracts;
using Core.Models;
using Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

Console.WriteLine("RecipeScribe");

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddUserSecrets<Program>()
    .Build();

var serviceProvider = new ServiceCollection()
    .AddInfrastructureServices(configuration)
    .AddLogging(builder => builder.AddSerilog(dispose: true))
    .BuildServiceProvider();

while (true)
{
    Console.Write("\nВведите ссылку с рецептом (или оставьте пустым для выхода): ");
    string? url = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(url))
    {
        Console.WriteLine("До свидания!");
        return;
    }

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

        Console.Write("Желаете сохранить этот рецепт в файл Markdown? (y/n): ");
        string? answer = Console.ReadLine()?.Trim().ToLower();

        if (answer == "y" || answer == "yes")
        {
            string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string exportFolder = Path.Combine(myDocuments, "RecipeScribe", "SavedRecipes");
            Directory.CreateDirectory(exportFolder);

            char[] invalidChars = Path.GetInvalidFileNameChars();
            string safeTitle = string.Join("_", recipe.Title.Split(invalidChars)).Trim();

            if (string.IsNullOrWhiteSpace(safeTitle))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                safeTitle = $"recipe_{timestamp}.md";
            }

            string savePath = Path.Combine(exportFolder, $"{safeTitle}.md");

            var mdExporter = serviceProvider.GetServices<IRecipeExporter>()
                .FirstOrDefault(e => e.Format == "md");

            if (mdExporter != null)
            {
                Console.WriteLine("Сохраняю рецепт в файл...");
                await mdExporter.ExportAsync(recipe, savePath);
                Console.WriteLine($"Рецепт успешно сохранен по адресу:\n{savePath}\n");
            }
            else
            {
                Console.WriteLine("Ошибка: Экспортер формата Markdown не зарегистрирован в системе.");
            }
        }
    }
    catch (RecipeScribeException ex)
    {
        string msg = ex.Type switch
        {
            ErrorType.Network => "Нет соединения или видео недоступно",
            ErrorType.VideoNotFound => "Видео не найдено или недоступно",
            ErrorType.LlmFailure => "Не удалось распарсить рецепт (ошибка ИИ)",
            ErrorType.ParseError => "Ответ от ИИ не содержит корректный рецепт",
            ErrorType.TranscriptionFailed => "Не удалось распознать аудио",
            _ => "Неизвестная ошибка"
        };
        Console.WriteLine($"\nОшибка: {msg}");
        Console.WriteLine($"Детали: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nНеожиданная ошибка: {ex.Message}");
    }
}