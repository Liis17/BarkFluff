using BarkFluff.Proto.Files;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.Core.Services;

public readonly record struct OutgoingScope(string Node, long UserId)
{
    public static OutgoingScope Create(string node, long userId) =>
        new(node.Trim().TrimEnd('/').ToUpperInvariant(), userId);
}

public sealed record OutgoingFile(Guid Id, string Name, string? ContentType, UploadFileType Type, string Extension);

public sealed record OutgoingMessage(Guid Id, string ChatId, string Text, long ReplyId, long[] Forwards,
    string[] Attachments, OutgoingFile[] Files, string?[] Uploaded, string? Error = null, bool QuotaChecked = false);

public interface IOutgoingMessageStore
{
    IReadOnlyList<OutgoingMessage> Load(OutgoingScope scope);
    IReadOnlyList<(OutgoingScope Scope, Guid Id)> ListOperations();
    void Save(OutgoingScope scope, OutgoingMessage message);
    void Remove(OutgoingScope scope, Guid id);
    void Clear(OutgoingScope scope);
}

public interface IOutgoingMessageTransport
{
    Task<long?> GetStorageAvailableAsync(OutgoingScope scope, CancellationToken token);

    Task<(ErrorReturner error, string? fileId)> UploadAsync(OutgoingScope scope, OutgoingFile file,
        string path, IProgress<double> progress, CancellationToken token);

    Task<(ErrorReturner error, MessageModel? message)> SendAsync(OutgoingScope scope, OutgoingMessage message,
        CancellationToken token);
}

internal enum OutgoingState { Waiting, Uploading, Sending, Failed }

internal sealed record OutgoingStatus(OutgoingMessage Message, OutgoingState State, double Progress);

