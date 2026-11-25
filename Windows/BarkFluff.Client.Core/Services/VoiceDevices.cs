namespace BarkFluff.Client.Core.Services;

public interface IVoiceRecorder
{
    Task<IVoiceRecording> StartAsync(Action interrupted, CancellationToken cancellationToken);
}

public interface IVoiceRecording : IDisposable
{
    // Возвращает завершённый PCM WAV. Поток принадлежит записи.
    Task<Stream> StopAsync(CancellationToken cancellationToken);
}

public interface IVoicePlayer
{
    Task<IVoicePlayback> PrepareAsync(string url, bool isOpus, Action<bool> ended, CancellationToken cancellationToken);
}

public interface IVoicePlayback : IDisposable
{
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    bool CanSeek { get; }
    void Play(double speed);
    void Pause();
    void Seek(TimeSpan position);
    void SetSpeed(double speed);
}
