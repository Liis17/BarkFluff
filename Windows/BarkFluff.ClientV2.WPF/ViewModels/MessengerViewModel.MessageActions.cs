using BarkFluff.ClientV2.WPF.Infrastructure.Converters;
using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.WebApi.Core;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;
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

    private long _forwardSourceMessageId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsComposerHintVisible), nameof(ComposerHintTitle), nameof(ComposerHintPreview))]
    private MessageItemViewModel? _editingMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReplying), nameof(IsComposerHintVisible), nameof(ComposerHintTitle), nameof(ComposerHintPreview))]
    private MessageItemViewModel? _replyTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteConfirmVisible))]
    private MessageItemViewModel? _messagePendingDelete;

    [ObservableProperty] private string? _actionError;
    [ObservableProperty] private bool _isForwardVisible;
    [ObservableProperty] private string _forwardComment = string.Empty;

    public ObservableCollection<ForwardTargetViewModel> ForwardTargets { get; } = [];

    public bool IsEditing => EditingMessage is not null;

    public bool IsReplying => ReplyTarget is not null;

    public bool IsDeleteConfirmVisible => MessagePendingDelete is not null;

    public bool IsComposerHintVisible => IsEditing || IsReplying;

    public string ComposerHintTitle => _localization.GetString(IsEditing ? "Messenger_EditingMessage" : "Messenger_ReplyingTo");

    public string ComposerHintPreview => (EditingMessage ?? ReplyTarget)?.Text ?? string.Empty;

    public bool CanSubmitForward => ForwardTargets.Any(target => target.IsSelected);

    public string ForwardSelectionSummary => CanSubmitForward
        ? $"{_localization.GetString("Messenger_ForwardSelected")} {ForwardTargets.Count(target => target.IsSelected)}"
        : _localization.GetString("Messenger_ForwardNoneSelected");

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
        ReplyTarget = null;
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
    private void StartReplyMessage(MessageItemViewModel message)
    {
        if (!message.CanUseActions)
        {
            return;
        }

        ActionError = null;
        CancelEdit();
        ReplyTarget = message;
    }

    /// <summary>
    /// Кнопка отмены в панели над полем ввода: закрывает активный режим,
    /// но при отмене ответа набранный текст не теряется.
    /// </summary>
    [RelayCommand]
    private void CancelComposerHint()
    {
        if (IsEditing)
        {
            CancelEdit();
            return;
        }

        ReplyTarget = null;
    }

    [RelayCommand]
    private void ScrollToOriginal(MessageItemViewModel message)
    {
        if (message.IsReplyQuote && message.Forwarded is not null)
        {
            ScrollRequest = new MessageScrollRequest(MessageScrollTarget.Message, message.Forwarded.OriginalMessageId);
        }
    }

    [RelayCommand]
    private void StartForwardMessage(MessageItemViewModel message)
    {
        if (!message.CanUseActions)
        {
            return;
        }

        ActionError = null;
        // Пересылаем оригинал, а не саму пересылку — так же поступает веб-клиент.
        _forwardSourceMessageId = message.Forwarded is { OriginalMessageId: > 0 } forwarded
            ? forwarded.OriginalMessageId
            : message.Id;

        ForwardComment = string.Empty;
        ForwardTargets.Clear();
        // Приватные чаты шифруются отдельным методом отправки, пересылка в них не поддерживается.
        foreach (var chat in Chats.Where(chat => !chat.IsPrivate))
        {
            ForwardTargets.Add(new ForwardTargetViewModel(chat.Id, chat.Title, chat.Initials));
        }

        NotifyForwardSelectionChanged();
        IsForwardVisible = true;
    }

    [RelayCommand]
    private void ToggleForwardTarget(ForwardTargetViewModel target)
    {
        target.IsSelected = !target.IsSelected;
        NotifyForwardSelectionChanged();
    }

    [RelayCommand]
    private void CancelForward()
    {
        IsForwardVisible = false;
        ForwardTargets.Clear();
        ForwardComment = string.Empty;
        NotifyForwardSelectionChanged();
    }

    [RelayCommand]
    private async Task SubmitForwardAsync()
    {
        var currentUserId = _messenger.CurrentUserId;
        var targetChatIds = ForwardTargets.Where(target => target.IsSelected).Select(target => target.ChatId).ToArray();
        if (currentUserId is null || targetChatIds.Length == 0 || _forwardSourceMessageId == 0)
        {
            return;
        }

        var comment = ForwardComment.Trim();
        IsForwardVisible = false;
        foreach (var chatId in targetChatIds)
        {
            var (error, message) = await _messenger.SendMessageAsync(chatId, comment, _forwardSourceMessageId);
            if (!error.IsSuccess)
            {
                ActionError = DescribeError(error);
                continue;
            }

            if (message is not null && SelectedChat?.Id == chatId)
            {
                Messages.Add(await CreateMessageItemAsync(message, currentUserId.Value));
                ApplyReplyQuoteState();
                ScrollRequest = new MessageScrollRequest(MessageScrollTarget.Bottom);
            }
        }

        CancelForward();
    }

    private void NotifyForwardSelectionChanged()
    {
        OnPropertyChanged(nameof(CanSubmitForward));
        OnPropertyChanged(nameof(ForwardSelectionSummary));
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
        ReplyTarget = null;
        MessagePendingDelete = null;
        ActionError = null;
        _pinnedMessages.Clear();
        CancelForward();
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

/// <summary>
/// Чат в списке получателей пересылки.
/// </summary>
public sealed partial class ForwardTargetViewModel(string chatId, string title, string initials) : ObservableObject
{
    public string ChatId { get; } = chatId;
    public string Title { get; } = title;
    public string Initials { get; } = initials;

    [ObservableProperty] private bool _isSelected;
}
