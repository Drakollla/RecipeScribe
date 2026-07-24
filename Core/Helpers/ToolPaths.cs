namespace Core.Helpers;

public static class ToolPaths
{
    public static string Directory => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Core", "Tools"));
}
