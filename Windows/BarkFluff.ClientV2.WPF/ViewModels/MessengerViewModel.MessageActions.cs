using BarkFluff.ClientV2.WPF.Infrastructure.Converters;
using BarkFluff.WebApi.Core;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Windows;

using PinnedMessageInfo = BarkFluff.Proto.Shared.PinnedMessageInfo;

namespace BarkFluff.ClientV2.WPF.ViewModels;

/// <summary>
/// Действия над отдельным сообщением: контекстное меню, правка, удаление, закрепление.
/// Набор и условия повторяют веб-клиент.
/// </summary>
public sealed partial class MessengerViewModel
{
    private readonly List<PinnedMessageInfo> _pinnedMessages = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private MessageItemViewModel? _editingMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteConfirmVisible))]
    private MessageItemViewModel? _messagePendingDelete;

    [ObservableProperty] private string? _actionError;

    public bool IsEditing => EditingMessage is not null;

    public bool IsDeleteConfirmVisible => MessagePendingDelete is not null;

    [RelayCommand]
    private void CopyMessageText(MessageItemViewModel message)
    {
        if (message.CanCopyText)
        {
            TryUseClipboard(() => Clipboard.SetText(message.Text));
        }
    }

    [RelayCommand]
    private void CopyMessageImage(MessageItemViewModel message)
    {
        if (!message.CanCopyImage)
        {
            return;
        }

        // Как в вебе: копируем уже показанное превью, а не полный файл.
        var attachment = message.MediaAttachments.First();
        var image = StringToImageSourceConverter.TryCreate(attachment.PreviewUrl)
            ?? StringToImageSourceConverter.TryCreate(attachment.FileUrl);
        if (image is not null)
        {
            TryUseClipboard(() => Clipboard.SetImage(image));
        }
    }

    [RelayCommand]
    private async Task TogglePinMessageAsync(MessageItemViewModel message)
    {
        var chat = SelectedChat;
        if (chat is null || !message.CanUseActions)
        {
            return;
        }

        ActionError = null;
        if (message.IsPinned)
        {
            var unpinError = await _messenger.UnpinMessageAsync(chat.Id, message.Id);
            if (!unpinError.IsSuccess)
            {
                ActionError = DescribeError(unpinError);
                return;
            }

            _pinnedMessages.RemoveAll(info => info.Message?.Id == message.Id);
            message.IsPinned = false;
            return;
        }

        var (pinError, pinned) = await _messenger.PinMessageAsync(chat.Id, message.Id);
        if (!pinError.IsSuccess || pinned is null)
        {
            ActionError = DescribeError(pinError);
            return;
        }

        _pinnedMessages.Insert(0, pinned);
        message.IsPinned = true;
    }

    [RelayCommand]
    private void StartEditMessage(MessageItemViewModel message)
    {
        if (!message.CanModify)
        {
            return;
        }

        ActionError = null;
        EditingMessage = message;
        DraftText = message.Text;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingMessage = null;
        DraftText = string.Empty;
    }

    [RelayCommand]
    private void RequestDeleteMessage(MessageItemViewModel message)
    {
        if (message.CanModify)
        {
            ActionError = null;
            MessagePendingDelete = message;
        }
    }

    [RelayCommand]
    private void CancelDeleteMessage() => MessagePendingDelete = null;

    [RelayCommand]
    private async Task ConfirmDeleteMessageAsync()
    {
        var message = MessagePendingDelete;
        if (message is null)
        {
            return;
        }

        MessagePendingDelete = null;
        var error = await _messenger.DeleteMessageAsync(message.Id);
        if (!error.IsSuccess)
        {
            ActionError = DescribeError(error);
            return;
        }

        Messages.Remove(message);
        _pinnedMessages.RemoveAll(info => info.Message?.Id == message.Id);
        if (EditingMessage == message)
        {
            CancelEdit();
        }
    }

    /// <summary>
    /// Применяет правку вместо отправки нового сообщения, пока активен режим правки.
    /// </summary>
    private async Task ApplyEditAsync()
    {
        var chat = SelectedChat;
        var message = EditingMessage;
        if (chat is null || message is null || string.IsNullOrWhiteSpace(DraftText))
        {
            return;
        }

        var (error, edited) = await _messenger.EditMessageAsync(chat.Id, message.Id, DraftText.Trim());
        if (!error.IsSuccess || edited is null)
        {
            ActionError = DescribeError(error);
            return;
        }

        message.Text = edited.Text;
        message.IsEdited = true;
        EditingMessage = null;
        DraftText = string.Empty;
    }

    private async Task LoadPinnedMessagesAsync(ChatItemViewModel chat, int loadVersion)
    {
        var (error, pinned) = await _messenger.GetPinnedMessagesAsync(chat.Id);
        if (!error.IsSuccess || pinned is null || SelectedChat?.Id != chat.Id || loadVersion != _messageLoadVersion)
        {
            return;
        }

        _pinnedMessages.Clear();
        _pinnedMessages.AddRange(pinned.OrderByDescending(info => info.PinnedAt?.ToDateTimeOffset()));
        ApplyPinnedState();
    }

    private void ApplyPinnedState()
    {
        var pinnedIds = _pinnedMessages.Select(info => info.Message?.Id ?? 0).ToHashSet();
        foreach (var message in Messages)
        {
            message.IsPinned = pinnedIds.Contains(message.Id);
        }
    }

    private void ResetMessageActionState()
    {
        EditingMessage = null;
        MessagePendingDelete = null;
        ActionError = null;
        _pinnedMessages.Clear();
    }

    /// <summary>
    /// Текст ошибки приходит из WebApi.Core уже в пригодном для показа виде;
    /// общий запасной вариант нужен, когда сообщение не заполнено.
    /// </summary>
    private string DescribeError(ErrorReturner error) =>
        string.IsNullOrWhiteSpace(error.ErrorMessage)
            ? _localization.GetString("Messenger_ActionFailed")
            : error.ErrorMessage;

    private void TryUseClipboard(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception)
        {
            // Буфер обмена может быть занят другим процессом — действие просто не выполнится.
            ActionError = _localization.GetString("Messenger_ClipboardFailed");
        }
    }
}
