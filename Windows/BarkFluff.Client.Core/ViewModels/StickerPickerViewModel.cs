using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Files;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace BarkFluff.Client.Core.ViewModels;

public sealed record PickerSticker(string PackId, string Id, string FileId, string Emoji, string PreviewUrl);

public sealed partial class StickerTab(string name, string? packId = null, string? coverId = null) : ObservableObject
{
    public string Name { get; } = name;
    public string? PackId { get; } = packId;
    internal string? CoverId { get; } = coverId;
    [ObservableProperty] private string _coverUrl = "";
}

public sealed partial class StickerPickerViewModel(
    IStickerCatalog catalog, IApplicationDataStore store, ILocalizationService localization, IUiDispatcher dispatcher)
    : ObservableObject
{
    private sealed record RecentSticker(string PackId, string Id);

    private sealed class Scope(string node, long user, CancellationToken token, Func<bool> isCurrent, StickerTab recentTab)
    {
        public string Node { get; } = node;
        public long User { get; } = user;
        public CancellationToken Token { get; } = token;
        public Func<bool> IsCurrent { get; } = isCurrent;
        public StickerTab RecentTab { get; } = recentTab;
        public string RecentKey { get; } = $"stickers.recent/{user.ToString(CultureInfo.InvariantCulture)}/{node}";
        public Task<IReadOnlyList<StickerTab>?>? Tabs { get; set; }
        public SemaphoreSlim PackSlots { get; } = new(3);
        public Dictionary<string, Task<IReadOnlyList<PickerSticker>?>> Packs { get; } = [];
        public Task<IReadOnlyList<RecentSticker>>? Recents { get; set; }
    }

    private Scope? _scope;
    private Func<bool>? _isOpen;
    private int _request;
    private IReadOnlyList<PickerSticker> _results = [];

    [ObservableProperty] private IReadOnlyList<StickerTab> _tabs = [];
    [ObservableProperty] private StickerTab? _selectedTab;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private IReadOnlyList<PickerSticker> _stickers = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isSending;

    internal void StartSession(string node, long user, CancellationToken token, Func<bool> isCurrent)
    {
        _scope = new(node, user, token, isCurrent, new(localization.GetString("Messenger_StickerRecent")));
    }

    internal void Reset()
    {
        _scope = null;
        Close();
        Tabs = [];
        SelectedTab = null;
        IsSending = false;
    }

    internal Task OpenAsync(Func<bool> isOpen)
    {
        if (_scope is not { } scope || !scope.IsCurrent() || !isOpen())
            return Task.CompletedTask;

        _isOpen = isOpen;
        Search = "";
        return LoadAsync();
    }

    internal void Close()
    {
        _isOpen = null;
        _request++;
        _results = [];
        RefreshResults();
        IsLoading = HasError = IsEmpty = false;
    }

    partial void OnSelectedTabChanged(StickerTab? value)
    {
        if (value is not null && !IsLoadingTabs)
            _ = LoadAsync();
    }

    partial void OnSearchChanged(string value) => RefreshResults();

    private bool IsLoadingTabs { get; set; }

    public async Task LoadAsync()
    {
        if (_scope is not { } scope || !scope.IsCurrent() || _isOpen?.Invoke() != true)
            return;

        var request = ++_request;
        bool IsCurrent() => request == _request && scope.IsCurrent() && _isOpen?.Invoke() == true;
        IsLoading = true;
        HasError = IsEmpty = false;
        _results = [];
        RefreshResults();
        IReadOnlyList<PickerSticker>? result = null;
        try
        {
            var tabsTask = scope.Tabs;
            if (tabsTask is null || tabsTask.IsCompleted && !HasResult(tabsTask))
                scope.Tabs = tabsTask = LoadTabsAsync(scope);

            var tabs = await tabsTask;
            if (tabs is null || !IsCurrent())
                return;

            var tab = SelectedTab ?? scope.RecentTab;
            dispatcher.Post(() =>
            {
                if (!IsCurrent() || ReferenceEquals(Tabs, tabs))
                    return;
                IsLoadingTabs = true;
                Tabs = tabs;
                SelectedTab = tab;
                IsLoadingTabs = false;
            });

            result = tab.PackId is { } packId
                ? await GetPackAsync(scope, packId)
                : await LoadRecentAsync(scope, tabs, IsCurrent);
        }
        catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
        catch (Exception)
        {
            // Список, пак или хранилище могут быть временно недоступны.
        }
        finally
        {
            dispatcher.Post(() =>
            {
                if (!IsCurrent())
                    return;
                _results = result ?? [];
                IsLoading = false;
                HasError = result is null;
                RefreshResults();
            });
        }
    }

    public async Task LoadCoverAsync(StickerTab tab)
    {
        if (_scope is not { } scope || !scope.IsCurrent() || _isOpen?.Invoke() != true ||
            tab.PackId is not { } packId || string.IsNullOrEmpty(tab.CoverId) || tab.CoverUrl.Length > 0 || !Tabs.Contains(tab))
            return;

        try
        {
            var stickers = await GetPackAsync(scope, packId);
            var url = stickers?.FirstOrDefault(sticker => sticker.Id == tab.CoverId)?.PreviewUrl;
            if (url is not null)
                dispatcher.Post(() =>
                {
                    if (ReferenceEquals(_scope, scope) && scope.IsCurrent())
                        tab.CoverUrl = url;
                });
        }
        catch (Exception)
        {
            // Название пака остаётся доступным, ошибка загрузки содержимого видна на его вкладке.
        }
    }

    public bool CanChoose(PickerSticker sticker) =>
        !IsSending && _isOpen?.Invoke() == true && Stickers.Any(item => ReferenceEquals(item, sticker));

    internal async Task RememberAsync(PickerSticker sticker)
    {
        if (_scope is not { } scope || !scope.IsCurrent())
            return;

        try
        {
            var previous = await GetRecentsAsync(scope);
            if (!scope.IsCurrent())
                return;

            var recent = new RecentSticker(sticker.PackId, sticker.Id);
            RecentSticker[] updated = [recent, .. previous.Where(item => item.Id != sticker.Id).Take(31)];
            scope.Recents = Task.FromResult<IReadOnlyList<RecentSticker>>(updated);
            await store.SavePreferenceAsync(scope.RecentKey, JsonSerializer.Serialize(updated), scope.Token);
        }
        catch (Exception)
        {
            // Ошибка локального хранилища не отменяет отправленное сообщение.
        }
    }

    private void RefreshResults()
    {
        var query = Search.Trim();
        Stickers = query.Length == 0 ? _results : _results.Where(item => item.Emoji.Contains(query, StringComparison.Ordinal)).ToArray();
        IsEmpty = _isOpen?.Invoke() == true && !IsLoading && !HasError && Stickers.Count == 0;
    }

    private async Task<IReadOnlyList<StickerTab>?> LoadTabsAsync(Scope scope)
    {
        var packs = await catalog.ListAsync(scope.Node, scope.User, scope.Token);
        scope.Token.ThrowIfCancellationRequested();
        return packs is null ? null : [scope.RecentTab, .. packs.Select(pack => new StickerTab(pack.Name, pack.Id, pack.CoverStickerId))];
    }

    private Task<IReadOnlyList<PickerSticker>?> GetPackAsync(Scope scope, string packId)
    {
        lock (scope.Packs)
        {
            if (!scope.Packs.TryGetValue(packId, out var request) || request.IsCompleted && !HasResult(request))
                scope.Packs[packId] = request = LoadPackAsync(scope, packId);
            return request;
        }
    }

    private static bool HasResult<T>(Task<T?> task) where T : class => task.IsCompletedSuccessfully && task.Result is not null;

    private async Task<IReadOnlyList<PickerSticker>?> LoadPackAsync(Scope scope, string packId)
    {
        await scope.PackSlots.WaitAsync(scope.Token);
        try
        {
            // Вызов фасада без отмены может завершиться после выхода. Очередь новых запросов отменяется.
            var stickers = await catalog.LoadPackAsync(scope.Node, scope.User, packId, scope.Token);
            scope.Token.ThrowIfCancellationRequested();
            return stickers?.Where(item => item.FileId.Length > 0).Select(item =>
                new PickerSticker(packId, item.Id, item.FileId, item.Emoji,
                    item.PreviewUrl.Length > 0 ? item.PreviewUrl : item.FileUrl)).ToArray();
        }
        finally { scope.PackSlots.Release(); }
    }

    private async Task<IReadOnlyList<PickerSticker>?> LoadRecentAsync(Scope scope, IReadOnlyList<StickerTab> tabs, Func<bool> isCurrent)
    {
        var recent = await GetRecentsAsync(scope);
        if (!isCurrent())
            return null;

        var listed = tabs.Select(tab => tab.PackId).ToHashSet();
        var ids = recent.Select(item => item.PackId).Distinct().Where(listed.Contains);
        var contents = await Task.WhenAll(ids.Select(id => GetPackAsync(scope, id)));
        if (contents.Any(pack => pack is null))
            return null;

        var byId = contents.SelectMany(pack => pack!).ToLookup(sticker => (sticker.PackId, sticker.Id));
        return recent.Select(item => byId[(item.PackId, item.Id)].FirstOrDefault()).OfType<PickerSticker>().ToArray();
    }

    private Task<IReadOnlyList<RecentSticker>> GetRecentsAsync(Scope scope)
    {
        if (scope.Recents is null || scope.Recents.IsFaulted)
            scope.Recents = ReadRecentsAsync(scope);
        return scope.Recents;
    }

    private async Task<IReadOnlyList<RecentSticker>> ReadRecentsAsync(Scope scope)
    {
        var json = await store.GetPreferenceAsync(scope.RecentKey, scope.Token);
        if (json is null)
            return [];
        try { return JsonSerializer.Deserialize<RecentSticker[]>(json)?.Take(32).ToArray() ?? []; }
        catch (JsonException) { return []; }
    }
}
