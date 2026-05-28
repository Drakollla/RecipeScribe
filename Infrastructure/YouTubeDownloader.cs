using Core.Contracts;
using Core.Models;
using System.Diagnostics;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace Infrastructure
{
    public class YouTubeDownloader : IVideoDownloader
    {
        public async Task<ViewMetadata> DownloadAudioAsync(string videoUrl)
        {
            string ytdlpBin = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
            string ffmpegBin = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

            if (!File.Exists(ytdlpBin))
            {
                Console.WriteLine("Компонент скачивания (yt-dlp) не найден. Скачиваю...");
                await Utils.DownloadYtDlp();
            }

            if (!File.Exists(ffmpegBin))
            {
                Console.WriteLine("Аудио-конвертер (ffmpeg) не найден. Скачиваю...");
                await Utils.DownloadFFmpeg();
            }

            var ytdl = new YoutubeDL();
            ytdl.YoutubeDLPath = ytdlpBin;
            ytdl.FFmpegPath = ffmpegBin;

            string folderPath = GetDirectory();

            var video = await ytdl.RunVideoDataFetch(videoUrl);
            if (!video.Success)
                throw new RecipeScribeException(ErrorType.VideoNotFound,
                    $"Не удалось получить данные о видео: {string.Join("; ", video.ErrorOutput)}");

            string title = video.Data.Title;
            string description = video.Data.Description;

            ytdl.OutputFolder = folderPath;

            var options = new OptionSet()
            {
                Output = Path.Combine(folderPath, "audio.%(ext)s")
            };

            var downloadResult = await ytdl.RunAudioDownload(
                videoUrl,
                AudioConversionFormat.Mp3,
                overrideOptions: options
            );

            if (!downloadResult.Success)
            {
                string errorDetails = string.Join(Environment.NewLine, downloadResult.ErrorOutput);
                throw new RecipeScribeException(ErrorType.Network,
                    $"Не удалось скачать аудио. Ошибка: {errorDetails}");
            }

            string outputFilePath = downloadResult.Data;

            return new ViewMetadata
            {
                Title = title,
                Description = description,
                AudioFilePath = outputFilePath
            };
        }

        private string GetDirectory()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string folderPath = Path.Combine(exeDir, "DownloadedAudio");
            Directory.CreateDirectory(folderPath);

            return folderPath;
        }

        public async Task<string?> GetFirstCommentAsync(string videoUrl)
        {
            string ytdlpBin = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";

            var startInfo = new ProcessStartInfo
            {
                FileName = ytdlpBin,
                Arguments = $"--get-comments --extractor-args \"youtube:max-comments=1\" --print \"%(comments.0.text)s\" --skip-download --encoding utf-8 \"{videoUrl}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new RecipeScribeException(ErrorType.Network, "Не удалось запустить yt-dlp для получения комментариев");

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                throw new RecipeScribeException(ErrorType.VideoNotFound,
                    $"yt-dlp не смог получить комментарии: {error}");
            }

            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
    }
}