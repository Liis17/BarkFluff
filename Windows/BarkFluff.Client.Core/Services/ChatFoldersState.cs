using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Models;
using BarkFluff.Proto.Users;

namespace BarkFluff.Client.Core.Services;

public interface IChatFoldersState
{
    IReadOnlyList<ChatFolderData> Folders { get; }
    CancellationToken Lifetime { get; }
    bool IsBusy { get; }
    bool IsSaving { get; }
    string? LoadError { get; }
    event EventHandler? Changed;

    void Start();
    void Reset();
    void Invalidate();
    Task RefreshAsync();
    Task<string?> SetMembershipAsync(string folderId, string chatId, bool included);
    Task<string?> ApplySelectionAsync(string folderId, IReadOnlyDictionary<string, bool> changes);
    Task<string?> SaveAsync(Func<IUserPreferencesService, CancellationToken, Task<string?>> save);
}

/// <summary>Единое состояние папок. Все операции, кроме приёма сигнала стрима, начинаются в UI-потоке.</summary>
public sealed class ChatFoldersState(
    IUserPreferencesService preferences, IClientSession session, IUiDispatcher ui) : IChatFoldersState
{
    private sealed class Scope(NodeConnection connection)
    {
        public NodeConnection Connection { get; } = connection;
        public long? UserId { get; } = connection.ConnectionParameters.UserId;
        public CancellationTokenSource Cancellation { get; } = new();
        public IReadOnlyList<ChatFolderData> Folders { get; set; } = [];
        public string? LoadError { get; set; }
        public TaskCompletionSource? Reading { get; set; }
        public bool Pending { get; set; }
        public bool Saving { get; set; }
        public (string FolderId, string ChatId, bool Included)? MenuToggle { get; set; }

        public CancellationToken Lifetime => Cancellation.Token;
    }

    private Scope? _scope;
    private Scope? Current => _scope is { } scope && ReferenceEquals(scope.Connection, session.CurrentConnection) &&
        scope.UserId == scope.Connection.ConnectionParameters.UserId ? scope : null;

    public IReadOnlyList<ChatFolderData> Folders => Current?.Folders ?? [];
    public CancellationToken Lifetime => Current?.Lifetime ?? new CancellationToken(true);
    public bool IsBusy => Current is { } scope && (scope.Saving || scope.Reading is not null);
    public bool IsSaving => Current?.Saving == true;
    public string? LoadError => Current?.LoadError;
    public event EventHandler? Changed;

    public void Start()
    {
        if (Current is not null)
        {
            return;
        }
        Reset();
        if (session.CurrentConnection is { } connection)
        {
            _scope = new Scope(connection);
            _ = RefreshAsync();
        }
    }

    public void Reset()
    {
        if (_scope is not { } scope)
        {
            return;
        }

        _scope = null;
        scope.Cancellation.Cancel();
        scope.Cancellation.Dispose();
        Publish();
    }

    // Сигнал относится к сессии, в которой его получил стрим.
    public void Invalidate()
    {
        var scope = Volatile.Read(ref _scope);
        ui.Post(() =>
        {
            if (scope is not null && ReferenceEquals(scope, Current))
            {
                scope.Pending = true;
                _ = RefreshAsync();
            }
        });
    }

    public Task RefreshAsync()
    {
        if (Current is not { Saving: false } scope)
        {
            return Task.CompletedTask;
        }
        if (scope.Reading is { } active)
        {
            return active.Task;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Reading = completion;
        Publish();
        _ = ReadAsync(scope, completion);
        return completion.Task;
    }

    private async Task ReadAsync(Scope scope, TaskCompletionSource completion)
    {
        var token = scope.Lifetime;
        try
        {
            do
            {
                scope.Pending = false;
                var (error, folders) = await ReadSnapshotAsync(token);
                if (!ReferenceEquals(scope, Current))
                {
                    return;
                }
                if (scope.Saving)
                {
                    scope.Pending = true;
                    return;
                }
                if (!scope.Pending)
                {
                    scope.LoadError = folders is null ? error ?? "Error_SettingsLoadFailed" : null;
                    if (scope.LoadError is null && folders is not null)
                    {
                        scope.Folders = folders.OrderBy(folder => folder.SortOrder).ToArray();
                    }
                }
            }
            while (scope.Pending);
        }
        finally
        {
            scope.Reading = null;
            if (ReferenceEquals(scope, Current))
            {
                Publish();
            }
            completion.TrySetResult();
        }
    }

    private async Task<(string? ErrorKey, IReadOnlyList<ChatFolderData>? Folders)> ReadSnapshotAsync(CancellationToken token)
    {
        try
        {
            return await preferences.GetChatFoldersAsync(token).WaitAsync(token);
        }
        catch (Exception)
        {
            return ("Error_SettingsLoadFailed", null);
        }
    }

    public Task<string?> SetMembershipAsync(string folderId, string chatId, bool included) =>
        ApplySelectionAsync(folderId, new Dictionary<string, bool> { [chatId] = included }, (folderId, chatId, included));

    public Task<string?> ApplySelectionAsync(string folderId, IReadOnlyDictionary<string, bool> changes) =>
        ApplySelectionAsync(folderId, changes, null);

    private Task<string?> ApplySelectionAsync(string folderId, IReadOnlyDictionary<string, bool> changes,
        (string FolderId, string ChatId, bool Included)? menuToggle) =>
        RunMutationAsync(async (scope, token) =>
        {
            var applied = false;
            foreach (var (chatId, included) in changes)
            {
                if (!ReferenceEquals(scope, Current))
                {
                    return null;
                }
                var before = scope.Folders.FirstOrDefault(folder => folder.FolderId == folderId);
                if (before is null)
                {
                    return "Error_SettingsSaveFailed";
                }
                var optimistic = before.Clone();
                if (included && !optimistic.ChatList.Contains(chatId))
                {
                    optimistic.ChatList.Add(chatId);
                }
                else if (!included)
                {
                    optimistic.ChatList.Remove(chatId);
                }

                ReplaceFolder(scope, optimistic);
                string? failure;
                try
                {
                    var (error, updated) = await preferences.SetChatFolderMembershipAsync(folderId, chatId, included, token)
                        .WaitAsync(token);
                    if (!ReferenceEquals(scope, Current))
                    {
                        return null;
                    }
                    if (error is null && updated?.FolderId == folderId)
                    {
                        ReplaceFolder(scope, updated);
                        applied = true;
                        continue;
                    }
                    failure = error ?? "Error_SettingsSaveFailed";
                }
                catch (Exception) when (applied)
                {
                    failure = "Error_SettingsSaveFailed";
                }
                finally
                {
                    if (ReferenceEquals(scope, Current) && scope.Folders.Contains(optimistic, ReferenceEqualityComparer.Instance))
                    {
                        ReplaceFolder(scope, before);
                    }
                }

                if (applied)
                {
                    scope.Pending = true;
                    return "Error_ChatFoldersSelectionPartiallySaved";
                }
                return failure;
            }
            return null;
        }, menuToggle);

    public Task<string?> SaveAsync(Func<IUserPreferencesService, CancellationToken, Task<string?>> save) =>
        RunMutationAsync(async (scope, token) =>
        {
            scope.Pending = true;
            return await save(preferences, token).WaitAsync(token);
        });

    private async Task<string?> RunMutationAsync(Func<Scope, CancellationToken, Task<string?>> mutate,
        (string FolderId, string ChatId, bool Included)? menuToggle = null)
    {
        if (Current is not { } scope)
        {
            return null;
        }
        if (scope.Saving)
        {
            return menuToggle is not null && scope.MenuToggle == menuToggle ? null : "Error_ChatFoldersSaveInProgress";
        }

        var token = scope.Lifetime;
        scope.Saving = true;
        scope.MenuToggle = menuToggle;
        // Снимок, начатый до записи, устарел даже если ответит после неё.
        scope.Pending |= scope.Reading is not null;
        Publish();
        string? error;
        try
        {
            error = await mutate(scope, token);
        }
        catch (Exception)
        {
            error = "Error_SettingsSaveFailed";
        }
        finally
        {
            scope.MenuToggle = null;
            scope.Saving = false;
            if (ReferenceEquals(scope, Current))
            {
                Publish();
                if (scope.Pending)
                {
                    _ = RefreshAsync();
                }
            }
        }

        return ReferenceEquals(scope, Current) ? error : null;
    }

    private void ReplaceFolder(Scope scope, ChatFolderData folder)
    {
        scope.Folders = scope.Folders.Select(item => item.FolderId == folder.FolderId ? folder : item).ToArray();
        Publish();
    }

    private void Publish() => Changed?.Invoke(this, EventArgs.Empty);
}
