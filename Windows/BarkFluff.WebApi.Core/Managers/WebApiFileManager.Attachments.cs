using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarkFluff.Proto.Files;
using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.WebApi.Core.Managers;

internal partial class WebApiFileManager
{
    internal const long MaxUploadRequestSize = 512L * 1024 * 1024;
    private static readonly HttpClient AttachmentHttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly TimeSpan UploadIdleTimeout = TimeSpan.FromMinutes(1);
    // Прокси ждёт ответ 600 секунд. Оставляем время на доставку ответа прокси.
    private static readonly TimeSpan UploadResponseTimeout = TimeSpan.FromSeconds(630);
    private static readonly TimeSpan MaxStatusWait = TimeSpan.FromSeconds(30);

    internal static Task<string> PrepareAttachmentAsync(string path, UploadFileType type, string temporaryDirectory,
        CancellationToken cancellationToken) =>
        type == UploadFileType.MessageAttachmentImage
            ? ImageProcessor.ProcessImageForUploadAsync(path, temporaryDirectory, optimizeAttachment: true,
                cancellationToken: cancellationToken)
            : Task.FromResult(path);

    public Task<long?> GetAttachmentStorageAvailableAsync(GlobalParam parameters, CancellationToken cancellationToken) =>
        _webApi.TokenManager.SafeCallAsync<long?>(async () =>
        {
            var storage = await FilesAC!.GetUserStorageInfoAsync(new GetUserStorageInfoRequest(),
                cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);
            return storage.StorageLimit - storage.TotalUsedStorage;
        }, parameters);

    public async Task<(ErrorReturner error, string? fileId)> UploadFileAsync(
        GlobalParam parameters, string path, string name, string? contentType, UploadFileType type,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string? processed = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed = await PrepareAttachmentAsync(path, type, Path.GetTempPath(), cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (processed is not null && processed != path)
            {
                name = Path.ChangeExtension(name, ".jpg");
                contentType = "image/jpeg";
            }

            var uploadPath = processed ?? path;
            return await _webApi.TokenManager.SafeCallAsync(async () =>
            {
                // CheckFileHash не учитывает тип. Дедупликацию выполняет POST с выбранным типом.
                var slot = await FilesAC!.GetUploadUrlAsync(new GetUploadUrlRequest { FileType = type },
                    cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);
                return await PostAttachmentAsync(AttachmentHttpClient, ToMediaUrl(parameters, slot.Url), uploadPath,
                    name, contentType, progress, cancellationToken).ConfigureAwait(false);
            }, parameters).ConfigureAwait(false);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure();
        }
        finally
        {
            if (processed is not null && processed != path)
            {
                try { File.Delete(processed); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal async Task<(ErrorReturner error, string? fileId)> UploadFileAsync(
        GlobalParam parameters, string path, string name, string? contentType, UploadFileType type,
        IProgress<double>? progress, CancellationToken cancellationToken, string clientOperationId)
    {
        try
        {
            return await _webApi.TokenManager.SafeCallAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var client = _webApi.GetOutgoingClient(parameters, cancellationToken).Files;
                var slot = await client.GetUploadUrlAsync(new GetUploadUrlRequest
                {
                    FileType = type,
                    ClientOperationId = clientOperationId
                }, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: cancellationToken)
                    .ResponseAsync.ConfigureAwait(false);
                var url = ToMediaUrl(parameters, slot.Url);
                return await ResumeAttachmentAsync(AttachmentHttpClient, url, PostAsync, progress, cancellationToken)
                    .ConfigureAwait(false);

                async Task<(ErrorReturner error, string? fileId)> PostAsync()
                {
                    string? processed = null;
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!File.Exists(path))
                            return Failure("Messenger_AttachmentReadFailed");
                        processed = await PrepareAttachmentAsync(path, type, Path.GetDirectoryName(path)!,
                            cancellationToken).ConfigureAwait(false);

                        var converted = processed is not null && processed != path;
                        return await PostAttachmentAsync(AttachmentHttpClient, url, processed ?? path,
                            converted ? Path.ChangeExtension(name, ".jpg") : name,
                            converted ? "image/jpeg" : contentType, progress, cancellationToken,
                            queued: true).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (processed is not null && processed != path)
                        {
                            try { File.Delete(processed); }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                        }
                    }
                }
            }, parameters).ConfigureAwait(false);
        }
        catch (Exception exception) when (WebApiMessageManager.IsPermanentOperationFailure(exception))
        {
            return (new ErrorReturner(false, errorCode: ErrorReturner.PermanentOutgoingFailure), null);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException
            or UnauthorizedAccessException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure("Messenger_AttachmentReadFailed");
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure();
        }
    }

