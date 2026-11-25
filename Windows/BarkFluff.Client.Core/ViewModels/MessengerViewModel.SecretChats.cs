using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.Services.SecretChats;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly SqliteApplicationDataStore? _secretStore;
    private readonly IClientSession? _secretClientSession;
    private readonly WebApiClient? _secretApi;
    public SecretChatsViewModel SecretChats { get; }

    private bool _secretSessionStarted;
    private long _secretPreferenceVersion;

    public Task RefreshSecretPreferenceAsync() => _realtimeSession is { } session
        ? StartSecretChatsAsync(session)
        : Task.CompletedTask;

    private void StartSecretChats(RealtimeSession session) => _ = StartSecretChatsAsync(session);

    private async Task StartSecretChatsAsync(RealtimeSession session)
    {
        if (IsOffline || _secretStore is null || _secretApi is not { } api ||
            _secretClientSession?.CurrentConnection is not { } connection)
            return;
        var version = ++_secretPreferenceVersion;
        var token = session.Lifetime.Token;
        bool enabled;
        try
        {
            enabled = _stickerPreferences is not null &&
                (await _stickerPreferences.GetTestingPreferencesAsync(token)).SecretChatsEnabled;
        }
        catch (Exception) { return; }
        if (!IsCurrent(session) || version != _secretPreferenceVersion || token.IsCancellationRequested ||
            !ReferenceEquals(_secretClientSession.CurrentConnection, connection))
            return;
        if (!enabled)
        {
            if (_secretSessionStarted) StopSecretChats();
            return;
        }

        var parameters = connection.ConnectionParameters;
        if (_secretSessionStarted || parameters.UserId != session.UserId)
            return;
        _secretSessionStarted = true;
        var service = SecretChatService.Open(_secretStore, api, _secretClientSession);
        var streams = new SecretChatStreams(
            ct => api.ReadSecretChatInvitesAsync(parameters, ct),
            ct => api.ReadSecretChatResolutionsAsync(parameters, ct),
            ct => api.ReadSecretMessagesAsync(parameters, ct));
        SecretChats.Start(service, streams, session.Lifetime.Token,
            chatId => _ = NotifySecretMessageAsync(session, chatId));
    }

    private void StopSecretChats()
    {
        _secretPreferenceVersion++;
        _secretSessionStarted = false;
        _ = SecretChats.Stop();
    }

    private void ClearSecretChatNotification()
    {
        if (_notificationMessengerVisible && SecretChats.IsOpen && SecretChats.SelectedChat is { } chat &&
            _realtimeSession is { } session && IsCurrent(session))
            _notifications?.ClearChat(new(session.Node, session.UserId, "secret:" + chat.Info.Id));
    }

    private async Task NotifySecretMessageAsync(RealtimeSession session, string chatId)
    {
        if (_notifications is null || _notificationSettings is null)
            return;
        try
        {
            await _notificationSettings.PendingLoad.WaitAsync(session.Lifetime.Token);
            if (IsCurrent(session) && MessageNotificationService.Allows(_notificationSettings.IsEnabled,
                muted: false, _notificationMessengerVisible && SecretChats.IsOpen &&
                SecretChats.SelectedChat?.Info.Id == chatId, _notificationWindowActive))
                _notifications.ShowSecret(new(session.Node, session.UserId, "secret:" + chatId));
        }
        catch (OperationCanceledException) { }
    }

    private async Task ClearSecretChatsAsync()
    {
        if (_secretStore is null || _secretClientSession?.CurrentConnection is not { } connection ||
            connection.ConnectionParameters.UserId <= 0)
            return;
        try
        {
            var scope = SqliteApplicationDataStore.SecretScope.Create(
                connection.Profile.BeaconAddress, connection.ConnectionParameters.UserId);
            await SecretChats.FlushDraftAsync();
            await Task.Run(() => _secretStore.ForgetSecretChatsAsync(scope));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Ошибка локальной очистки не продлевает отклонённую сессию.
        }
    }
}
