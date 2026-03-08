using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.CheckFileHash;

public class CheckFileHashCommand : IRequest<CheckFileHashResponse>
{
    /// <summary>
    /// SHA256 hash of the file (hex string, 64 characters).
    /// </summary>
    public string FileHash { get; set; } = string.Empty;
}
