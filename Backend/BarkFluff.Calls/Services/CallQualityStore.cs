using System.Collections.Concurrent;

using BarkFluff.Calls.Domain;

namespace BarkFluff.Calls.Services;

/// <summary>
/// Текущее общее качество голоса по активным звонкам (in-memory, Singleton).
/// Транзиентное состояние звонка — как и подписки в <see cref="CallEventSubscriptionsManager"/>:
/// при рестарте сервиса активные звонки и так не переживают, поэтому колонку в CDR не заводим.
/// Дефолт для неизвестного звонка — <see cref="CallAudioQualityKind.Auto"/>.
/// </summary>
public class CallQualityStore
{
    private readonly ConcurrentDictionary<Guid, CallAudioQualityKind> _audio = new();

    public CallAudioQualityKind GetAudio(Guid callId)
        => _audio.TryGetValue(callId, out var quality) ? quality : CallAudioQualityKind.Auto;

    public void SetAudio(Guid callId, CallAudioQualityKind quality) => _audio[callId] = quality;

    public void Remove(Guid callId) => _audio.TryRemove(callId, out _);
}