    internal static async Task<(ErrorReturner error, string? fileId)> ResumeAttachmentAsync(
        HttpClient client, string url, Func<Task<(ErrorReturner error, string? fileId)>> post,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MaxStatusWait);
        using var response = await client.GetAsync(AttachmentStatusUrl(url), timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return UploadHttpFailure(response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<UploadResult>(timeout.Token).ConfigureAwait(false);
        if (status?.State == "pending")
            return await post().ConfigureAwait(false);

        if (status?.State == "processing")
            status = await WaitForAttachmentAsync(client, url, TimeSpan.FromSeconds(Math.Max(1, status.RetryAfterSeconds)),
                TimeProvider.System, cancellationToken).ConfigureAwait(false);

        if (status?.State != "completed" || status.FileId == Guid.Empty)
            return Failure();

        progress?.Report(1);
        return (new ErrorReturner(true), status.FileId.ToString());
    }

    private static Uri AttachmentStatusUrl(string url)
    {
        var builder = new UriBuilder(url);
        builder.Path = builder.Path.TrimEnd('/') + "/status";
        return builder.Uri;
    }

    internal static async Task<(ErrorReturner error, string? fileId)> PostAttachmentAsync(
        HttpClient client, string url, string path, string name, string? contentType,
        IProgress<double>? progress, CancellationToken cancellationToken, TimeProvider? timeProvider = null, bool queued = false)
    {
        var clock = timeProvider ?? TimeProvider.System;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            bufferSize: 128 * 1024, useAsync: true);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var timer = clock.CreateTimer(_ => requestCancellation.Cancel(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var waitingForResponse = false;
        using var form = new MultipartFormDataContent();
        var content = new AttachmentContent(stream, progress,
            () => timer.Change(UploadIdleTimeout, Timeout.InfiniteTimeSpan));
        content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
            ? mediaType : new MediaTypeHeaderValue("application/octet-stream");
        var safeName = new string(Path.GetFileName(name).Where(c => !char.IsControl(c)).ToArray());
        form.Add(content, "file", safeName);
        if (form.Headers.ContentLength > MaxUploadRequestSize)
            return Failure("Messenger_AttachmentTooLarge");

        using var requestContent = new AttachmentRequestContent(form, () =>
        {
            waitingForResponse = true;
            timer.Change(UploadResponseTimeout, Timeout.InfiniteTimeSpan);
        });
        HttpResponseMessage postResponse;
        timer.Change(UploadIdleTimeout, Timeout.InfiniteTimeSpan);
        try
        {
            postResponse = await client.PostAsync(url, requestContent, requestCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException &&
            requestCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Failure(waitingForResponse
                ? "Messenger_AttachmentResponseTimeout" : "Messenger_AttachmentConnectionTimeout");
        }
        finally
        {
            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        using var response = postResponse;
        if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            return Failure("Messenger_AttachmentTooLarge");
        if (response.StatusCode == HttpStatusCode.GatewayTimeout)
            return Failure("Messenger_AttachmentResponseTimeout");

        UploadResult? result;
        if (response.StatusCode == HttpStatusCode.Conflict && response.Headers.RetryAfter is { } retry)
        {
            var delay = retry.Delta ?? retry.Date - clock.GetUtcNow() ?? TimeSpan.FromSeconds(1);
            result = await WaitForAttachmentAsync(client, url, delay, clock, cancellationToken).ConfigureAwait(false);
        }
        else if (response.IsSuccessStatusCode)
        {
            result = await response.Content.ReadFromJsonAsync<UploadResult>(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return queued ? UploadHttpFailure(response.StatusCode) : Failure();
        }

        if (result is null || result.FileId == Guid.Empty)
            return Failure();

        progress?.Report(1);
        return (new ErrorReturner(true), result.FileId.ToString());
    }

    private static async Task<UploadResult?> WaitForAttachmentAsync(
        HttpClient client, string url, TimeSpan delay, TimeProvider clock, CancellationToken cancellationToken)
    {
        var deadline = clock.GetUtcNow() + MaxStatusWait;
        using var deadlineCancellation = new CancellationTokenSource(MaxStatusWait, clock);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineCancellation.Token);
        var statusUrl = AttachmentStatusUrl(url);
        while (clock.GetUtcNow() + delay < deadline)
        {
            await Task.Delay(delay >= TimeSpan.FromSeconds(1) ? delay : TimeSpan.FromSeconds(1), clock, timeout.Token)
                .ConfigureAwait(false);
            using var response = await client.GetAsync(statusUrl, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var status = await response.Content.ReadFromJsonAsync<UploadResult>(timeout.Token).ConfigureAwait(false);
            if (status?.State == "completed")
                return status;
            if (status?.State != "processing")
                return null;

            delay = TimeSpan.FromSeconds(Math.Max(1, status.RetryAfterSeconds));
        }
        return null;
    }

    private static (ErrorReturner, string?) UploadHttpFailure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.InsufficientStorage
            => (new ErrorReturner(false, errorCode: ErrorReturner.PermanentOutgoingFailure), null),
        _ => Failure()
    };

    private static (ErrorReturner, string?) Failure(string key = "Messenger_AttachmentUploadFailed") =>
        (new ErrorReturner(false, errorResourceKey: key), null);

    private sealed record UploadResult(Guid FileId, string? State, int RetryAfterSeconds);

    private sealed class AttachmentRequestContent : HttpContent
    {
        private readonly MultipartFormDataContent _form;
        private readonly Action _complete;

        public AttachmentRequestContent(MultipartFormDataContent form, Action complete)
        {
            _form = form;
            _complete = complete;
            Headers.ContentType = form.Headers.ContentType;
        }

        protected override bool TryComputeLength(out long length)
        {
            var contentLength = _form.Headers.ContentLength;
            length = contentLength.GetValueOrDefault();
            return contentLength.HasValue;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await _form.CopyToAsync(stream, context, cancellationToken).ConfigureAwait(false);
            _complete();
        }
    }

    private sealed class AttachmentContent(Stream stream, IProgress<double>? progress, Action bytesSent) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = stream.Length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) =>
            SerializeToStreamAsync(target, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream target, TransportContext? context, CancellationToken cancellationToken)
        {
            stream.Position = 0;
            var buffer = new byte[128 * 1024];
            var lastPercent = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                bytesSent();
                var percent = (int)(100 * stream.Position / stream.Length);
                if (percent > lastPercent)
                {
                    progress?.Report(percent / 100d);
                    lastPercent = percent;
                }
            }
        }
    }
}
