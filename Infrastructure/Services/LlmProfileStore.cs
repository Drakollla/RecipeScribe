using Core.Contracts;
using Core.Helpers;
using Core.Models;
using System.Text.Json;

namespace Infrastructure.Services;

public class LlmProfileStore : ILlmProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;

    public LlmProfileStore()
    {
        _directory = Path.Combine(ToolPaths.Directory, "LlmProfiles");
    }

    public IReadOnlyList<LlmProfile> GetAll()
    {
        if (!Directory.Exists(_directory))
            return Array.Empty<LlmProfile>();

        var profiles = new List<LlmProfile>();

        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            var profile = ReadFile(file);

            if (profile != null)
                profiles.Add(profile);
        }

        return profiles.OrderBy(p => p.Name).ToList();
    }

    public LlmProfile? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var file = GetFilePath(name);

        return File.Exists(file) ? ReadFile(file) : null;
    }

    public void Save(LlmProfile profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("Profile name is required.");

        Directory.CreateDirectory(_directory);
        var file = GetFilePath(profile.Name);
        var json = JsonSerializer.Serialize(new LlmProfileFile(profile.Endpoint, profile.ModelId), JsonOptions);
        File.WriteAllText(file, json);
    }

    public void Delete(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        var file = GetFilePath(name);

        if (File.Exists(file))
            File.Delete(file);
    }

    private LlmProfile? ReadFile(string file)
    {
        try
        {
            var data = JsonSerializer.Deserialize<LlmProfileFile>(File.ReadAllText(file), JsonOptions);
            
            if (data == null || string.IsNullOrWhiteSpace(data.Endpoint) && string.IsNullOrWhiteSpace(data.ModelId))
                return null;

            return new LlmProfile
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Endpoint = data.Endpoint ?? string.Empty,
                ModelId = data.ModelId ?? string.Empty
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string GetFilePath(string name)
    {
        var safeName = name.Trim();
        
        foreach (var c in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(c, '_');

        return Path.Combine(_directory, safeName + ".json");
    }

    private sealed record LlmProfileFile(string? Endpoint, string? ModelId);
}
