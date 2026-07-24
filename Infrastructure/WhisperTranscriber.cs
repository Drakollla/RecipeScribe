using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Core.Helpers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;

namespace Infrastructure
{
    public class WhisperTranscriber : ITranscriber
    {
        private readonly ILogger<WhisperTranscriber> _logger;
        private static readonly string ModelPath = Path.Combine(ToolPaths.Directory, "ggml-base.bin");

        public WhisperTranscriber(ILogger<WhisperTranscriber> logger)
        {
            _logger = logger;
        }

        public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
        {
            Directory.CreateDirectory(ToolPaths.Directory);

            await EnsureModelAsync(ct);

            string wavPath = await ConvertToWavAsync(audioFilePath, ct);

            try
            {
                return await TranscribeWavAsync(wavPath, ct);
            }
            finally
            {
                if (File.Exists(wavPath))
                    File.Delete(wavPath);
            }
        }

        private async Task EnsureModelAsync(CancellationToken ct)
        {
            if (File.Exists(ModelPath))
                return;

            _logger.LogInformation("Модель для транскрипции (ggml-base.bin) не найдена. Скачиваю...");

            using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Base);
            using var fileWriter = File.OpenWrite(ModelPath);

            await modelStream.CopyToAsync(fileWriter, ct);
            _logger.LogInformation("Модель успешно загружена.");
        }

        private async Task<string> ConvertToWavAsync(string audioFilePath, CancellationToken ct)
        {
            string wavPath = Path.ChangeExtension(audioFilePath, ".wav");
            string ffmpegBin = Path.Combine(ToolPaths.Directory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

            _logger.LogInformation("Конвертирую аудио в WAV формат...");

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegBin,
                Arguments = $"-y -i \"{audioFilePath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{wavPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            });

            if (process == null)
                throw new RecipeScribeException(ErrorType.TranscriptionFailed, "Failed to start ffmpeg");

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync(ct);
                throw new RecipeScribeException(ErrorType.TranscriptionFailed, $"ffmpeg failed to convert audio: {error}");
            }

            return wavPath;
        }

        private async Task<string> TranscribeWavAsync(string wavPath, CancellationToken ct)
        {
            _logger.LogInformation("Начинаю распознавание речи (это может занять некоторое время)...");

            var resultText = new StringBuilder();

            using var whisperFactory = WhisperFactory.FromPath(ModelPath);
            using var processor = whisperFactory.CreateBuilder().WithLanguage("auto").Build();
            using var fileStream = File.OpenRead(wavPath);

            await foreach (var segment in processor.ProcessAsync(fileStream).WithCancellation(ct))
            {
                resultText.Append(segment.Text);
            }

            return resultText.ToString();
        }
    }
}
