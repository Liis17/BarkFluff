namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    public AttachmentComposer AttachmentComposer { get; }
    public bool CanAttach => SelectedChat is { IsPrivate: false, CanSend: true } && !IsEditing;
    public bool CanAddAttachments => CanAttach && !Voice.IsBusy && !AttachmentComposer.HasVoice;
    public bool HasPrivateAttachmentRestriction => SelectedChat?.IsPrivate == true;

    public Task AddAttachmentsAsync(Func<CancellationToken, Task<IReadOnlyList<AttachmentSourceFile>>> read)
    {
        if (!CanAddAttachments || AttachmentComposer.IsSending || SelectedChat is not { } chat)
            return Task.CompletedTask;
        var session = _realtimeSession;
        var version = _chatVersion;
        return IsOpen(session, chat, version)
            ? AttachmentComposer.AddAsync(read, () => CanAddAttachments && IsOpen(session, chat, version))
            : Task.CompletedTask;
    }

    public Task CancelAttachmentsAsync()
    {
        return Task.WhenAll(Voice.CancelAsync(), AttachmentComposer.ClearAsync());
    }
}
