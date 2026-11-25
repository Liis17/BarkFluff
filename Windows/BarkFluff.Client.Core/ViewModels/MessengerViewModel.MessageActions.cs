using BarkFluff.Client.Core.Markdown;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
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

    private IReadOnlyList<long> _forwardSourceMessageIds = [];
    private int _pinnedBarIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinnedMessages))]
    private PinnedPreviewViewModel? _pinnedPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsComposerHintVisible), nameof(ComposerHintTitle), nameof(ComposerHintPreview), nameof(CanAttach))]
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

    public string ComposerHintPreview => MarkdownText.Strip((EditingMessage ?? ReplyTarget)?.Text);

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

        var session = _realtimeSession;
        var version = _chatVersion;
        var revision = _pinnedRevision;
        var cacheGeneration = session is null ? null : HistoryCacheGeneration(session, chat);
        ActionError = null;
        if (message.IsPinned)
        {
            var unpinError = await _messenger.UnpinMessageAsync(chat.Id, message.Id);
            if (!IsOpen(session, chat, version))
            {
                return;
            }

            if (revision != _pinnedRevision)
            {
                session!.PinRefresh.RequestConfirmation();
                return;
            }

            if (!unpinError.IsSuccess)
            {
                ActionError = DescribeError(unpinError);
                return;
            }

            _pinnedRevision++;
            _pinnedMessages.RemoveAll(info => info.Message?.Id == message.Id);
            if (Messages.FirstOrDefault(item => item.Id == message.Id) is { } visible)
            {
                visible.IsPinned = false;
            }
            _pinnedBarIndex = 0;
            RefreshPinnedBar();
            SaveHistoryPins(session!, chat, cacheGeneration);
            return;
        }

        var (pinError, pinned) = await _messenger.PinMessageAsync(chat.Id, message.Id);
        if (!IsOpen(session, chat, version))
        {
            return;
        }

        if (revision != _pinnedRevision)
        {
            session!.PinRefresh.RequestConfirmation();
            return;
        }

        if (!pinError.IsSuccess || pinned is null)
        {
            ActionError = DescribeError(pinError);
            return;
        }

        _pinnedRevision++;
        // Новый закреп показываем первым — как это делает веб-клиент.
        _pinnedMessages.RemoveAll(info => info.Message?.Id == message.Id);
        _pinnedMessages.Insert(0, pinned);
        if (Messages.FirstOrDefault(item => item.Id == message.Id) is { } visiblePin)
        {
            visiblePin.IsPinned = true;
        }
        _pinnedBarIndex = 0;
        RefreshPinnedBar();
        SaveHistoryPins(session!, chat, cacheGeneration);
    }

    /// <summary>
    /// Клик по плашке: переход к текущему закрепу и перелистывание на следующий по кругу.
    /// </summary>
    [RelayCommand]
    private async Task CyclePinnedMessageAsync()
    {
        if (_pinnedMessages.Count == 0)
        {
            return;
        }

        var messageId = _pinnedMessages[_pinnedBarIndex].Message?.Id ?? 0;
        if (_pinnedMessages.Count > 1)
        {
            _pinnedBarIndex = (_pinnedBarIndex + 1) % _pinnedMessages.Count;
            RefreshPinnedBar();
        }

        await NavigateToMessageAsync(messageId);
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

        var session = _realtimeSession;
        var version = _chatVersion;
        var revision = _pinnedRevision;
        var error = await _messenger.UnpinMessageAsync(chat.Id, preview.MessageId);
        if (!IsOpen(session, chat, version))
        {
            return;
        }

        if (revision != _pinnedRevision)
        {
            session!.PinRefresh.RequestConfirmation();
            return;
        }
        if (!error.IsSuccess)
        {
            ActionError = DescribeError(error);
            return;
        }

        ApplyPinUpdate(session!, new(chat.Id, false, preview.MessageId, MessageUpdateKind.Unpin));
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
        if (PinnedPreview != preview)
        {
            return;
        }

        if (!error.IsSuccess || data is null)
        {
            ReportBackgroundError(error);
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
        if (!message.CanModify || Voice.IsBusy || AttachmentComposer.HasVoice)
        {
            return;
        }

        ActionError = null;
        BeginDraftEdit(message);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        RestoreDraftAfterEdit();
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
    private Task ScrollToOriginalAsync(MessageItemViewModel message) =>
        message.Reply is { } reply
            ? NavigateToMessageAsync(reply.IsDeleted ? 0 : reply.MessageId)
            : Task.CompletedTask;

    [RelayCommand]
    private void StartForwardMessage(MessageItemViewModel message)
    {
        if (!message.CanUseActions)
        {
            return;
        }

        ActionError = null;
        // Пересылаем оригиналы, а не саму пересылку — так же поступает веб-клиент.
        // Оригиналов может быть несколько: иначе пересылка пачки потеряла бы всё, кроме первого.
        var originalIds = message.Forwards
            .Where(forwarded => forwarded.OriginalMessageId > 0)
            .Select(forwarded => forwarded.OriginalMessageId)
            .ToArray();
        _forwardSourceMessageIds = originalIds.Length > 0 ? originalIds : [message.Id];

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
        var session = _realtimeSession;
        var targetChatIds = ForwardTargets.Where(target => target.IsSelected).Select(target => target.ChatId).ToArray();
        var sourceIds = _forwardSourceMessageIds;
        if (!IsCurrent(session) || targetChatIds.Length == 0 || sourceIds.Count == 0)
        {
            return;
        }

        var comment = ForwardComment.Trim();
        var chat = SelectedChat;
        var version = _chatVersion;
        foreach (var chatId in targetChatIds)
        {
            if (!await EnqueueOutgoingAsync(session!, chatId, comment, forwards: sourceIds))
                return;
            if (!IsCurrent(session))
                return;
            var target = ForwardTargets.FirstOrDefault(item => item.ChatId == chatId);
            if (target is not null)
                target.IsSelected = false;
        }

        CancelForward();
        if (chat is not null && IsOpen(session, chat, version) && targetChatIds.Contains(chat.Id))
            await ShowOutgoingTailAsync();
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
        var chat = SelectedChat;
        var session = _realtimeSession;
        var version = _chatVersion;
        if (message is null || chat is null)
        {
            return;
        }

        MessagePendingDelete = null;
        var error = await _messenger.DeleteMessageAsync(message.Id);
        if (!IsCurrent(session))
        {
            return;
        }

        if (!error.IsSuccess)
        {
            if (IsOpen(session, chat, version))
            {
                ActionError = DescribeError(error);
            }
            return;
        }

        await ApplyMessageUpdateAsync(session!, new(chat.Id, false, message.Id, MessageUpdateKind.Delete));
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

        var session = _realtimeSession;
        var version = _chatVersion;
        var draft = DraftText;
        var (error, edited) = await _messenger.EditMessageAsync(chat.Id, message.Id, draft.Trim());
        if (!IsOpen(session, chat, version))
        {
            return;
        }
        if (!error.IsSuccess || edited is null)
        {
            // Черновик остаётся в композере, чтобы пользователь мог повторить правку.
            ActionError = DescribeError(error);
            return;
        }

        if (EditingMessage == message && DraftText == draft)
        {
            RestoreDraftAfterEdit();
        }

        await ApplyMessageUpdateAsync(session!, new(chat.Id, false, message.Id, MessageUpdateKind.Edit, Message: edited));
    }

    private async Task<bool> RefreshPinsAsync(ChatItemViewModel chat, int loadVersion, bool reportError)
    {
        var session = _realtimeSession;
        var revision = _pinnedRevision;
        if (!IsOpen(session, chat, loadVersion))
        {
            return true;
        }

        var token = session!.Lifetime.Token;
        var cacheGeneration = HistoryCacheGeneration(session, chat);
        var (error, pinned) = await _messenger.GetPinnedMessagesAsync(chat.Id, token).WaitAsync(token);
        // Закрепы относятся ко всему чату и переживают замену окна истории.
        if (!IsOpen(session, chat, loadVersion))
        {
            return true;
        }

        if (!error.IsSuccess || pinned is null)
        {
            if (reportError)
            {
                ReportBackgroundError(error);
            }
            return false;
        }

        if (revision != _pinnedRevision)
        {
            session!.PinRefresh.RequestConfirmation();
            return true;
        }

        var next = pinned
            .Where(pin => pin.Message is not null &&
                session!.Journal.Find(chat.Id, false, pin.Message.Id)?.Kind != MessageUpdateKind.Delete)
            .OrderByDescending(pin => pin.PinnedAt)
            .ToArray();
        if (!_pinnedMessages.SequenceEqual(next))
        {
            var shown = PinnedPreview?.MessageId;
            _pinnedMessages.Clear();
            _pinnedMessages.AddRange(next);
            _pinnedBarIndex = Math.Max(0, _pinnedMessages.FindIndex(pin => pin.Message.Id == shown));
            RefreshPinnedBar();
        }
        ApplyPinnedState();
        SaveHistoryPins(session, chat, cacheGeneration);
        return true;
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
        error.ErrorResourceKey is { Length: > 0 } resourceKey
            ? _localization.GetString(resourceKey)
            : string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? _localization.GetString("Messenger_ActionFailed")
                : error.ErrorMessage;

    /// <summary>
    /// Фоновая операция сообщает об ошибке, только если баннер пуст: она не перетирает
    /// сообщение о только что сделанном действии, а серия сетевых сбоев подряд ставит
    /// текст один раз вместо того, чтобы мигать им.
    /// </summary>
    private void ReportBackgroundError(ErrorReturner error) => ActionError ??= DescribeError(error);

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
