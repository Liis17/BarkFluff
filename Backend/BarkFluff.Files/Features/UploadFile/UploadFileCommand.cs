using MediatR;

namespace BarkFluff.Files.Features.UploadFile;

public class UploadFileCommand : IRequest<string>, IDisposable
{
    public Guid FileId { get; set; }

    public Stream FileStream { get; set; }

    public string FileName { get; set; }

    public void Dispose()
    {
        FileStream?.Dispose();
    }
}