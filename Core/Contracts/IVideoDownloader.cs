using Core.Helpers;

namespace Core.Contracts
{
    public interface IVideoDownloader
    {
        Task<ViewMetadata> DownloadAudioAsync(string videoUrl, CancellationToken ct = default);
        Task<string?> GetFirstCommentAsync(string videoUrl, CancellationToken ct = default);
    }
}