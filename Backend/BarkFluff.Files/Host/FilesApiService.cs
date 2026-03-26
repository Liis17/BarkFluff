using BarkFluff.Files.Features.CheckFileHash;
using BarkFluff.Files.Features.GetStickerPack;
using BarkFluff.Files.Features.GetTempDownloadUrl;
using BarkFluff.Files.Features.GetUploadUrl;
using BarkFluff.Files.Features.GetUserStorageInfo;
using BarkFluff.Files.Features.ListStickerPacks;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Files;
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
    private readonly MetricsCollector _metrics;

    public FilesApiService(IMediator mediator, MetricsCollector metrics)
    {
        _mediator = mediator;
        _metrics = metrics;
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
        _metrics.Increment("files_downloaded");
        var guids = request.FileIds.Select(Guid.Parse).ToList();

        var command = new GetTempDownloadUrlCommand()
        {
            FileIds = guids
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