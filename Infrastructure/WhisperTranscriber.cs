using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
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
        private static readonly string ToolsDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Core", "Tools"));
        private static readonly string ModelPath = Path.Combine(ToolsDir, "ggml-base.bin");

        public WhisperTranscriber(ILogger<WhisperTranscriber> logger)
        {
            _logger = logger;
        }

        public async Task<string> TranscribeAsync(string audioFilePath)
        {
            Directory.CreateDirectory(ToolsDir);

            await EnsureModelAsync();

            string wavPath = await ConvertToWavAsync(audioFilePath);

            try
            {
                return await TranscribeWavAsync(wavPath);
            }
            finally
            {
                if (File.Exists(wavPath))
                    File.Delete(wavPath);
            }
        }

        private async Task EnsureModelAsync()
        {
            if (File.Exists(ModelPath))
                return;

            _logger.LogInformation("Модель для транскрипции (ggml-base.bin) не найдена. Скачиваю...");

            using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Base);
            using var fileWriter = File.OpenWrite(ModelPath);

            await modelStream.CopyToAsync(fileWriter);
            _logger.LogInformation("Модель успешно загружена.");
        }

        private async Task<string> ConvertToWavAsync(string audioFilePath)
        {
            string wavPath = Path.ChangeExtension(audioFilePath, ".wav");
            string ffmpegBin = Path.Combine(ToolsDir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

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
                throw new RecipeScribeException(ErrorType.TranscriptionFailed, "Не удалось запустить ffmpeg");

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                throw new RecipeScribeException(ErrorType.TranscriptionFailed, $"ffmpeg не смог конвертировать аудио: {error}");
            }

            return wavPath;
        }

        private async Task<string> TranscribeWavAsync(string wavPath)
        {
            _logger.LogInformation("Начинаю распознавание речи (это может занять некоторое время)...");

            var resultText = new StringBuilder();

            using var whisperFactory = WhisperFactory.FromPath(ModelPath);
            using var processor = whisperFactory.CreateBuilder().WithLanguage("auto").Build();
            using var fileStream = File.OpenRead(wavPath);

            await foreach (var segment in processor.ProcessAsync(fileStream))
            {
                resultText.Append(segment.Text);
            }

            return resultText.ToString();
        }
    }
}
