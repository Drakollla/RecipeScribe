using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Core.Helpers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace Infrastructure;

public class YouTubeDownloader : IVideoDownloader
{
    private readonly ILogger<YouTubeDownloader> _logger;
    private static readonly string YtdlpPath = Path.Combine(ToolPaths.Directory, BinaryName("yt-dlp"));
    private static readonly string FfmpegPath = Path.Combine(ToolPaths.Directory, BinaryName("ffmpeg"));
    private static readonly string AudioDir = Path.Combine(ToolPaths.Directory, "Audio");

    public YouTubeDownloader(ILogger<YouTubeDownloader> logger)
    {
        _logger = logger;
    }

    public async Task<ViewMetadata> DownloadAudioAsync(string videoUrl, CancellationToken ct = default)
    {
        await EnsureBinariesAsync(ct);
        Directory.CreateDirectory(AudioDir);

        var ytdl = CreateYoutubeDl();
        var videoData = await FetchVideoDataAsync(ytdl, videoUrl);

        string? cachedTranscript = await ReadCachedTranscriptAsync(videoData.ID, ct);

        if (cachedTranscript != null)
            return ToMetadata(videoData, audioFilePath: null, cachedTranscript);

        string audioPath = await DownloadAudioFileAsync(ytdl, videoUrl, videoData.ID);
        
        return ToMetadata(videoData, audioPath, cachedTranscript: null);
    }

    public async Task<string?> GetFirstCommentAsync(string videoUrl, CancellationToken ct = default)
    {
        await EnsureBinariesAsync(ct);

        string arguments = $"--get-comments --extractor-args \"youtube:max-comments=1\" --print \"%(comments.0.text)s\" --skip-download --encoding utf-8 \"{videoUrl}\"";
        var (exitCode, output, error) = await RunYtdlpAsync(arguments, ct);

        if (exitCode != 0)
            throw new RecipeScribeException(ErrorType.VideoNotFound, $"yt-dlp failed to retrieve comments: {error}");

        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }

    private static YoutubeDL CreateYoutubeDl() => new()
    {
        YoutubeDLPath = YtdlpPath,
        FFmpegPath = FfmpegPath,
        OutputFolder = AudioDir
    };

    private static async Task<VideoData> FetchVideoDataAsync(YoutubeDL ytdl, string videoUrl)
    {
        var video = await ytdl.RunVideoDataFetch(videoUrl);

        if (!video.Success)
            throw new RecipeScribeException(ErrorType.VideoNotFound, $"Failed to retrieve video data: {string.Join("; ", video.ErrorOutput)}");

        return video.Data;
    }

    private static async Task<string?> ReadCachedTranscriptAsync(string videoId, CancellationToken ct)
    {
        string transcriptPath = Path.Combine(AudioDir, $"{videoId}.txt");

        if (!File.Exists(transcriptPath))
            return null;

        return await File.ReadAllTextAsync(transcriptPath, System.Text.Encoding.UTF8, ct);
    }

    private static async Task<string> DownloadAudioFileAsync(YoutubeDL ytdl, string videoUrl, string videoId)
    {
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

        return downloadResult.Data;
    }

    private static ViewMetadata ToMetadata(VideoData video, string? audioFilePath, string? cachedTranscript) => new()
    {
        Title = video.Title,
        Description = video.Description,
        AudioFilePath = audioFilePath ?? string.Empty,
        CachedTranscript = cachedTranscript
    };

    private static async Task<(int ExitCode, string Output, string Error)> RunYtdlpAsync(string arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = YtdlpPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);

        if (process == null)
            throw new RecipeScribeException(ErrorType.Network, $"Failed to start yt-dlp: {arguments}");

        string output = await process.StandardOutput.ReadToEndAsync(ct);
        string error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, output, error);
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
        var files = Directory.GetFiles(path, "*.txt")
            .Concat(Directory.GetFiles(path, "*.mp3"));

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
