using Core.Contracts;
using System.Diagnostics;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;

namespace Infrastructure
{
    public class WhisperTranscriber : ITranscriber
    {
        private const string ModelName = "ggml-base.bin";

        public async Task<string> TranscribeAsync(string audioFilePath)
        {
            if (!File.Exists(ModelName))
            {
                Console.WriteLine("Модель для транскрипции (ggml-base.bin) не найдена. Скачиваю...");

                using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Base);
                using var fileWriter = File.OpenWrite(ModelName);

                await modelStream.CopyToAsync(fileWriter);
                Console.WriteLine("Модель успешно загружена.");
            }

            string wavPath = Path.ChangeExtension(audioFilePath, ".wav");
            string ffmpegBin = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

            Console.WriteLine("Конвертирую аудио в WAV формат...");

            using var ffmpegProcess = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegBin,
                Arguments = $"-y -i \"{audioFilePath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{wavPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (ffmpegProcess != null)
                await ffmpegProcess.WaitForExitAsync();

            Console.WriteLine("Начиналось распознавание речи (это может занять некоторое время)...");

            var resultText = new StringBuilder();

            using (var whisperFactory = WhisperFactory.FromPath(ModelName))
            using (var processor = whisperFactory.CreateBuilder().WithLanguage("auto").Build())
            using (var fileStream = File.OpenRead(wavPath))
            {
                await foreach (var segment in processor.ProcessAsync(fileStream))
                {
                    resultText.Append(segment.Text);
                }
            }

            if (File.Exists(wavPath))
                File.Delete(wavPath);

            return resultText.ToString();
        }
    }
}