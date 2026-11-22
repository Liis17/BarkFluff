using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    public AttachmentComposer AttachmentComposer { get; }
    public bool CanAttach => SelectedChat is { IsPrivate: false, CanSend: true } && !IsEditing;
    public bool HasPrivateAttachmentRestriction => SelectedChat?.IsPrivate == true;

    public Task AddAttachmentsAsync(Func<CancellationToken, Task<IReadOnlyList<AttachmentSourceFile>>> read)
    {
        if (!CanAttach || AttachmentComposer.IsSending || SelectedChat is not { } chat)
            return Task.CompletedTask;
        var session = _realtimeSession;
        var version = _chatVersion;
        return IsOpen(session, chat, version)
            ? AttachmentComposer.AddAsync(read, () => CanAttach && IsOpen(session, chat, version))
            : Task.CompletedTask;
    }

    public Task RetryAttachmentAsync(PendingAttachment file)
    {
        if (!CanAttach || AttachmentComposer.IsSending || SelectedChat is not { } chat)
            return Task.CompletedTask;
        var session = _realtimeSession;
        var version = _chatVersion;
        return AttachmentComposer.UploadAsync(file, () => IsOpen(session, chat, version));
    }

    public Task CancelAttachmentsAsync() => AttachmentComposer.ClearAsync();

    private async Task<(ErrorReturner error, MessageModel? message)?> SendWithAttachmentsAsync(
        ChatItemViewModel chat, RealtimeSession session, int version, string text, long replyId)
    {
        var composer = AttachmentComposer;
        var token = composer.ScopeToken;
        bool IsCurrent() => IsOpen(session, chat, version) && !token.IsCancellationRequested;
        composer.IsSending = true;
        try
        {
            var ids = await composer.PrepareAsync(token, IsCurrent);
            if (ids is null || !IsCurrent())
                return null;

            var result = await _messenger.SendMessageAsync(chat.Id, text, replyId, cancellationToken: token, attachmentIds: ids);
            if (!IsCurrent())
                return null;

            if (result.error is { IsSuccess: true } && result.message is not null)
                _ = composer.ClearAsync();
            return result;
        }
        finally
        {
            if (IsCurrent())
                composer.IsSending = false;
        }
    }
}
