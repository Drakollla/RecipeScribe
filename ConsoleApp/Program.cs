using Core.Contracts;
using Infrastructure;
using System;

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

    ITranscriber transcriber = new WhisperTranscriber();
    string transcript = await transcriber.TranscribeAsync(metadata.AudioFilePath);

    Console.WriteLine("\n=== Текст успешно распознан! ===");
    Console.WriteLine(transcript);

}
catch (Exception ex)
{
    Console.WriteLine($" Произошла ошибка: {ex.Message}");

    if (ex.InnerException != null)
    {
        Console.WriteLine($"Детали: {ex.InnerException.Message}");
    }
}