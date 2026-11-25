namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    public ChatMediaViewer MediaViewer { get; }

    public Task OpenMediaAsync(MessageAttachmentItemViewModel attachment)
    {
        if (SecretChats.IsOpen || SelectedChat is { IsPrivate: true })
            return Task.CompletedTask;

        var chatId = IsProfileVisible ? Profile.AttachmentChatId
            : SelectedChat?.Id;
        return chatId is null ? Task.CompletedTask : MediaViewer.OpenAsync(chatId, attachment);
    }
}
