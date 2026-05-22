using Core.Contracts;
using Infrastructure;

Console.WriteLine("=== RecipeScrebe запущен ===");
Console.WriteLine("Введите ссылку с рецептом:");
string? url = Console.ReadLine();

if (string.IsNullOrWhiteSpace(url))
{
    Console.WriteLine("Ошибка: Ссылка не может быть пустой.");
    return;
}

try
{
    IYouTubeDownloader downloader = new YouTubeDownloader();
    var metadata = await downloader.DownloadAudioAsync(url);
    Console.WriteLine("\n=== Успешно загружено! ===");
    Console.WriteLine($"Название: {metadata.Title}");
    Console.WriteLine($"Путь к файлу: {metadata.AudioFilePath}");
    Console.WriteLine($"Длина описания: {metadata.Description.Length} символов.");
}
catch (Exception ex)
{
    Console.WriteLine($" Произошла ошибка: {ex.Message}");
}