using Core.Contracts;
using Core.Models;
using System.Diagnostics;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace Infrastructure
{
    public class YouTubeDownloader : IVideoDownloader
    {
        private static readonly string ToolsDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Core", "Tools"));
        private static readonly string YtdlpPath = Path.Combine(ToolsDir, BinaryName("yt-dlp"));
        private static readonly string FfmpegPath = Path.Combine(ToolsDir, BinaryName("ffmpeg"));
        private static readonly string AudioDir = Path.Combine(AppContext.BaseDirectory, "DownloadedAudio");

        public async Task<ViewMetadata> DownloadAudioAsync(string videoUrl)
        {
            await EnsureBinariesAsync();

            CleanDirectory(AudioDir);

            var ytdl = new YoutubeDL
            {
                YoutubeDLPath = YtdlpPath,
                FFmpegPath = FfmpegPath,
                OutputFolder = AudioDir
            };

            var video = await ytdl.RunVideoDataFetch(videoUrl);
            
            if (!video.Success)
                throw new RecipeScribeException(ErrorType.VideoNotFound,$"Не удалось получить данные о видео: {string.Join("; ", video.ErrorOutput)}");

            var options = new OptionSet
            {
                Output = Path.Combine(AudioDir, "audio.%(ext)s")
            };

            var downloadResult = await ytdl.RunAudioDownload(
                videoUrl, AudioConversionFormat.Mp3, overrideOptions: options);

            if (!downloadResult.Success)
            {
                string errorDetails = string.Join(Environment.NewLine, downloadResult.ErrorOutput);
                throw new RecipeScribeException(ErrorType.Network,$"Не удалось скачать аудио. Ошибка: {errorDetails}");
            }

            return new ViewMetadata
            {
                Title = video.Data.Title,
                Description = video.Data.Description,
                AudioFilePath = downloadResult.Data
            };
        }

        public async Task<string?> GetFirstCommentAsync(string videoUrl)
        {
            await EnsureBinariesAsync();

            var startInfo = new ProcessStartInfo
            {
                FileName = YtdlpPath,
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

        private static async Task EnsureBinariesAsync()
        {
            Directory.CreateDirectory(ToolsDir);

            if (!File.Exists(YtdlpPath))
            {
                Console.WriteLine("yt-dlp не найден. Скачиваю...");
                await Utils.DownloadYtDlp(ToolsDir);
            }

            if (!File.Exists(FfmpegPath))
            {
                Console.WriteLine("ffmpeg не найден. Скачиваю...");
                await Utils.DownloadFFmpeg(ToolsDir);
            }
        }

        private static void CleanDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.GetFiles(path))
                    File.Delete(file);
            }
            else
            {
                Directory.CreateDirectory(path);
            }
        }

        private static string BinaryName(string name) =>
            OperatingSystem.IsWindows() ? $"{name}.exe" : name;
    }
}