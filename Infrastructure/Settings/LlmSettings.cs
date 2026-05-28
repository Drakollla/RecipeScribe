namespace Infrastructure.Settings
{
    public class LlmSettings
    {
        public string Endpoint { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
    }
}