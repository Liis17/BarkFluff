using BarkFluff.Client.Core.Models;
using BarkFluff.WebApi.Core;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

using PinnedMessageInfo = BarkFluff.Proto.Shared.PinnedMessageInfo;

namespace BarkFluff.Client.Core.ViewModels;

/// <summary>
/// Действия над отдельным сообщением: контекстное меню, правка, удаление, закрепление.
/// Набор и условия повторяют веб-клиент.
/// </summary>
public sealed partial class MessengerViewModel
{
    private readonly List<PinnedMessageInfo> _pinnedMessages = [];

    private long _forwardSourceMessageId;
    private int _pinnedBarIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinnedMessages))]
    private PinnedPreviewViewModel? _pinnedPreview;

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

    public bool HasPinnedMessages => PinnedPreview is not null;

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
            TryUseClipboard(() =>
            {
                var package = new DataPackage();
                package.SetText(message.Text);
                Clipboard.SetContent(package);
            });
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
        var source = attachment.PreviewUrl is { Length: > 0 } preview ? preview : attachment.FileUrl;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return;
        }

        TryUseClipboard(() =>
        {
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromUri(uri));
            Clipboard.SetContent(package);
        });
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
            _pinnedBarIndex = 0;
            RefreshPinnedBar();
            return;
        }

        var (pinError, pinned) = await _messenger.PinMessageAsync(chat.Id, message.Id);
        if (!pinError.IsSuccess || pinned is null)
        {
            ActionError = DescribeError(pinError);
            return;
        }

        // Новый закреп показываем первым — как это делает веб-клиент.
        _pinnedMessages.RemoveAll(info => info.Message?.Id == message.Id);
        _pinnedMessages.Insert(0, pinned);
        message.IsPinned = true;
        _pinnedBarIndex = 0;
        RefreshPinnedBar();
    }

    /// <summary>
    /// Клик по плашке: переход к текущему закрепу и перелистывание на следующий по кругу.
    /// </summary>
    [RelayCommand]
    private void CyclePinnedMessage()
    {
        if (_pinnedMessages.Count == 0)
        {
            return;
        }

        var messageId = _pinnedMessages[_pinnedBarIndex].Message?.Id ?? 0;
        if (messageId > 0)
        {
            RequestScroll(MessageScrollTarget.Message, messageId);
        }

        if (_pinnedMessages.Count > 1)
        {
            _pinnedBarIndex = (_pinnedBarIndex + 1) % _pinnedMessages.Count;
            RefreshPinnedBar();
        }
    }

    [RelayCommand]
    private async Task UnpinFromBarAsync()
    {
        var chat = SelectedChat;
        var preview = PinnedPreview;
        if (chat is null || preview is null)
        {
            return;
        }

        var error = await _messenger.UnpinMessageAsync(chat.Id, preview.MessageId);
        if (!error.IsSuccess)
        {
            ActionError = DescribeError(error);
            return;
        }

        _pinnedMessages.RemoveAll(info => info.Message?.Id == preview.MessageId);
        var message = Messages.FirstOrDefault(item => item.Id == preview.MessageId);
        if (message is not null)
        {
            message.IsPinned = false;
        }

        _pinnedBarIndex = 0;
        RefreshPinnedBar();
    }

    private void RefreshPinnedBar()
    {
        if (_pinnedMessages.Count == 0)
        {
            PinnedPreview = null;
            return;
        }

        if (_pinnedBarIndex >= _pinnedMessages.Count)
        {
            _pinnedBarIndex = 0;
        }

        var pinnedMessage = _pinnedMessages[_pinnedBarIndex].Message;
        var text = pinnedMessage?.Content?.Text ?? string.Empty;
        var preview = new PinnedPreviewViewModel(
            pinnedMessage?.Id ?? 0,
            _localization.GetString("Messenger_PinnedLabel"),
            string.IsNullOrWhiteSpace(text)
                ? _localization.GetString("Messenger_Attachment")
                : ChatItemViewModel.CreatePreview(text, 80),
            $"{_pinnedBarIndex + 1}/{_pinnedMessages.Count}",
            _pinnedMessages.Count > 1);
        PinnedPreview = preview;

        if (pinnedMessage is { SenderId: not 0 })
        {
            _ = ResolvePinnedAuthorAsync(preview, pinnedMessage.SenderId);
        }
    }

    private async Task ResolvePinnedAuthorAsync(PinnedPreviewViewModel preview, long senderId)
    {
        var (error, data) = await _messenger.GetUserDataAsync(senderId);
        if (!error.IsSuccess || data is null || PinnedPreview != preview)
        {
            return;
        }

        var name = string.Join(' ', new[] { data.FirstName, data.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (!string.IsNullOrWhiteSpace(name))
        {
            preview.Author = name;
        }
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
            RequestScroll(MessageScrollTarget.Message, message.Forwarded.OriginalMessageId);
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
            ForwardTargets.Add(new ForwardTargetViewModel(this, chat.Id, chat.Title, chat.Initials));
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
                InsertMessageInOrder(await CreateMessageItemAsync(message, currentUserId.Value));
                ApplyReplyQuoteState();
                RequestScroll(MessageScrollTarget.Bottom);
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
        // Сервер сам снимает закреп с удалённого сообщения, локальный кеш чистим сразу.
        _pinnedMessages.RemoveAll(info => info.Message?.Id == message.Id);
        _pinnedBarIndex = 0;
        RefreshPinnedBar();
        if (EditingMessage == message)
        {
            CancelEdit();
        }

        if (ReplyTarget == message)
        {
            ReplyTarget = null;
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
        _pinnedBarIndex = 0;
        ApplyPinnedState();
        RefreshPinnedBar();
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
        _pinnedBarIndex = 0;
        RefreshPinnedBar();
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
/// Плашка закреплённого сообщения над лентой чата.
/// </summary>
public sealed partial class PinnedPreviewViewModel(long messageId, string author, string preview, string counter, bool hasCounter) : ObservableObject
{
    public long MessageId { get; } = messageId;
    public string Preview { get; } = preview;
    public string Counter { get; } = counter;
    public bool HasCounter { get; } = hasCounter;

    /// <summary>Подпись плашки: сначала общая, затем имя автора оригинала, когда оно подгрузится.</summary>
    [ObservableProperty] private string _author = author;
}

/// <summary>
/// Чат в списке получателей пересылки.
/// </summary>
public sealed partial class ForwardTargetViewModel(MessengerViewModel owner, string chatId, string title, string initials) : ObservableObject
{
    /// <summary>Команда переключения живёт на владельце: в WinUI нет <c>RelativeSource AncestorType</c>.</summary>
    public MessengerViewModel Owner { get; } = owner;
    public string ChatId { get; } = chatId;
    public string Title { get; } = title;
    public string Initials { get; } = initials;

    [ObservableProperty] private bool _isSelected;
}
