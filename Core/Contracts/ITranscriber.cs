namespace Core.Contracts
{
    public interface ITranscriber
    {
        Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default);
    }
}