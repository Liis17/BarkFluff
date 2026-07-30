using BarkFluff.Client.Core.Infrastructure.Threading;

using Microsoft.UI.Dispatching;

namespace BarkFluff.Client.WinUI.Infrastructure.Threading;

/// <summary>
/// Очередь UI-потока захватывается при создании (в DI это происходит на UI-потоке).
/// Брать <see cref="DispatcherQueue.GetForCurrentThread"/> внутри ViewModel нельзя:
/// вне UI-потока он вернёт <c>null</c>.
/// </summary>
public sealed class DispatcherQueueUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    public void Post(Action action)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }
}
