using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetTempDownloadUrl;

public class GetTempDownloadUrlCommand : IRequest<GetTempDownloadUrlResponse>
{
    public List<Guid> FileIds { get; set; }

    /// <summary>Federated-вложения (этап 3.3): байты живут на origin-ноде.</summary>
    public List<FederatedFileRequest> FedFiles { get; set; } = [];

    /// <summary>Кто запрашивает — нужен проверке членства в чате на нашей стороне.</summary>
    public long RequesterUserId { get; set; }
}

public record FederatedFileRequest(string OriginServer, string FileId);
