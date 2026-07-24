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
        private static readonly string YtdlpPath = Path.Combine(ToolPaths.Directory, BinaryName("yt-dlp"));
        private static readonly string FfmpegPath = Path.Combine(ToolPaths.Directory, BinaryName("ffmpeg"));
        private static readonly string AudioDir = Path.Combine(AppContext.BaseDirectory, "DownloadedAudio");

        public YouTubeDownloader(ILogger<YouTubeDownloader> logger)
        {
            _logger = logger;
        }

        public async Task<ViewMetadata> DownloadAudioAsync(string videoUrl, CancellationToken ct = default)
        {
            await EnsureBinariesAsync(ct);

            Directory.CreateDirectory(AudioDir);

            var ytdl = new YoutubeDL
            {
                YoutubeDLPath = YtdlpPath,
                FFmpegPath = FfmpegPath,
                OutputFolder = AudioDir
            };

            var video = await ytdl.RunVideoDataFetch(videoUrl);

            if (!video.Success)
                throw new RecipeScribeException(ErrorType.VideoNotFound, $"Failed to retrieve video data: {string.Join("; ", video.ErrorOutput)}");

            string videoId = video.Data.ID;
            string transcriptPath = Path.Combine(AudioDir, $"{videoId}.txt");

            if (File.Exists(transcriptPath))
            {
                string cachedText = await File.ReadAllTextAsync(transcriptPath, System.Text.Encoding.UTF8, ct);

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
                throw new RecipeScribeException(ErrorType.Network, $"Failed to download audio: {errorDetails}");
            }

            return new ViewMetadata
            {
                Title = video.Data.Title,
                Description = video.Data.Description,
                AudioFilePath = downloadResult.Data,
                CachedTranscript = null
            };
        }

        public async Task<string?> GetFirstCommentAsync(string videoUrl, CancellationToken ct = default)
        {
            await EnsureBinariesAsync(ct);

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
                throw new RecipeScribeException(ErrorType.Network, "Failed to start yt-dlp for retrieving comments");

            string output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync(ct);
                throw new RecipeScribeException(ErrorType.VideoNotFound,
                    $"yt-dlp failed to retrieve comments: {error}");
            }

            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }

        private async Task EnsureBinariesAsync(CancellationToken ct = default)
        {
            Directory.CreateDirectory(ToolPaths.Directory);
            CleanOldCacheFiles(AudioDir);

            if (!File.Exists(YtdlpPath))
            {
                _logger.LogInformation("yt-dlp не найден. Скачиваю...");
                await Utils.DownloadYtDlp(ToolPaths.Directory);
            }

            if (!File.Exists(FfmpegPath))
            {
                _logger.LogInformation("ffmpeg не найден. Скачиваю...");
                await Utils.DownloadFFmpeg(ToolPaths.Directory);
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