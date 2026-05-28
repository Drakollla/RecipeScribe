using Core.Models;

namespace Core.Contracts
{
    public interface IVideoDownloader
    {
        Task<ViewMetadata> DownloadAudioAsync(string videoUrl);
        Task<string?> GetFirstCommentAsync(string videoUrl);
    }
}