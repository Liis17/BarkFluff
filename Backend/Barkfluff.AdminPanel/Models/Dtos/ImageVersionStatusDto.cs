namespace Barkfluff.AdminPanel.Models.Dtos;

public class ImageVersionStatusDto
{
    public string? CurrentVersion { get; init; }
    public string? LatestVersion { get; init; }
    public bool UpdateAvailable { get; init; }
}
