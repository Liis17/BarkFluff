using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Bots;
using BarkFluff.Proto.Files;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Bots.Features.GetBotFile;

public class GetBotFileQueryHandler : IRequestHandler<GetBotFileQuery, GetFileResponse>
{
    private readonly FilesServerApi.FilesServerApiClient _filesClient;
    private readonly MetricsCollector _metrics;

    public GetBotFileQueryHandler(FilesServerApi.FilesServerApiClient filesClient, MetricsCollector metrics)
    {
        _filesClient = filesClient;
        _metrics = metrics;
    }

    public async Task<GetFileResponse> Handle(GetBotFileQuery request, CancellationToken cancellationToken)
    {
        _metrics.Increment("bot_api_file_requests");

        // Files парсит file_id как Guid — невалидную строку отсекаем до вызова
        if (!Guid.TryParse(request.FileId, out _))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "file_id обязателен"));
        }

        // Вложения сообщений по прямому file_id не отдаются — нужна временная ссылка
        var temp = await _filesClient.GetTempDownloadUrlServerAsync(
            new GetTempDownloadUrlRequest { FileIds = { request.FileId } },
            cancellationToken: cancellationToken);

        var link = temp.FileUrls.FirstOrDefault();

        if (link is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Файл не найден"));
        }

        // Имени и размера во временной ссылке нет — берём из метаданных файла
        var fileData = await _filesClient.GetFileDataAsync(
            new GetFileDataRequest { FileId = request.FileId },
            cancellationToken: cancellationToken);

        return new GetFileResponse
        {
            FileId = request.FileId,
            FileName = fileData.FileInfo.FileName,
            FileSize = fileData.FileInfo.FileSize,
            FileUrl = link.Url,
        };
    }
}
