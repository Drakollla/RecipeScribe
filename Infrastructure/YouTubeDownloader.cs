using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Core.Helpers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace Infrastructure
{
    public class YouTubeDownloader : IVideoDownloader
    {
        private readonly ILogger<YouTubeDownloader> _logger;
        private static readonly string ToolsDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Core", "Tools"));
        private static readonly string YtdlpPath = Path.Combine(ToolsDir, BinaryName("yt-dlp"));
        private static readonly string FfmpegPath = Path.Combine(ToolsDir, BinaryName("ffmpeg"));
        private static readonly string AudioDir = Path.Combine(AppContext.BaseDirectory, "DownloadedAudio");

        public YouTubeDownloader(ILogger<YouTubeDownloader> logger)
        {
            _logger = logger;
        }

        public async Task<ViewMetadata> DownloadAudioAsync(string videoUrl)
        {
            await EnsureBinariesAsync();

            Directory.CreateDirectory(AudioDir);

            var ytdl = new YoutubeDL
            {
                YoutubeDLPath = YtdlpPath,
                FFmpegPath = FfmpegPath,
                OutputFolder = AudioDir
            };

            var video = await ytdl.RunVideoDataFetch(videoUrl);

            if (!video.Success)
                throw new RecipeScribeException(ErrorType.VideoNotFound, $"Не удалось получить данные о видео: {string.Join("; ", video.ErrorOutput)}");

            string videoId = video.Data.ID;
            string transcriptPath = Path.Combine(AudioDir, $"{videoId}.txt");

            if (File.Exists(transcriptPath))
            {
                string cachedText = await File.ReadAllTextAsync(transcriptPath, System.Text.Encoding.UTF8);

                return new ViewMetadata
                {
                    Title = video.Data.Title,
                    Description = video.Data.Description,
                    AudioFilePath = string.Empty,
                    CachedTranscript = cachedText
                };
            }

            var options = new OptionSet
            {
                Output = Path.Combine(AudioDir, $"audio_{videoId}.%(ext)s")
            };

            var downloadResult = await ytdl.RunAudioDownload(
                videoUrl, AudioConversionFormat.Mp3, overrideOptions: options);

            if (!downloadResult.Success)
            {
                string errorDetails = string.Join(Environment.NewLine, downloadResult.ErrorOutput);
                throw new RecipeScribeException(ErrorType.Network, $"Не удалось скачать аудио. Ошибка: {errorDetails}");
            }

            return new ViewMetadata
            {
                Title = video.Data.Title,
                Description = video.Data.Description,
                AudioFilePath = downloadResult.Data,
                CachedTranscript = null
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

        private async Task EnsureBinariesAsync()
        {
            Directory.CreateDirectory(ToolsDir);
            CleanOldCacheFiles(AudioDir);

            if (!File.Exists(YtdlpPath))
            {
                _logger.LogInformation("yt-dlp не найден. Скачиваю...");
                await Utils.DownloadYtDlp(ToolsDir);
            }

            if (!File.Exists(FfmpegPath))
            {
                _logger.LogInformation("ffmpeg не найден. Скачиваю...");
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

        private static void CleanOldCacheFiles(string path)
        {
            if (!Directory.Exists(path))
                return;

            var threshold = DateTime.UtcNow.AddDays(-30);
            var files = Directory.GetFiles(path, "*.txt");

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                
                if (fileInfo.LastWriteTimeUtc < threshold)
                    fileInfo.Delete();
            }
        }

        private static string BinaryName(string name) =>
            OperatingSystem.IsWindows() ? $"{name}.exe" : name;
    }
}