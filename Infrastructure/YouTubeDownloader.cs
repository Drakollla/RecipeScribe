using Core.Contracts;
using Core.Models;
using YoutubeDLSharp;

namespace Infrastructure
{
    public class YouTubeDownloader : IYouTubeDownloader
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
            string title = video.Data.Title;
            string description = video.Data.Description;
            string outputFilePath = Path.Combine(folderPath, $"{title}.mp4");

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
    }
}