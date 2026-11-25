using System.Collections.ObjectModel;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Services.SecretChats;
using BarkFluff.Proto.Updates;
using BarkFluff.Proto.Users;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed record SecretChatRow(SecretChatInfo Info, string Title);
public sealed record SecretInviteRow(SecretChatInviteEvent Invite, string Title)
{
    public string DevicePrefix => Invite.SenderDeviceId[..Math.Min(8, Invite.SenderDeviceId.Length)];
}
public sealed record SecretMessageRow(string Id, string Text, string Caption, SecretDeliveryState? Delivery)
{
    public bool CanDelete => Delivery == SecretDeliveryState.Failed;
}

public sealed partial class SecretChatsViewModel(IUiDispatcher dispatcher, ILocalizationService localization,
    TimeProvider? clock = null) : ObservableObject
{
    private SecretChatService? _service;
    private CancellationTokenSource? _lifetime;
    private Task _streamRun = Task.CompletedTask;
    private Task _selectionLoad = Task.CompletedTask;
    private Task _identityRefresh = Task.CompletedTask;
    private int _selection;
    private long _draftVersion;
    private long _refreshVersion;
    private bool _loadingDraft;
    private CancellationTokenSource? _draftDelay;
    private (SecretChatService Service, string ChatId, string Text)? _pendingDraft;
    private Task _draftWrite = Task.CompletedTask;
    private SecretChatMessage? _sending;
    private readonly Dictionary<long, string> _names = [];

    public ObservableCollection<SecretChatRow> Chats { get; } = [];
    public ObservableCollection<SecretInviteRow> Invites { get; } = [];

    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMainMessengerVisible))]
    private bool _isOpen;
    [ObservableProperty] private IReadOnlyList<SecretMessageRow> _messages = [];
    [ObservableProperty] private SecretChatRow? _selectedChat;
    [ObservableProperty] private string _draft = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    public bool CanSend => IsAvailable && !IsBusy && SelectedChat?.Info.Status == SecretChatStatus.Accepted;
    public int SessionVersion { get; private set; }
    public bool IsMainMessengerVisible => !IsOpen;
    public bool HasIncomingInvites => Invites.Count > 0;
    public bool HasChat => SelectedChat is not null;

    public void Open() { if (IsAvailable) IsOpen = true; }
    public void Close() { IsOpen = false; SelectedChat = null; }

    internal void Start(SecretChatService service, SecretChatStreams streams, CancellationToken sessionToken,
        Action<string> incoming)
    {
        _ = Stop();
        _service = service;
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        _lifetime = lifetime;
        var token = lifetime.Token;
        service.IncomingMessageStored += OnIncoming;
        service.PeerIdentityChanged += OnIdentityChanged;
        _streamRun = Task.Run(async () =>
        {
            try
            {
                await streams.RunAsync(service, available => Post(() =>
                {
                    IsAvailable = available;
                    if (!available)
                    {
                        Close();
                        Chats.Clear();
                        Invites.Clear();
                        OnPropertyChanged(nameof(HasIncomingInvites));
                    }
                }), invite => Post(() =>
                {
                    if (IsAvailable && !Invites.Any(row => row.Invite.InviteId == invite.InviteId))
                    {
                        Invites.Add(new(invite, PeerName(invite.SenderUserId)));
                        OnPropertyChanged(nameof(HasIncomingInvites));
                    }
                }), () => DispatchAsync(() => RefreshAsync(service, token)), token);
            }
            finally
            {
                service.IncomingMessageStored -= OnIncoming;
                service.PeerIdentityChanged -= OnIdentityChanged;
            }
        }, CancellationToken.None);

        void Post(Action action) => dispatcher.Post(() =>
        {
            if (ReferenceEquals(_lifetime, lifetime) && !token.IsCancellationRequested)
                action();
        });
        void OnIncoming(string chatId) => Post(() => incoming(chatId));
        void OnIdentityChanged(SecretChatInfo info) => Post(() =>
        {
            if (SelectedChat is { } row && row.Info.Id == info.Id)
                SelectedChat = row with { Info = info };
            _identityRefresh = RefreshIgnoringErrorsAsync(service, token);
        });
    }

    internal Task Stop()
    {
        var flushed = FlushDraftAsync();
        SessionVersion++;
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _service = null;
        _sending = null;
        IsAvailable = false;
        Close();
        Chats.Clear();
        Invites.Clear();
        OnPropertyChanged(nameof(HasIncomingInvites));
        _names.Clear();
        IsBusy = false;
        Error = "";
        return Task.WhenAll(flushed, _streamRun, _selectionLoad, _identityRefresh);
    }

    internal bool OpenChat(string id)
    {
        if (!IsAvailable || Chats.FirstOrDefault(row => row.Info.Id == id) is not { } chat)
            return false;
        Open();
        SelectedChat = chat;
        return true;
    }

    public async Task<IReadOnlyList<PeerDeviceInfo>> LoadDevicesAsync(long userId)
    {
        if (!IsAvailable || _service is not { } service || _lifetime is not { } lifetime)
            return [];
        var token = lifetime.Token;
        try
        {
            var devices = await service.ListPeerDevicesAsync(userId, token);
            return IsCurrent(service, token) ? devices : [];
        }
        catch (Exception)
        {
            if (IsCurrent(service, token)) Error = T("Secret_Error");
            return [];
        }
    }

    public Task CreateAsync(long userId, string name, string deviceId, string text) =>
        RunAsync(async (service, token) =>
        {
            _names[userId] = name;
            var selection = _selection;
            var chat = await service.CreateChatAsync(userId, deviceId, text, token);
            await RefreshAsync(service, token);
            if (IsCurrent(service, token) && selection == _selection)
                OpenChat(chat.Id);
        });

    [RelayCommand]
    private Task AcceptAsync(SecretInviteRow row) => RunAsync(async (service, token) =>
    {
        var selection = _selection;
        try
        {
            var chat = await service.AcceptInviteAsync(row.Invite, token);
            await RefreshAsync(service, token);
            if (IsCurrent(service, token) && selection == _selection)
                OpenChat(chat.Id);
        }
        finally
        {
            await RemoveDecidedInviteAsync(row, service, token);
        }
    });

    [RelayCommand]
    private Task RejectAsync(SecretInviteRow row) => RunAsync(async (service, token) =>
    {
        try { await service.RejectInviteAsync(row.Invite.InviteId, token); }
        finally
        {
            await RemoveDecidedInviteAsync(row, service, token);
        }
    });

    private async Task RemoveDecidedInviteAsync(SecretInviteRow row, SecretChatService service, CancellationToken token)
    {
        if (IsCurrent(service, token) && !await service.ShouldOfferInviteAsync(row.Invite, token) && IsCurrent(service, token))
        {
            Invites.Remove(row);
            OnPropertyChanged(nameof(HasIncomingInvites));
        }
    }

    [RelayCommand]
    private Task SendAsync()
    {
        if (!CanSend || SelectedChat is not { } chat || string.IsNullOrWhiteSpace(Draft))
            return Task.CompletedTask;
        var text = Draft;
        var version = _draftVersion;
        var id = "out:" + Guid.NewGuid().ToString("N");
        return RunAsync(async (service, token) =>
        {
            _sending = new(id, chat.Info.Id, 0, "", DateTimeOffset.UtcNow, text, Outgoing: true);
            Messages = Messages.Append(ToRow(_sending, SecretDeliveryState.Sending)).ToArray();
            try
            {
                await service.SendMessageAsync(chat.Info.Id, text, id, token);
            }
            finally
            {
                if (IsCurrent(service, token))
                {
                    _sending = null;
                    var history = await service.ReadHistoryAsync(chat.Info.Id, token);
                    if (IsCurrent(service, token) && SelectedChat?.Info.Id == chat.Info.Id &&
                        _draftVersion == version && history.Any(message => message.Id == id))
                        Draft = "";
                }
            }
        });
    }

    [RelayCommand]
    private Task DeleteFailedAsync(SecretMessageRow row) => SelectedChat is { } chat && row.CanDelete
        ? RunAsync((service, token) => service.DeleteFailedMessageAsync(chat.Info.Id, row.Id, token))
        : Task.CompletedTask;

    public Task ForgetAsync(SecretChatRow row) =>
        RunAsync((service, token) => service.ForgetChatAsync(row.Info.Id, token));

    partial void OnSelectedChatChanged(SecretChatRow? oldValue, SecretChatRow? newValue)
    {
        UpdateStatus();
        OnPropertyChanged(nameof(HasChat));
        if (oldValue?.Info.Id == newValue?.Info.Id)
            return;
        _ = FlushDraftAsync();
        var selection = ++_selection;
        Messages = [];
        SetLoadedDraft("");
        Error = "";
        if (newValue is not null && _service is { } service && _lifetime is { } lifetime)
            _selectionLoad = LoadSelectionAsync(service, newValue.Info.Id, selection, _draftVersion, lifetime.Token);
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSend));
    partial void OnIsAvailableChanged(bool value) => OnPropertyChanged(nameof(CanSend));

    partial void OnDraftChanged(string value)
    {
        if (_loadingDraft || SelectedChat is not { } chat || _service is not { } service || _lifetime is not { } lifetime)
            return;
        _draftVersion++;
        _pendingDraft = (service, chat.Info.Id, value);
        _draftDelay?.Cancel();
        var delay = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _draftDelay = delay;
        _ = DebounceDraftAsync(delay);
    }

    private async Task DebounceDraftAsync(CancellationTokenSource delay)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), clock ?? TimeProvider.System, delay.Token);
            await DispatchAsync(() => ReferenceEquals(_draftDelay, delay) ? FlushDraftAsync() : Task.CompletedTask);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await DispatchAsync(() =>
            {
                if (ReferenceEquals(_draftDelay, delay))
                    _draftDelay = null;
                delay.Dispose();
                return Task.CompletedTask;
            });
        }
    }

    internal Task FlushDraftAsync()
    {
        var delay = _draftDelay;
        _draftDelay = null;
        delay?.Cancel();
        if (_pendingDraft is { } draft)
        {
            _pendingDraft = null;
            _draftWrite = SaveDraftAsync(_draftWrite, draft.Service, draft.ChatId, draft.Text);
        }
        return _draftWrite;
    }

    private static async Task SaveDraftAsync(Task previous, SecretChatService service, string id, string text)
    {
        try
        {
            await previous;
            await service.WriteDraftAsync(id, text, CancellationToken.None);
        }
        catch (Exception)
        {
            // Сбой автосохранения не прерывает текущую операцию и следующие записи.
        }
    }

    private async Task LoadSelectionAsync(SecretChatService service, string id, int selection, long version, CancellationToken token)
    {
        try
        {
            await _draftWrite;
            var draft = await service.ReadDraftAsync(id, token);
            if (IsCurrent(service, token) && selection == _selection && version == _draftVersion)
                SetLoadedDraft(draft);
            await RefreshAsync(service, token);
        }
        catch (Exception)
        {
            if (IsCurrent(service, token) && selection == _selection) Error = T("Secret_Error");
        }
    }

    private void SetLoadedDraft(string value)
    {
        _loadingDraft = true;
        Draft = value;
        _loadingDraft = false;
    }

    private async Task RefreshIgnoringErrorsAsync(SecretChatService service, CancellationToken token)
    {
        try { await RefreshAsync(service, token); }
        catch (Exception)
        {
            if (IsCurrent(service, token)) Error = T("Secret_Error");
        }
    }

    private async Task RefreshAsync(SecretChatService service, CancellationToken token)
    {
        if (!IsCurrent(service, token) || !IsAvailable)
            return;
        var revision = ++_refreshVersion;
        var infos = await service.ListChatsAsync(token);
        if (!IsCurrent(service, token) || revision != _refreshVersion)
            return;
        var selected = SelectedChat?.Info.Id;
        var rows = infos.Select(info => new SecretChatRow(info, PeerName(info.PeerUserId))).ToArray();
        foreach (var row in rows)
        {
            var old = Chats.FirstOrDefault(item => item.Info.Id == row.Info.Id);
            if (old is null)
                Chats.Add(row);
            else if (old != row)
                Chats[Chats.IndexOf(old)] = row;
        }
        foreach (var old in Chats.Where(item => rows.All(row => row.Info.Id != item.Info.Id)).ToArray())
            Chats.Remove(old);
        SelectedChat = Chats.FirstOrDefault(item => item.Info.Id == selected);
        if (SelectedChat is not { } current)
            return;
        var selection = _selection;
        var history = await service.ReadHistoryAsync(current.Info.Id, token);
        if (!IsCurrent(service, token) || revision != _refreshVersion || selection != _selection)
            return;
        var feed = history.Where(message => message.Id != _sending?.Id)
            .Select(message => ToRow(message, !message.Outgoing ? null :
                message.ServerId is null ? SecretDeliveryState.Failed : SecretDeliveryState.Sent)).ToList();
        if (_sending is { } sending && sending.ChatId == current.Info.Id)
            feed.Add(ToRow(sending, SecretDeliveryState.Sending));
        if (!Messages.SequenceEqual(feed))
            Messages = feed;
    }

    private async Task RunAsync(Func<SecretChatService, CancellationToken, Task> operation)
    {
        if (!IsAvailable || IsBusy || _service is not { } service || _lifetime is not { } lifetime)
            return;
        var token = lifetime.Token;
        var selection = _selection;
        Error = "";
        IsBusy = true;
        try { await operation(service, token); }
        catch (Exception)
        {
            if (IsCurrent(service, token) && selection == _selection) Error = T("Secret_Error");
        }
        finally
        {
            if (IsCurrent(service, token))
            {
                try { await RefreshAsync(service, token); }
                catch (Exception)
                {
                    if (IsCurrent(service, token) && selection == _selection) Error = T("Secret_Error");
                }
                if (IsCurrent(service, token)) IsBusy = false;
            }
        }
    }

    private Task DispatchAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Post(async () =>
        {
            try { await action(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return completion.Task;
    }

    private void UpdateStatus()
    {
        Status = SelectedChat?.Info.Status switch
        {
            SecretChatStatus.Pending => T("Secret_Pending"),
            SecretChatStatus.KeyChanged => T("Secret_KeyChanged"),
            _ => ""
        };
        OnPropertyChanged(nameof(CanSend));
    }

    private SecretMessageRow ToRow(SecretChatMessage message, SecretDeliveryState? state)
    {
        var caption = state switch
        {
            SecretDeliveryState.Sending => T("Secret_Sending"),
            SecretDeliveryState.Sent => T("Secret_Sent"),
            SecretDeliveryState.Failed => T("Secret_Failed"),
            _ => T("Secret_Received")
        };
        return new(message.Id, message.Text, $"{caption} · {message.SentAt.ToLocalTime():t}", state);
    }

    private bool IsCurrent(SecretChatService service, CancellationToken token) =>
        !token.IsCancellationRequested && ReferenceEquals(service, _service);
    private string PeerName(long userId) => _names.GetValueOrDefault(userId) ?? $"{T("Secret_User")} {userId}";
    private string T(string key) => localization.GetString(key);
}
