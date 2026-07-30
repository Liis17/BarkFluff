using BarkFluff.Files.Features.CheckFileHash;
using BarkFluff.Files.Features.GetStickerPack;
using BarkFluff.Files.Features.GetTempDownloadUrl;
using BarkFluff.Files.Features.GetUploadUrl;
using BarkFluff.Files.Features.GetUserStorageInfo;
using BarkFluff.Files.Features.ListStickerPacks;
using BarkFluff.Proto.Files;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Exceptions.Files;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Host;

[Authorize(Policy = nameof(TokenType.User))]
public class FilesApiService : FilesApi.FilesApiBase
{
    private readonly IMediator _mediator;
    private readonly UserContext _userContext;
    private readonly ILogger<FilesApiService> _logger;

    public FilesApiService(IMediator mediator, UserContext userContext, ILogger<FilesApiService> logger)
    {
        _mediator = mediator;
        _userContext = userContext;
        _logger = logger;
    }

    public override Task<GetUploadUrlResponse> GetUploadUrl(GetUploadUrlRequest request, ServerCallContext context)
    {
        var command = new GetUploadUrlCommand()
        {
            Type = (UploadFileType)(int)request.FileType
        };

        return _mediator.Send(command);
    }


    public override async Task<GetTempDownloadUrlResponse> GetTempDownloadUrl(GetTempDownloadUrlRequest request, ServerCallContext context)
    {
        var guids = new List<Guid>(request.FileIds.Count);

        foreach (var fileId in request.FileIds)
        {
            // Клиент может прислать не-guid (например S3-ключ вместо идентификатора файла) —
            // это ошибка клиента, а не сбой сервиса, поэтому не даём Guid.Parse уронить вызов.
            if (!Guid.TryParse(fileId, out var guid))
            {
                _logger.LogWarning("Невалидный file_id в запросе GetTempDownloadUrl: {FileId}", fileId);

                throw new NotValidFileIdException();
            }

            guids.Add(guid);
        }

        var command = new GetTempDownloadUrlCommand()
        {
            FileIds = guids,
            // Federated-вложения (этап 3.3): доступ проверяется по членству запрашивающего в чате.
            FedFiles = request.FedFiles
                .Select(f => new FederatedFileRequest(f.OriginServer, f.FileId))
                .ToList(),
            RequesterUserId = _userContext.UserId,
        };

        return await _mediator.Send(command);
    }

    public override async Task<CheckFileHashResponse> CheckFileHash(CheckFileHashRequest request, ServerCallContext context)
    {
        var command = new CheckFileHashCommand()
        {
            FileHash = request.FileHash
        };

        return await _mediator.Send(command);
    }

    public override async Task<GetUserStorageInfoResponse> GetUserStorageInfo(GetUserStorageInfoRequest request, ServerCallContext context)
    {
        var command = new GetUserStorageInfoCommand();

        return await _mediator.Send(command);
    }

    // --- Стикерпаки (только чтение) ---

    public override Task<ListStickerPacksResponse> ListStickerPacks(ListStickerPacksRequest request, ServerCallContext context)
    {
        var command = new ListStickerPacksCommand
        {
            Offset = request.Pagination?.Offset ?? 0,
            Limit = request.Pagination?.Size ?? 20
        };

        return _mediator.Send(command);
    }

    public override Task<GetStickerPackResponse> GetStickerPack(GetStickerPackRequest request, ServerCallContext context)
    {
        var command = new GetStickerPackCommand
        {
            PackId = Guid.Parse(request.PackId)
        };

        return _mediator.Send(command);
    }
}
