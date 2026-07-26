using BarkFluff.Files.Features.AddSticker;
using BarkFluff.Files.Features.CreateStickerPack;
using BarkFluff.Files.Features.DeleteStickerPack;
using BarkFluff.Files.Features.GetFileData;
using BarkFluff.Files.Features.GetFilesData;
using BarkFluff.Files.Features.GetStickerPack;
using BarkFluff.Files.Features.GetStickers;
using BarkFluff.Files.Features.FetchFileStream;
using BarkFluff.Files.Features.GetTempDownloadUrl;
using BarkFluff.Files.Features.GetUserStorageInfoServer;
using BarkFluff.Files.Features.ListStickerPacks;
using BarkFluff.Files.Features.RemoveSticker;
using BarkFluff.Files.Features.UpdateSticker;
using BarkFluff.Files.Features.UpdateStickerPack;
using BarkFluff.Files.Features.UploadAvatarServer;
using BarkFluff.Files.Features.UploadBadgeImage;
using BarkFluff.Files.Features.UploadFileServer;
using BarkFluff.Files.Features.UploadPosterServer;
using BarkFluff.Files.Features.UploadStickerImage;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Files.Host;

[Authorize(Policy = nameof(TokenType.Service))]
public class FilesServerApiService : FilesServerApi.FilesServerApiBase
{
    private readonly IMediator _mediator;
    private readonly FetchFileStreamQueryHandler _fetchFileStreamHandler;

    public FilesServerApiService(IMediator mediator, FetchFileStreamQueryHandler fetchFileStreamHandler)
    {
        _mediator = mediator;
        _fetchFileStreamHandler = fetchFileStreamHandler;
    }

    // Стриминг содержимого файла ноде-партнёру (этап 3.2). Не через MediatR: результат — поток.
    public override Task FetchFileStream(
        FetchFileStreamRequest request,
        IServerStreamWriter<FileStreamChunk> responseStream,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.FileId, out var fileId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "file_id не является UUID"));
        }

        var query = new FetchFileStreamQuery
        {
            FileId = fileId,
            RangeFrom = request.RangeFrom,
            RangeTo = request.RangeTo,
        };

        return _fetchFileStreamHandler.HandleAsync(query, responseStream, context.CancellationToken);
    }

    public override Task<GetFileDataResponse> GetFileData(GetFileDataRequest request, ServerCallContext context)
    {
        var command = new GetFileDataCommand()
        {
            FileId = Guid.Parse(request.FileId)
        };

        return _mediator.Send(command);
    }

    public override Task<GetFilesDataResponse> GetFilesData(GetFilesDataRequest request, ServerCallContext context)
    {
        var command = new GetFilesDataCommand()
        {
            FileIds = request.FileIds.Select(Guid.Parse).ToList()
        };

        return _mediator.Send(command);
    }

    public override Task<GetTempDownloadUrlResponse> GetTempDownloadUrlServer(GetTempDownloadUrlRequest request, ServerCallContext context)
    {
        var command = new GetTempDownloadUrlCommand()
        {
            FileIds = request.FileIds.Select(Guid.Parse).ToList()
        };

        return _mediator.Send(command);
    }

    public override Task<UploadBadgeImageResponse> UploadBadgeImage(UploadBadgeImageRequest request, ServerCallContext context)
    {
        var command = new UploadBadgeImageCommand
        {
            ImageData = request.ImageData.ToByteArray(),
            Filename = request.Filename
        };

        return _mediator.Send(command);
    }

    public override Task<GetUserStorageInfoResponse> GetUserStorageInfoServer(GetUserStorageInfoServerRequest request, ServerCallContext context)
    {
        var command = new GetUserStorageInfoServerCommand
        {
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    public override Task<UploadAvatarServerResponse> UploadAvatarServer(UploadAvatarServerRequest request, ServerCallContext context)
    {
        var command = new UploadAvatarServerCommand
        {
            ImageData = request.ImageData.ToByteArray(),
            Filename = request.Filename,
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    public override Task<UploadPosterServerResponse> UploadPosterServer(UploadPosterServerRequest request, ServerCallContext context)
    {
        var command = new UploadPosterServerCommand
        {
            ImageData = request.ImageData.ToByteArray(),
            Filename = request.Filename,
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    public override Task<UploadFileServerResponse> UploadFileServer(UploadFileServerRequest request, ServerCallContext context)
    {
        var command = new UploadFileServerCommand
        {
            Data = request.Data.ToByteArray(),
            Filename = request.Filename,
            FileType = (BarkFluff.Files.Domain.UploadFileType)(int)request.FileType,
            OwnerUserId = request.OwnerUserId
        };

        return _mediator.Send(command);
    }

    // --- Стикерпаки ---

    public override Task<CreateStickerPackResponse> CreateStickerPack(CreateStickerPackRequest request, ServerCallContext context)
    {
        var command = new CreateStickerPackCommand
        {
            CreatorUserId = request.CreatorUserId,
            Name = request.Name,
            Description = request.Description
        };

        return _mediator.Send(command);
    }

    public override Task<UpdateStickerPackResponse> UpdateStickerPack(UpdateStickerPackRequest request, ServerCallContext context)
    {
        var command = new UpdateStickerPackCommand
        {
            PackId = Guid.Parse(request.PackId),
            Name = request.Name,
            Description = request.Description,
            CoverStickerId = string.IsNullOrEmpty(request.CoverStickerId) ? null : Guid.Parse(request.CoverStickerId)
        };

        return _mediator.Send(command);
    }

    public override Task<DeleteStickerPackResponse> DeleteStickerPack(DeleteStickerPackRequest request, ServerCallContext context)
    {
        var command = new DeleteStickerPackCommand
        {
            PackId = Guid.Parse(request.PackId)
        };

        return _mediator.Send(command);
    }

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

    // --- Стикеры ---

    public override Task<AddStickerResponse> AddSticker(AddStickerRequest request, ServerCallContext context)
    {
        var command = new AddStickerCommand
        {
            PackId = Guid.Parse(request.PackId),
            FileId = Guid.Parse(request.FileId),
            Emoji = request.Emoji
        };

        return _mediator.Send(command);
    }

    public override Task<RemoveStickerResponse> RemoveSticker(RemoveStickerRequest request, ServerCallContext context)
    {
        var command = new RemoveStickerCommand
        {
            StickerId = Guid.Parse(request.StickerId)
        };

        return _mediator.Send(command);
    }

    public override Task<UpdateStickerResponse> UpdateSticker(UpdateStickerRequest request, ServerCallContext context)
    {
        var command = new UpdateStickerCommand
        {
            StickerId = Guid.Parse(request.StickerId),
            Emoji = request.Emoji
        };

        return _mediator.Send(command);
    }

    public override Task<GetStickersResponse> GetStickers(GetStickersRequest request, ServerCallContext context)
    {
        var command = new GetStickersCommand
        {
            StickerIds = request.StickerIds.Select(Guid.Parse).ToList()
        };

        return _mediator.Send(command);
    }

    // --- Загрузка изображения стикера ---

    public override Task<UploadStickerImageResponse> UploadStickerImage(UploadStickerImageRequest request, ServerCallContext context)
    {
        var command = new UploadStickerImageCommand
        {
            ImageData = request.ImageData.ToByteArray(),
            Filename = request.Filename
        };

        return _mediator.Send(command);
    }
}