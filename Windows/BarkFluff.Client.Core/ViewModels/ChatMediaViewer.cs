using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels;

public sealed record ChatMediaItem(long AttachmentId, string FileId, string FileName, bool IsVideo);

public sealed partial class ChatMediaViewer : ObservableObject
{
    private const int PageSize = 50;
    private const int MaxPageLoads = 60;
    private static readonly HttpClient Downloads = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly IMessengerService _messenger;
    private readonly HttpClient _downloads;
    private readonly TimeSpan _idleTimeout;
    private readonly AttachmentPages[] _pages =
    [
        new(MessageAttachmentType.Image),
        new(MessageAttachmentType.Video),
        new(MessageAttachmentType.Gif)
    ];
    private readonly ILocalizationService _localization;
    private readonly List<ChatMediaItem> _items = [];
    private readonly HashSet<long> _attachmentIds = [];
    private CancellationTokenSource? _session;
    private string _chatId = string.Empty;
    private int _index;
    private bool _mediaNotFound;
    private bool _refreshed;

    public ChatMediaViewer(IMessengerService messenger, ILocalizationService localization)
        : this(messenger, localization, Downloads)
    {
    }

    internal ChatMediaViewer(IMessengerService messenger, ILocalizationService localization, HttpClient downloads,
        TimeSpan? idleTimeout = null)
    {
        _messenger = messenger;
        _localization = localization;
        _downloads = downloads;
        _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30);
    }

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private ChatMediaItem? _current;
    [ObservableProperty] private Uri? _source;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _saveStatus;

    public CancellationToken Lifetime => _session?.Token ?? new CancellationToken(canceled: true);
    public bool CanGoPrevious => IsOpen && !IsLoading && !_mediaNotFound && (_index > 0 || _index < 0);
    public bool CanGoNext => IsOpen && !IsLoading && !_mediaNotFound &&
        (_index + 1 < _items.Count || _pages.Any(page => page.HasMore || page.Pending.Count > 0));

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    public async Task OpenAsync(string chatId, MessageAttachmentItemViewModel attachment)
    {
        if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(attachment.FileId) ||
            !(attachment.IsVideo || attachment.IsImageOrGif))
            return;

        Close();
        _session = new();
        var token = _session.Token;
        _chatId = chatId;
        _index = -1;
        IsLoading = true;
        Current = new(attachment.AttachmentId, attachment.FileId, attachment.FileName, attachment.IsVideo);
        IsOpen = true;
        try
        {
            await ShowAsync(Current, attachment.FileUrl, token);
            if (!token.IsCancellationRequested && Source is not null)
                await LoadNavigationAsync(Current, 0, token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
    }

    public void Close()
    {
        _session?.Cancel();
        _session?.Dispose();
        _session = null;
        _items.Clear();
        _attachmentIds.Clear();
        foreach (var page in _pages)
        {
            page.Pending.Clear();
            page.Offset = 0;
            page.HasMore = true;
        }
        _index = -1;
        _mediaNotFound = false;
        Source = null;
        Current = null;
        Error = null;
        SaveStatus = null;
        IsOpen = false;
        IsLoading = false;
    }

    public async Task NavigateAsync(int direction)
    {
        if (Current is not { } current || !(direction switch { -1 => CanGoPrevious, 1 => CanGoNext, _ => false }))
            return;

        var token = Lifetime;
        IsLoading = true;
        Error = null;
        try
        {
            await LoadNavigationAsync(current, direction, token);
            if (token.IsCancellationRequested || Error is not null)
                return;

            var target = _index + direction;
            if (_index >= 0 && target >= 0 && target < _items.Count)
            {
                _index = target;
                await ShowAsync(_items[target], null, token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
    }

    private async Task LoadNavigationAsync(ChatMediaItem current, int direction, CancellationToken token)
    {
        var pageLoads = 0;
        try
        {
            while (_index < 0 || _index + direction >= _items.Count)
            {
                var pendingLoads = _pages.Where(page => page.HasMore && page.Pending.Count == 0).ToArray();
                if (pageLoads + pendingLoads.Length > MaxPageLoads)
                    break;

                if (pendingLoads.Length > 0)
                {
                    pageLoads += pendingLoads.Length;
                    await Task.WhenAll(pendingLoads.Select(page => LoadPageAsync(page, token)));
                    token.ThrowIfCancellationRequested();
                }

                // Головы всех непустых очередей образуют следующий элемент в серверном порядке.
                var next = _pages.Where(page => page.Pending.Count > 0)
                    .OrderByDescending(page => page.Pending.Peek().SentAt)
                    .ThenBy(page => page.Pending.Peek().AttachmentId)
                    .FirstOrDefault();
                if (next is null)
                    break;

                var entry = next.Pending.Dequeue();
                if (entry.Attachment is not { FileId.Length: > 0 } attachment || !_attachmentIds.Add(entry.AttachmentId))
                    continue;

                if (_index < 0 && (current.AttachmentId != 0
                    ? current.AttachmentId == entry.AttachmentId : current.FileId == attachment.FileId))
                    _index = _items.Count;

                _items.Add(new(entry.AttachmentId, attachment.FileId,
                    string.IsNullOrWhiteSpace(attachment.FileName) ? _localization.GetString("Messenger_File") : attachment.FileName,
                    attachment.Type == MessageAttachmentType.Video));
            }

            if (_index < 0)
            {
                _mediaNotFound = true;
                Error = _localization.GetString("Error_MediaNotFound");
            }
            else if (_index + direction >= _items.Count && _pages.Any(page => page.HasMore || page.Pending.Count > 0))
            {
                Error = _localization.GetString("Error_AttachmentsLoadFailed");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
                Error = _localization.GetString("Error_AttachmentsLoadFailed");
        }
    }

    private async Task LoadPageAsync(AttachmentPages state, CancellationToken token)
    {
        var (error, page, total) = await _messenger.ListChatAttachmentsAsync(
            _chatId, state.Type, state.Offset, PageSize, token).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if (error is not { IsSuccess: true } || page is null)
            throw new IOException();

        state.Offset += page.Count;
        state.HasMore = page.Count > 0 && state.Offset < total;
        foreach (var entry in page)
            state.Pending.Enqueue(entry);
    }

    private sealed class AttachmentPages(MessageAttachmentType type)
    {
        public MessageAttachmentType Type { get; } = type;
        public Queue<ChatAttachmentInfo> Pending { get; } = new();
        public int Offset { get; set; }
        public bool HasMore { get; set; } = true;
    }

    public async Task RefreshFailedSourceAsync(Uri failedSource)
    {
        if (!ReferenceEquals(Source, failedSource) || Current is not { } current)
            return;

        var token = Lifetime;
        Source = null;
        if (_refreshed)
        {
            Error = _localization.GetString("Error_MediaOpen");
            return;
        }

        _refreshed = true;
        try
        {
            var url = await ResolveAsync(current, token);
            if (token.IsCancellationRequested || !ReferenceEquals(Current, current))
                return;

            Source = url;
            if (url is null)
                ReportOpenError();
        }
        catch (OperationCanceledException) { }
    }

    public async Task SaveAsync(ChatMediaItem item, CancellationToken token,
        Func<string, Func<Stream, Task>, CancellationToken, Task<bool>> save)
    {
        if (token.IsCancellationRequested || !ReferenceEquals(item, Current))
            return;

        SaveStatus = null;
        try
        {
            var uri = Source ?? await ResolveAsync(item, token) ?? throw new IOException();
            using var first = await GetDownloadAsync(uri, token);
            bool saved;
            if (first.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.NotFound)
            {
                uri = await ResolveAsync(item, token) ?? throw new IOException();
                using var retry = await GetDownloadAsync(uri, token);
                saved = await SaveResponseAsync(retry);
            }
            else
            {
                saved = await SaveResponseAsync(first);
            }

            if (saved && !token.IsCancellationRequested)
                SaveStatus = _localization.GetString("Messenger_MediaSaved");

            Task<bool> SaveResponseAsync(HttpResponseMessage response)
            {
                token.ThrowIfCancellationRequested();
                response.EnsureSuccessStatusCode();
                var extension = Path.GetExtension(item.FileName);
                if (extension.Length <= 1)
                    extension = GetMediaExtension(response.Content.Headers.ContentType?.MediaType);
                return save(extension, output => CopyDownloadAsync(response.Content, output, token), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
                SaveStatus = _localization.GetString("Error_MediaSave");
        }
    }

    private async Task<HttpResponseMessage> GetDownloadAsync(Uri uri, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        return await _downloads.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
    }

    private async Task CopyDownloadAsync(HttpContent content, Stream output, CancellationToken token)
    {
        await using var source = await content.ReadAsStreamAsync(token);
        var buffer = new byte[81920];
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        while (true)
        {
            idle.CancelAfter(_idleTimeout);
            var count = await source.ReadAsync(buffer.AsMemory(), idle.Token);
            idle.CancelAfter(Timeout.InfiniteTimeSpan);
            idle.Token.ThrowIfCancellationRequested();
            if (count == 0)
                return;
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }

    private static string GetMediaExtension(string? mimeType) => mimeType?.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/svg+xml" => ".svg",
        "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
        "video/quicktime" => ".mov",
        "video/x-msvideo" => ".avi",
        "video/x-ms-wmv" => ".wmv",
        "video/x-matroska" => ".mkv",
        "video/mpeg" => ".mpeg",
        "video/ogg" => ".ogv",
        "image/apng" or "image/avif" or "image/bmp" or "image/gif" or "image/heic" or "image/heif" or
        "image/png" or "image/tiff" or "image/webp" or "video/3gpp" or "video/mp4" or "video/webm"
            => "." + mimeType[(mimeType.IndexOf('/') + 1)..].ToLowerInvariant(),
        _ => ".bin"
    };

    public void ReportOpenError() => Error = _localization.GetString("Error_MediaOpen");

    private async Task ShowAsync(ChatMediaItem item, string? initialUrl, CancellationToken token)
    {
        Source = null;
        Current = item;
        Error = null;
        _refreshed = false;
        var url = ParseUrl(initialUrl) ?? await ResolveAsync(item, token);
        if (token.IsCancellationRequested)
            return;
        Source = url;
        if (url is null)
            Error = _localization.GetString("Error_MediaOpen");
    }

    private async Task<Uri?> ResolveAsync(ChatMediaItem item, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            return ParseUrl(await _messenger.ResolveFileUrlAsync(item.FileId, token).WaitAsync(token));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return null;
        }
    }

    private static Uri? ParseUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
}
