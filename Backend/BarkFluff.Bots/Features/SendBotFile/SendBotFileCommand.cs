using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Bots.Features.SendBotFile;

/// <summary>Загрузка файла (квота 1 ГБ) и отправка сообщением от имени бота — HTTP sendPhoto/sendDocument.</summary>
public class SendBotFileCommand : IRequest<Proto.Shared.Message>
{
    public long BotId { get; set; }

    public string? ChatId { get; set; }

    public long? UserId { get; set; }

    public string Caption { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public byte[] Data { get; set; } = [];

    public UploadFileType FileType { get; set; }
}
