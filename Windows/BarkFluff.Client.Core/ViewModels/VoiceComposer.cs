using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class VoiceComposer(
    IVoiceRecorder? recorder, ILocalizationService localization, IUiDispatcher dispatcher, TimeProvider clock,
    string temporaryDirectory) : ObservableObject
{
    public static string TemporaryDirectory { get; } = Path.Combine(Path.GetTempPath(), "BarkFluff", "Voice");
    internal static readonly TimeSpan Minimum = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan Maximum = TimeSpan.FromMinutes(10);
    private CancellationTokenSource? _scope;
    private TaskCompletionSource? _stop;
    private Task _operation = Task.CompletedTask;

    public bool IsAvailable => recorder is not null;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _elapsed = "00:00";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _showMicrophoneSettings;

    public static void CleanTemporaryFiles()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(TemporaryDirectory, "barkfluff-*"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("barkfluff-", StringComparison.Ordinal) &&
                    Guid.TryParseExact(name.AsSpan("barkfluff-".Length), "N", out _) &&
                    Path.GetExtension(path) is ".ogg" or ".wav")
                    DeleteTemporaryFile(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal Task RecordAsync(Func<bool> isCurrent, Func<AttachmentSourceFile, Func<bool>, Task> stage)
    {
        if (recorder is null || !_operation.IsCompleted || IsBusy || !isCurrent())
            return Task.CompletedTask;

        return _operation = RecordCoreAsync(recorder, isCurrent, stage);
    }

    private async Task RecordCoreAsync(IVoiceRecorder device, Func<bool> isCurrent, Func<AttachmentSourceFile, Func<bool>, Task> stage)
    {
        using var scope = new CancellationTokenSource();
        var token = scope.Token;
        _scope = scope;
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stop = stop;
        bool Current() => ReferenceEquals(_scope, scope) && !token.IsCancellationRequested && isCurrent();
        bool started = false;
        string? path = null;
        Error = null;
        ShowMicrophoneSettings = false;
        Elapsed = "00:00";
        IsBusy = true;
        try
        {
            bool encoded;
            using (var recording = await device.StartAsync(() => dispatcher.Post(() =>
            {
                if (!Current())
                    return;
                Error = localization.GetString("Messenger_VoiceInterrupted");
                scope.Cancel();
            }), token))
            {
                if (!Current())
                    return;
                started = true;
                var start = clock.GetTimestamp();
                IsRecording = true;
                using (clock.CreateTimer(_ => dispatcher.Post(() =>
                {
                    if (!Current() || stop.Task.IsCompleted)
                        return;
                    var elapsed = clock.GetElapsedTime(start);
                    Elapsed = elapsed.ToString(@"mm\:ss");
                    if (elapsed >= Maximum)
                        stop.TrySetResult();
                }), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100)))
                {
                    await stop.Task.WaitAsync(token);
                }

                IsRecording = false;
                using var wave = await recording.StopAsync(token);
                if (!Current())
                    return;
                path = Path.Combine(temporaryDirectory, $"barkfluff-{Guid.NewGuid():N}.ogg");
                encoded = await Task.Run(() =>
                {
                    Directory.CreateDirectory(temporaryDirectory);
                    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    return VoiceOggCodec.EncodeWave(wave, output,
                        (int)(Minimum.TotalSeconds * VoiceOggCodec.SampleRate),
                        (int)(Maximum.TotalSeconds * VoiceOggCodec.SampleRate), token);
                }, token);
            }
            if (!Current())
                return;
            if (!encoded)
            {
                Error = localization.GetString("Messenger_VoiceTooShort");
                return;
            }
            await stage(new AttachmentSourceFile(path!, "voice.ogg", "audio/ogg", IsTemporary: true, IsVoice: true), Current);
            path = null;
        }
        catch (Exception)
        {
            if (Current())
            {
                Error = localization.GetString(started ? "Messenger_VoiceCaptureFailed" : "Messenger_VoiceMicUnavailable");
                ShowMicrophoneSettings = !started;
            }
        }
        finally
        {
            if (path is not null)
                DeleteTemporaryFile(path);
            if (ReferenceEquals(_scope, scope))
                _scope = null;
            _stop = null;
            IsRecording = false;
            IsBusy = false;
        }
    }

    private static void DeleteTemporaryFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Stop() => _stop?.TrySetResult();

    public Task CancelAsync()
    {
        _scope?.Cancel();
        Error = null;
        ShowMicrophoneSettings = false;
        IsRecording = false;
        return _operation;
    }
}
