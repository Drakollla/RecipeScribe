using Core.Models;

namespace Core.Contracts
{
    public interface IYouTubeDownloader
    {
        Task<ViewMetadata> DownloadAudioAsync(string videoUrl);
    }
}