namespace Barkfluff.Docker.Control.Models.Dtos;

public class AuthStatusResponse
{
    public AuthRequestStatus Status { get; set; }
    public string? Token { get; set; }
    public string? Message { get; set; }
}
