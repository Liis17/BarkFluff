using BarkFluff.Bots.Features.SendBotMessage;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.SendBotFile;

public class SendBotFileCommandHandler : IRequestHandler<SendBotFileCommand, Proto.Shared.Message>
{
    private const long BotStorageQuotaBytes = 1L * 1024 * 1024 * 1024; // 1 ГБ

    private readonly FilesServerApi.FilesServerApiClient _filesClient;
    private readonly IMediator _mediator;

    public SendBotFileCommandHandler(FilesServerApi.FilesServerApiClient filesClient, IMediator mediator)
    {
        _filesClient = filesClient;
        _mediator = mediator;
    }

    public async Task<Proto.Shared.Message> Handle(SendBotFileCommand request, CancellationToken cancellationToken)
    {
        // Квота хранилища вложений бота — 1 ГБ
        var storageInfo = await _filesClient.GetUserStorageInfoServerAsync(
            new GetUserStorageInfoServerRequest { UserId = request.BotId },
            cancellationToken: cancellationToken);

        if (storageInfo.TotalUsedStorage + request.Data.Length > BotStorageQuotaBytes)
            throw new BotStorageQuotaExceededException();

        var uploaded = await _filesClient.UploadFileServerAsync(new UploadFileServerRequest
        {
            Data = Google.Protobuf.ByteString.CopyFrom(request.Data),
            Filename = request.FileName,
            FileType = request.FileType,
            OwnerUserId = request.BotId,
        }, cancellationToken: cancellationToken);

        return await _mediator.Send(new SendBotMessageCommand
        {
            BotId = request.BotId,
            ChatId = request.ChatId,
            UserId = request.UserId,
            Text = request.Caption,
            FileIds = [uploaded.FileId],
        }, cancellationToken);
    }
}
