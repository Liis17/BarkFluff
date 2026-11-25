using System.Threading.Channels;
using BarkFluff.Client.Core.Infrastructure.Realtime;
using BarkFluff.Proto.Updates;
using Grpc.Core;
using Channel = System.Threading.Channels.Channel;

namespace BarkFluff.Client.Core.Services.SecretChats;

internal sealed class SecretChatStreams(
    Func<CancellationToken, IAsyncEnumerable<SecretChatInviteEvent>> invites,
    Func<CancellationToken, IAsyncEnumerable<SecretChatInviteResolutionEvent>> resolutions,
    Func<CancellationToken, IAsyncEnumerable<NewSecretMessageEvent>> messages,
    TimeProvider? clock = null)
{
    internal async Task RunAsync(SecretChatService service, Action<bool> availabilityChanged,
        Action<SecretChatInviteEvent> inviteReceived, Func<Task> refresh, CancellationToken lifetime)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var token = stop.Token;
        var unsupported = 0;
        void Unsupported()
        {
            if (Interlocked.Exchange(ref unsupported, 1) != 0)
                return;
            stop.Cancel();
            availabilityChanged(false);
        }

        async Task RetryPendingAsync()
        {
            try
            {
                await service.RetryPendingOperationsAsync(token);
            }
            catch (Exception error) when (SecretChatService.CanRetryStreamOperation(error))
            {
                // Незавершённый Ack или приглашение не должны задерживать подписку.
            }
        }

        for (var attempt = 1; !token.IsCancellationRequested; attempt++)
        {
            try
            {
                await service.SynchronizePrekeysAsync(token);
                await RetryPendingAsync();
                token.ThrowIfCancellationRequested();
                availabilityChanged(true);
                await refresh();
                break;
            }
            catch (RpcException error) when (error.StatusCode == StatusCode.Unimplemented)
            {
                Unsupported();
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (RpcException error) when (!SecretChatService.CanRetryStreamOperation(error)) { return; }
            catch (Exception)
            {
                try { await Task.Delay(StreamRetryLoop.Backoff(attempt), token); }
                catch (OperationCanceledException) { return; }
            }
        }

        var prekeys = Channel.CreateBounded<bool>(1);
        var refreshes = Channel.CreateBounded<bool>(1);
        var retryPending = 0;
        var connected = false;
        var messageProcessingFailed = false;
        await Task.WhenAll(
            RunWorkAsync(prekeys.Reader, async () =>
            {
                // Ошибка синхронизации не прерывает подписку и не отменяет повтор Ack.
                try { await service.SynchronizePrekeysAsync(token); }
                catch (Exception error) when (SecretChatService.CanRetryStreamOperation(error)) { }
                if (Interlocked.Exchange(ref retryPending, 0) != 0)
                {
                    await RetryPendingAsync();
                    refreshes.Writer.TryWrite(true);
                }
            }),
            RunWorkAsync(refreshes.Reader, async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), clock ?? TimeProvider.System, token);
                // Изменения, пришедшие во время перечитывания, вызовут следующий проход.
                refreshes.Reader.TryRead(out _);
                await refresh();
            }),
            Run(invites, async invite =>
            {
                if (await service.ShouldOfferInviteAsync(invite, token))
                    inviteReceived(invite);
            }),
            Run(resolutions, async resolution =>
            {
                await service.ApplyResolutionAsync(resolution, token);
                refreshes.Writer.TryWrite(true);
            }),
            Run(messages, async message =>
            {
                if (message.Envelope is not { } envelope)
                    return;
                try
                {
                    var delivery = await service.ReceiveMessageAsync(envelope, token, keepStreamOnAckFailure: true);
                    if (delivery.ConsumedPreKey)
                        prekeys.Writer.TryWrite(true);
                }
                catch (SignalProtocolException error) when (error.Error == SignalError.UntrustedIdentity)
                {
                    // Сервис уже сохранил состояние KeyChanged.
                }
                catch (Exception error) when (SecretChatService.CanRetryStreamOperation(error))
                {
                    messageProcessingFailed = true;
                    availabilityChanged(false);
                    throw;
                }
                finally
                {
                    // Ack может упасть после записи, а при повторной доставке запись уже существует.
                    refreshes.Writer.TryWrite(true);
                }
            }, ct =>
            {
                if (connected)
                {
                    Interlocked.Exchange(ref retryPending, 1);
                    prekeys.Writer.TryWrite(true);
                }
                var stream = messages(ct);
                connected = true;
                if (messageProcessingFailed)
                {
                    messageProcessingFailed = false;
                    availabilityChanged(true);
                }
                return Task.FromResult<IAsyncEnumerable<NewSecretMessageEvent>?>(stream);
            }, retryOnHandlerFailure: true));

        async Task RunWorkAsync(ChannelReader<bool> reader, Func<Task> work)
        {
            try
            {
                await foreach (var _ in reader.ReadAllAsync(token))
                    await HandleAsync(work);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }

        async Task HandleAsync(Func<Task> handle, bool retryOnHandlerFailure = false)
        {
            try { await handle(); }
            catch (RpcException error) when (error.StatusCode == StatusCode.Unimplemented) { Unsupported(); }
            catch (OperationCanceledException) { stop.Cancel(); }
            catch (RpcException error) when (!SecretChatService.CanRetryStreamOperation(error)) { stop.Cancel(); }
            catch (Exception) when (!retryOnHandlerFailure)
            {
                // Сбой отдельного события не закрывает поток.
            }
        }

        Task Run<T>(Func<CancellationToken, IAsyncEnumerable<T>> source, Func<T, Task> handle,
            Func<CancellationToken, Task<IAsyncEnumerable<T>?>>? connect = null, bool retryOnHandlerFailure = false) =>
            StreamRetryLoop.RunAsync(connect ?? (ct => Task.FromResult<IAsyncEnumerable<T>?>(source(ct))),
                _ => { }, null, StreamRetryLoop.Backoff, token,
                stopOnUnimplemented: true, onItemAsync: item => HandleAsync(() => handle(item), retryOnHandlerFailure),
                onUnimplemented: Unsupported);
    }
}
