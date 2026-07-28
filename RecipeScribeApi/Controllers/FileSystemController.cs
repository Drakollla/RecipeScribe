using Microsoft.AspNetCore.Mvc;

namespace RecipeScribeApi.Controllers;

[ApiController]
[Route("api/filesystem")]
public class FileSystemController : ControllerBase
{
    [HttpGet("directories")]
    public IActionResult GetDirectories([FromQuery] string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Ok(new { current = "", dirs = GetDrives() });

        if (!Path.IsPathRooted(path))
            return BadRequest(new { error = "Invalid path." });

        if (!Directory.Exists(path))
            return BadRequest(new { error = "Directory not found." });

        try
        {
            var dirs = Directory.GetDirectories(path)
                .Select(d => new DirectoryInfo(d))
                .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                .Select(d => d.Name)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Ok(new { current = path, dirs });
        }
        catch (UnauthorizedAccessException)
        {
            return Ok(new { current = path, dirs = new List<string>(), error = "Access denied." });
        }
    }

    private static List<string> GetDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType != DriveType.CDRom)
            .Select(d => d.Name)
            .OrderBy(d => d)
            .ToList();
    }
}
