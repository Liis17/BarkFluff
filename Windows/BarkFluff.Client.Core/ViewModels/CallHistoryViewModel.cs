using System.Collections.ObjectModel;
using BarkFluff.Proto.Calls;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using BarkFluff.Client.Core.Services.Calls;

namespace BarkFluff.Client.Core.ViewModels;

public sealed record CallHistoryEntry(Guid Id, CallHistoryItem Call, string Title, string Details);

public sealed class CallHistoryViewModel(
    ICallSignaling signaling, Func<CallHistoryItem, Guid, CallHistoryEntry> format) : IDisposable
{
    private readonly HashSet<Guid> _seen = [];
    private CancellationTokenSource? _window = new();
    private Timestamp? _before;
    private CallHistoryFilter _filter;
    private bool _unsupported;

    public ObservableCollection<CallHistoryEntry> Items { get; } = [];
    public bool IsOpen => _window is not null && signaling.IsCurrent;
    public bool HasMore { get; private set; } = true;
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public Task SetFilterAsync(CallHistoryFilter filter)
    {
        if (!IsOpen || _unsupported)
            return Task.CompletedTask;

        _window!.Cancel();
        _window.Dispose();
        _window = new();
        _filter = filter;
        _before = null;
        _seen.Clear();
        Items.Clear();
        HasMore = true;
        IsLoading = false;
        return LoadMoreAsync();
    }

    public async Task LoadMoreAsync()
    {
        if (!IsOpen || IsLoading || !HasMore)
            return;

        var request = new ListCallHistoryRequest { Filter = _filter, Limit = 30, BeforeStartedAt = _before };
        var window = _window!;
        var token = window.Token;
        IsLoading = true;
        Error = null;
        Changed?.Invoke();
        try
        {
            var response = await signaling.ListHistoryAsync(request, token);
            if (!IsOpen || _window != window)
                return;
            if (response is null)
                throw new InvalidOperationException();

            foreach (var item in response.Items)
            {
                if (item.StartedAt is not null && Guid.TryParse(item.CallId, out var id) && id != Guid.Empty && _seen.Add(id))
                    Items.Add(format(item, id));
            }

            var cursor = response.Items.LastOrDefault()?.StartedAt;
            HasMore = response.HasMore && cursor is not null && (_before is null || cursor.CompareTo(_before) < 0);
            _before = cursor;
        }
        catch (Exception ex)
        {
            if (IsOpen && _window == window)
            {
                _unsupported = ex is RpcException { StatusCode: StatusCode.Unimplemented };
                HasMore = !_unsupported;
                Error = _unsupported ? "Calls_Unavailable" : "Calls_HistoryError";
            }
        }
        finally
        {
            if (IsOpen && _window == window)
            {
                IsLoading = false;
                Changed?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        if (_window is null)
            return;
        _window.Cancel();
        _window.Dispose();
        _window = null;
        Items.Clear();
        Changed?.Invoke();
    }
}
