using BarkFluff.Proto.Calls;
using Grpc.Core;

namespace BarkFluff.Client.Core.Services.Calls;

public sealed record GroupCall(Guid Id, Guid ChatId, CallMediaType MediaType);

public sealed partial class AudioCallService
{
    private Guid? _watchedGroup;
    private Guid? _watchedCall;
    private readonly HashSet<long> _observedJoins = [];
    private CancellationTokenSource? _groupLookup;
    private bool _activeCallsUnsupported;
    private readonly HashSet<Guid> _endedDuringLookup = [];
    public GroupCall? JoinableCall { get; private set; }

    public void WatchGroup(Guid? chatId)
    {
        if (_watchedGroup == chatId)
            return;
        CancelGroupLookup();
        _watchedGroup = chatId;
        _watchedCall = _call?.Status.ChatId == chatId ? _call?.Status.Id : null;
        _observedJoins.Clear();
        JoinableCall = null;
        RefreshGroupCall();
    }

    private void ResetGroupLookup()
    {
        CancelGroupLookup();
        _watchedCall = null;
        _observedJoins.Clear();
        JoinableCall = null;
        _activeCallsUnsupported = false;
    }

    private void CancelGroupLookup()
    {
        _groupLookup?.Cancel();
        _groupLookup?.Dispose();
        _groupLookup = null;
        _endedDuringLookup.Clear();
    }

    private void ObserveGroupRing(Guid? chatId, Guid id)
    {
        if (chatId is null || chatId != _watchedGroup || _watchedCall == id)
            return;
        _watchedCall = id;
        _observedJoins.Clear();
    }

    private void ObserveGroupEvent(Guid id, CallEvent item)
    {
        if (_watchedGroup is null)
            return;
        if (item.Ended is not null && (id == _watchedCall || id == JoinableCall?.Id || _groupLookup is not null))
        {
            if (id == _watchedCall)
                _watchedCall = null;
            if (id == JoinableCall?.Id)
            {
                JoinableCall = null;
                Changed?.Invoke();
            }
            // Ответ запроса мог быть сформирован до события завершения.
            if (_groupLookup is not null)
                _endedDuringLookup.Add(id);
        }
        else if (id == _watchedCall && JoinableCall is null && item.Member is
                 { Action: ParticipantAction.ParticipantJoined, UserId: > 0 } joined && _observedJoins.Add(joined.UserId))
            RefreshGroupCall();
    }

    public void RefreshGroupCall()
    {
        if (_watchedGroup is { } chatId && _call is null && Available && !_activeCallsUnsupported &&
            _session is { } session && IsCurrent(session))
        {
            // Запросы идут при открытии чата, восстановлении потока и событиях звонка.
            CancelGroupLookup();
            var lookup = _groupLookup = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime.Token);
            _ = LoadGroupCallAsync(session, chatId, lookup);
        }
    }

    private async Task LoadGroupCallAsync(Session session, Guid chatId, CancellationTokenSource lookup)
    {
        var token = lookup.Token;
        try
        {
            var response = await session.Signaling.GetActiveAsync(chatId.ToString("D"), token);
            if (token.IsCancellationRequested || !IsCurrent(session) || _call is not null)
                return;
            JoinableCall = response?.Calls
                .Where(item => Guid.TryParse(item.ChatId, out var group) && group == chatId)
                .Select(item => Guid.TryParse(item.CallId, out var id) && id != Guid.Empty &&
                    !_endedDuringLookup.Contains(id) && item.MediaType is CallMediaType.CallMediaAudio or CallMediaType.CallMediaVideo
                    ? new GroupCall(id, chatId, item.MediaType) : null)
                .FirstOrDefault(item => item is not null);
            if (JoinableCall is { } found)
                _watchedCall = found.Id;
            Changed?.Invoke();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
        {
            if (!token.IsCancellationRequested && IsCurrent(session))
            {
                _activeCallsUnsupported = true;
                JoinableCall = null;
                Changed?.Invoke();
            }
        }
        catch (Exception)
        {
            // Баннер необязателен. Следующая сверка запускается событием.
        }
        finally
        {
            if (_groupLookup == lookup)
            {
                _groupLookup = null;
                lookup.Dispose();
            }
        }
    }

    public async Task JoinAsync(string chatName)
    {
        if (_session is not { } session || !IsCurrent(session) || !Available || _call is not null ||
            JoinableCall is not { } group)
            return;

        CancelGroupLookup();
        JoinableCall = null;
        session.Closed.Remove(group.Id);
        Outcome = null;
        var call = _call = new Call(new(group.Id, 0, chatName, AudioCallPhase.Connecting,
            ChatId: group.ChatId, MediaType: group.MediaType));
        BeginConnecting(session, call);
        try
        {
            var answer = await session.Signaling.JoinAsync(group.Id.ToString("D"), session.Lifetime.Token);
            if (IsCurrent(session, call))
                await ConnectAsync(session, call, answer?.LivekitUrl, answer?.AccessToken,
                    call.AudioQualityRevision == 0 ? answer?.AudioQuality : call.Status.AudioQuality);
        }
        catch (Exception ex)
        {
            if (IsCurrent(session, call))
            {
                if (ex is RpcException { StatusCode: StatusCode.Unimplemented })
                    _activeCallsUnsupported = true;
                HandleFailure(session, call, ex, disableUnavailable: false);
            }
        }
    }

    private void ReceiveCallDetails(Call call, CallEvent item)
    {
        if (item.AudioQuality is { } change && Enum.IsDefined(change.Quality))
        {
            call.AudioQualityRevision++;
            ApplyAudioQuality(call, change.Quality);
        }
        else if (call.Status.ChatId is not null && item.Member is { UserId: > 0 } member)
        {
            var changed = member.Action switch
            {
                ParticipantAction.ParticipantJoined => call.Participants.Add(member.UserId),
                ParticipantAction.ParticipantLeft => call.Participants.Remove(member.UserId),
                _ => false
            };
            if (changed)
            {
                UpdateParticipants(call);
                Changed?.Invoke();
            }
        }
    }

    private static void UpdateParticipants(Call call) =>
        call.Status = call.Status with { Participants = call.Participants.Order().ToArray() };
}
