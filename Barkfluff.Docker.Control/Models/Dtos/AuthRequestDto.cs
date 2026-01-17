namespace Barkfluff.Docker.Control.Models.Dtos;

public class AuthRequestDto
{
    public string? IpAddress { get; set; }
    public string? Browser { get; set; }
    public string? Os { get; set; }
    public string? UserAgent { get; set; }
    public string? TokenName { get; set; }
}
