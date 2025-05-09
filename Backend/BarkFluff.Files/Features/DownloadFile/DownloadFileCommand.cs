using MediatR;

namespace BarkFluff.Files.Features.DownloadFile;

public class DownloadFileCommand : IRequest<DownloadFileResult>
{
    public Guid FileId { get; set; }
}

public class DownloadFileResult
{
    public Stream FileStream { get; set; }
    public string FileName { get; set; }
    public string ContentType { get; set; }
}
