using System.Collections.ObjectModel;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.Models;
using BarkFluff.Proto.Files;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly OutgoingMessageQueue _outgoing;
    private Action? _unsubscribeOutgoing;
    private Task _outgoingReady = Task.CompletedTask;
    public ObservableCollection<OutgoingBubbleViewModel> PendingMessages { get; } = [];
    public bool IsOutgoingTailVisible => _feedIncludesTail;
    public string? OutgoingHistoryNotice => _feedIncludesTail || PendingMessages.Count == 0 ? null :
        string.Format(_localization.GetString(PendingMessages.Any(item => item.CanRetry)
            ? "Messenger_OutboxHistoryFailed" : "Messenger_OutboxHistoryPending"), PendingMessages.Count);

    private static OutgoingScope OutboxScope(RealtimeSession session) => OutgoingScope.Create(session.Node, session.UserId);

    private void StartOutgoing(RealtimeSession session)
    {
        var scope = OutboxScope(session);
        var token = session.Lifetime.Token;
        var refreshQueued = 0;
        void Changed()
        {
            if (Interlocked.Exchange(ref refreshQueued, 1) == 0)
                PostSession(session, _ =>
                {
                    Interlocked.Exchange(ref refreshQueued, 0);
                    RefreshOutgoing();
                });
        }
        void Delivered(OutgoingScope deliveredScope, MessageModel message)
        {
            if (deliveredScope == scope)
                OnMessageChanged(session, new(message.ChatId, false, message.MessageId, MessageUpdateKind.New, Message: message),
                    fromOutgoingDelivery: true);
        }

        _outgoing.Changed += Changed;
        _outgoing.Delivered += Delivered;
        _unsubscribeOutgoing = () =>
        {
            _outgoing.Changed -= Changed;
            _outgoing.Delivered -= Delivered;
        };

        _outgoingReady = StartAsync(_outgoingReady);

        async Task StartAsync(Task previous)
        {
            try
            {
                await previous;
                await _outgoing.StartAsync(scope, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { PostSession(session, _ => ReportOutgoingStorageFailure()); }
        }
    }

    private void EndOutgoing()
    {
        _unsubscribeOutgoing?.Invoke();
        _unsubscribeOutgoing = null;
        _outgoingReady = StopAsync(_outgoingReady);
        PendingMessages.Clear();
        OnPropertyChanged(nameof(OutgoingHistoryNotice));

        async Task StopAsync(Task previous)
        {
            await previous;
            await _outgoing.StopAsync();
        }
    }

    public Task<bool> ClearOutgoingForLogoutAsync()
    {
        var session = _realtimeSession;
        var scope = session is not null ? OutboxScope(session) :
            _messenger.CurrentUserId is { } userId ? OutgoingScope.Create(_messenger.CurrentNodeAddress, userId) :
            (OutgoingScope?)null;
        if (scope is null)
            return Task.FromResult(true);

        var cleanup = ClearAsync(scope.Value, _outgoingReady);
        _outgoingReady = cleanup;
        return cleanup;

        async Task<bool> ClearAsync(OutgoingScope ownedScope, Task previous)
        {
            try
            {
                await previous;
                await _outgoing.ClearAsync(ownedScope);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private async Task ConfirmOutgoingAsync(RealtimeSession session, string chatId, MessageModel message)
    {
        if (message.SenderId != session.UserId || !Guid.TryParse(message.ClientOperationId, out _))
            return;
        var token = session.Lifetime.Token;
        try
        {
            await _outgoingReady;
            if (IsCurrent(session))
                await _outgoing.ConfirmAsync(OutboxScope(session), chatId, message, token);
        }
        catch (Exception) { PostSession(session, _ => ReportOutgoingStorageFailure()); }
    }

    private void ReportOutgoingStorageFailure() =>
        ActionError = _localization.GetString("Messenger_OutboxStorageFailed");

    private void RefreshOutgoing()
    {
        if (_realtimeSession is not { } session || !IsCurrent(session) ||
            SelectedChat is not { IsPrivate: false } chat)
        {
            PendingMessages.Clear();
            OnPropertyChanged(nameof(OutgoingHistoryNotice));
            return;
        }

        var scope = OutboxScope(session);
        var snapshot = _outgoing.Snapshot(scope, chat.Id);
        var ids = snapshot.Select(status => status.Message.Id).ToHashSet();
        for (var i = PendingMessages.Count - 1; i >= 0; i--)
            if (!ids.Contains(PendingMessages[i].Id))
                PendingMessages.RemoveAt(i);
        foreach (var status in snapshot)
        {
            var bubble = PendingMessages.FirstOrDefault(item => item.Id == status.Message.Id);
            if (bubble is null)
            {
                bubble = new OutgoingBubbleViewModel(status.Message, _localization, async retry =>
                {
                    if (!IsCurrent(session))
                        return;
                    try
                    {
                        if (retry)
                            await _outgoing.RetryAsync(scope, status.Message.Id, session.Lifetime.Token);
                        else
                            await _outgoing.CancelAsync(scope, status.Message.Id, session.Lifetime.Token);
                    }
                    catch (Exception)
                    {
                        PostSession(session, _ => ReportOutgoingStorageFailure());
                    }
                });
                bubble.Update(status);
                PendingMessages.Add(bubble);
            }
            else
            {
                bubble.Update(status);
            }
        }
        OnPropertyChanged(nameof(OutgoingHistoryNotice));
    }

    private async Task<bool> EnqueueOutgoingAsync(RealtimeSession session, string chatId, string text,
        long replyId = 0, IReadOnlyList<long>? forwards = null, IReadOnlyList<string>? attachments = null,
        IReadOnlyList<(AttachmentSourceFile Source, UploadFileType Type)>? files = null, CancellationToken token = default)
    {
        var chat = SelectedChat;
        var version = _chatVersion;
        try
        {
            await _outgoingReady;
            if (!IsCurrent(session))
                return false;
            if (!token.CanBeCanceled)
                token = session.Lifetime.Token;
            await _outgoing.EnqueueAsync(OutboxScope(session), chatId, text, replyId, forwards, attachments, files ?? [], token);
            return IsCurrent(session);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !IsCurrent(session))
        {
            return false;
        }
        catch (Exception)
        {
            if (IsCurrent(session) && _chatVersion == version && SelectedChat == chat)
                ActionError = _localization.GetString("Messenger_OutboxStorageFailed");
            return false;
        }
    }

    [RelayCommand]
    private Task ShowOutgoingTailAsync()
    {
        if (!_feedIncludesTail)
            return ReloadHistoryTailAsync();
        RequestScroll(MessageScrollTarget.Bottom);
        return Task.CompletedTask;
    }
}

public sealed partial class OutgoingBubbleViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly Func<bool, Task> _change;

    internal OutgoingBubbleViewModel(OutgoingMessage message,
        ILocalizationService localization, Func<bool, Task> change)
    {
        Id = message.Id;
        Text = message.Text;
        Details = message.Files.Length > 0 ? string.Join(", ", message.Files.Select(file => file.Name))
            : message.Forwards.Length > 0 ? localization.GetString("Messenger_OutboxForward")
            : message.Attachments.Length > 0 ? localization.GetString("Messenger_OutboxSticker")
            : message.ReplyId != 0 ? localization.GetString("Messenger_OutboxReply") : "";
        _localization = localization;
        _change = change;
        var description = string.IsNullOrWhiteSpace(Text) ? Details : Text.Trim();
        var length = Math.Min(description.Length, 48);
        if (length > 0 && char.IsHighSurrogate(description[length - 1]))
            length--;
        var label = description[..length];
        RetryName = $"{localization.GetString("Messenger_OutboxRetry")}: {label}";
        CancelName = $"{localization.GetString("Messenger_Cancel")}: {label}";
    }

    internal Guid Id { get; }
    public string Text { get; }
    public string Details { get; }
    public string RetryName { get; }
    public string CancelName { get; }
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private bool _canRetry;

    internal void Update(OutgoingStatus status)
    {
        Status = _localization.GetString(status.State switch
        {
            OutgoingState.Uploading => "Messenger_AttachmentUploading",
            OutgoingState.Sending => "Messenger_OutboxSending",
            OutgoingState.Failed => status.Message.Error ?? "Messenger_OutboxRejected",
            _ => "Messenger_OutboxWaiting"
        });
        Progress = status.Progress * 100;
        IsUploading = status.State == OutgoingState.Uploading;
        CanRetry = status.State == OutgoingState.Failed;
    }

    [RelayCommand] private Task RetryAsync() => _change(true);
    [RelayCommand] private Task CancelAsync() => _change(false);
}

