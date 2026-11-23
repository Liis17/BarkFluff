using System.Collections.ObjectModel;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Files;
using BarkFluff.WebApi.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed record AttachmentSourceFile(string Path, string Name, string? ContentType = null, bool IsTemporary = false, bool IsVoice = false);

public sealed partial class AttachmentComposer(
    IMessengerService messenger, ILocalizationService localization, IUiDispatcher dispatcher) : ObservableObject
{
    public const int MaxFiles = 10;
    private readonly SemaphoreSlim _uploadSlots = new(2);
    private readonly ObservableCollection<PendingAttachment> _files = [];
    private readonly HashSet<Task> _inputs = [];
    private CancellationTokenSource _scope = new();
    private Task _cleanup = Task.CompletedTask;

    public IReadOnlyList<PendingAttachment> Files => _files;
    public bool HasFiles => _files.Count != 0;
    internal bool HasVoice => _files.Any(file => file.Source.IsVoice);
    internal CancellationToken ScopeToken => _scope.Token;

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private string? _error;

    internal async Task AddAsync(Func<CancellationToken, Task<IReadOnlyList<AttachmentSourceFile>>> read, Func<bool> isCurrent)
    {
        var task = ReadAsync(read, isCurrent, ScopeToken);
        _inputs.Add(task);
        try { await task; }
        finally { _inputs.Remove(task); }
    }

    private async Task ReadAsync(Func<CancellationToken, Task<IReadOnlyList<AttachmentSourceFile>>> read,
        Func<bool> isCurrent, CancellationToken token)
    {
        try
        {
            var selected = await read(token);
            var sizes = token.IsCancellationRequested || !isCurrent() || IsSending
                ? []
                : await Task.Run(() => selected.Select(file => ReadSize(file.Path)).ToArray());
            if (token.IsCancellationRequested || !isCurrent() || IsSending || sizes.Length != selected.Count)
            {
                foreach (var file in selected)
                    DeleteTemporary(file);
                return;
            }

            Error = null;
            for (var index = 0; index < selected.Count; index++)
            {
                var file = selected[index];
                var error = AddFile(file, sizes[index]);
                if (error is not null)
                {
                    Error ??= localization.GetString(error);
                    DeleteTemporary(file);
                }
            }
            OnPropertyChanged(nameof(HasFiles));
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested && isCurrent())
                Error = localization.GetString("Messenger_AttachmentReadFailed");
        }
    }

    private static long ReadSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    private string? AddFile(AttachmentSourceFile file, long size)
    {
        if (_files.Count >= MaxFiles)
            return "Messenger_AttachmentLimit";
        if (size == 0)
            return "Messenger_AttachmentReadFailed";
        if (size >= BarkFluff.WebApi.Core.WebApi.MaxFileUploadRequestSize)
            return "Messenger_AttachmentTooLarge";

        _files.Add(new PendingAttachment(this, file, size, localization));
        return null;
    }

    internal async Task<string[]?> PrepareAsync(CancellationToken token, Func<bool> isCurrent)
    {
        var files = _files.ToArray();
        var pendingBytes = files.Where(file => file.FileId is null && file.ActiveUpload is not { IsCompleted: false })
            .Sum(file => file.Size);
        if (pendingBytes > 0)
        {
            long? available = null;
            try { available = await messenger.GetAttachmentStorageAvailableAsync(token); }
            catch (Exception)
            {
                // При недоступной проверке квоту проверит сервер при загрузке.
            }

            if (token.IsCancellationRequested || !isCurrent())
                return null;
            if (available is { } remaining && pendingBytes > remaining)
            {
                Error = localization.GetString("Messenger_AttachmentStorageFull");
                return null;
            }
        }

        Error = null;
        await Task.WhenAll(files.Select(file => UploadAsync(file, isCurrent)));
        if (token.IsCancellationRequested || !isCurrent() || files.Any(file => file.FileId is null))
            return null;

        return files.Select(file => file.FileId!).ToArray();
    }

    internal Task UploadAsync(PendingAttachment file, Func<bool> isCurrent)
    {
        if (!_files.Contains(file) || file.FileId is not null || !isCurrent())
            return Task.CompletedTask;
        if (file.ActiveUpload is { IsCompleted: false } active)
            return active;

        return file.ActiveUpload = UploadCoreAsync(file, isCurrent);
    }

    private async Task UploadCoreAsync(PendingAttachment file, Func<bool> isCurrent)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ScopeToken);
        file.Cancellation = cancellation;
        file.Progress = 0;
        file.SetState("Messenger_AttachmentUploading", uploading: true);
        var progress = new UploadProgress(value => dispatcher.Post(() =>
        {
            if (ReferenceEquals(file.Cancellation, cancellation) && !cancellation.IsCancellationRequested && isCurrent())
                file.Progress = value * 100;
        }));

        try
        {
            await _uploadSlots.WaitAsync(cancellation.Token);
            (ErrorReturner error, string? fileId) result;
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                result = await messenger.UploadAttachmentAsync(file.Source.Path, file.Name, file.Source.ContentType,
                    file.UploadType, progress, cancellation.Token);
            }
            finally
            {
                _uploadSlots.Release();
            }
            if (!isCurrent() || !_files.Contains(file))
                return;

            cancellation.Token.ThrowIfCancellationRequested();
            if (result.error is { IsSuccess: true } && !string.IsNullOrWhiteSpace(result.fileId))
            {
                file.FileId = result.fileId;
                file.Progress = 100;
                file.SetState("Messenger_AttachmentReady");
            }
            else
            {
                file.SetState(result.error?.ErrorResourceKey ?? "Messenger_AttachmentUploadFailed", canRetry: true);
            }
        }
        catch (Exception)
        {
            if (isCurrent() && _files.Contains(file))
                file.SetState(cancellation.IsCancellationRequested
                    ? "Messenger_AttachmentCancelled" : "Messenger_AttachmentUploadFailed", canRetry: true);
        }
        finally
        {
            file.Cancellation = null;
        }
    }

    internal Task ClearAsync()
    {
        var scope = _scope;
        _scope = new CancellationTokenSource();
        scope.Cancel();
        var files = _files.ToArray();
        _files.Clear();
        IsSending = false;
        Error = null;
        OnPropertyChanged(nameof(HasFiles));
        return _cleanup = FinishCleanupAsync(scope, files, [.. _inputs, _cleanup]);
    }

    partial void OnIsSendingChanged(bool value)
    {
        foreach (var file in _files)
            file.UpdateComposerState();
    }

    private static async Task FinishCleanupAsync(CancellationTokenSource scope, PendingAttachment[] files, Task[] inputs)
    {
        using (scope)
            await Task.WhenAll(inputs.Concat(files.Select(RemoveTemporaryAsync)));
    }

    internal async Task RemoveAsync(PendingAttachment file)
    {
        if (IsSending || !_files.Remove(file))
            return;
        file.Cancellation?.Cancel();
        OnPropertyChanged(nameof(HasFiles));
        var cleanup = RemoveTemporaryAsync(file);
        _cleanup = Task.WhenAll(_cleanup, cleanup);
        await cleanup;
    }

    private static async Task RemoveTemporaryAsync(PendingAttachment file)
    {
        if (file.ActiveUpload is { } active)
            await active;
        DeleteTemporary(file.Source);
    }

    private static void DeleteTemporary(AttachmentSourceFile file)
    {
        if (!file.IsTemporary)
            return;
        try { File.Delete(file.Path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class UploadProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

public sealed partial class PendingAttachment : ObservableObject
{
    private readonly AttachmentComposer _owner;
    private readonly ILocalizationService _localization;
    private readonly UploadFileType _type;

    internal PendingAttachment(AttachmentComposer owner, AttachmentSourceFile file, long size, ILocalizationService localization)
    {
        _owner = owner;
        _localization = localization;
        Source = file;
        Size = size;
        _type = file.IsVoice ? UploadFileType.MessageAttachmentVoice : ResolveType(file.Name, file.ContentType);
        SetState("Messenger_AttachmentWaiting");
    }

    internal AttachmentSourceFile Source { get; }
    internal string? FileId { get; set; }
    internal Task? ActiveUpload { get; set; }
    internal CancellationTokenSource? Cancellation { get; set; }
    public string Name => Source.Name;
    public string Path => Source.Path;
    public long Size { get; }
    public string SizeLabel => Size < 1024 * 1024 ? $"{Size / 1024d:0.#} KiB" : $"{Size / (1024d * 1024):0.#} MiB";
    internal UploadFileType UploadType => AsDocument ? UploadFileType.MessageAttachmentDocument : _type;
    public bool HasVideoPreview => _type == UploadFileType.MessageAttachmentVideo;
    public bool HasThumbnail => HasVideoPreview || _type is UploadFileType.MessageAttachmentImage or UploadFileType.MessageAttachmentGif;
    public bool SupportsAsDocument => _type is UploadFileType.MessageAttachmentImage
        or UploadFileType.MessageAttachmentGif or UploadFileType.MessageAttachmentAudio;
    public bool CanRemove => !_owner.IsSending;
    public bool CanChangeType => CanRemove && !IsUploading && FileId is null && SupportsAsDocument;
    public string TypeLabel => _localization.GetString(UploadType switch
    {
        UploadFileType.MessageAttachmentImage => "Messenger_AttachmentPhoto",
        UploadFileType.MessageAttachmentGif => "Messenger_AttachmentGif",
        UploadFileType.MessageAttachmentVideo => "Messenger_AttachmentVideo",
        UploadFileType.MessageAttachmentVoice => "Messenger_VoiceAttachment",
        UploadFileType.MessageAttachmentAudio => "Messenger_AttachmentAudio",
        _ => "Messenger_AttachmentFile"
    });

    public bool AsDocument
    {
        get;
        set
        {
            if (!CanChangeType || !SetProperty(ref field, value))
                return;
            OnPropertyChanged(nameof(TypeLabel));
        }
    }

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _canRetry;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChangeType))] private bool _isUploading;

    internal void SetState(string key, bool uploading = false, bool canRetry = false)
    {
        Status = _localization.GetString(key);
        IsUploading = uploading;
        CanRetry = canRetry;
    }

    internal void UpdateComposerState()
    {
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanChangeType));
    }

    [RelayCommand]
    private void Cancel() => Cancellation?.Cancel();

    [RelayCommand]
    private Task RemoveAsync() => _owner.RemoveAsync(this);

    internal static UploadFileType ResolveType(string name, string? mime)
    {
        var extension = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return (mime?.ToLowerInvariant(), extension) switch
        {
            ("image/gif", _) or (_, ".gif") => UploadFileType.MessageAttachmentGif,
            (_, ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" or ".m4v") => UploadFileType.MessageAttachmentVideo,
            ({ } value, _) when value.StartsWith("video/", StringComparison.Ordinal) => UploadFileType.MessageAttachmentVideo,
            ({ } value, _) when value.StartsWith("image/", StringComparison.Ordinal) => UploadFileType.MessageAttachmentImage,
            ({ } value, _) when value.StartsWith("audio/", StringComparison.Ordinal) => UploadFileType.MessageAttachmentAudio,
            (_, ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".avif" or ".heic" or ".heif" or ".tiff" or ".tif" or ".svg" or ".ico")
                => UploadFileType.MessageAttachmentImage,
            (_, ".mp3" or ".ogg" or ".wav" or ".aac" or ".flac" or ".m4a") => UploadFileType.MessageAttachmentAudio,
            _ => UploadFileType.MessageAttachmentDocument
        };
    }
}
