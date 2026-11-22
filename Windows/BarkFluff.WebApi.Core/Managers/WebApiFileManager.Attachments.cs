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
            if (type == UploadFileType.MessageAttachmentImage)
                processed = await ImageProcessor.ProcessImageForUploadAsync(path).ConfigureAwait(false);

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

    internal static async Task<(ErrorReturner error, string? fileId)> PostAttachmentAsync(
        HttpClient client, string url, string path, string name, string? contentType,
        IProgress<double>? progress, CancellationToken cancellationToken, TimeProvider? timeProvider = null)
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
            return Failure();
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
        var statusUrl = new UriBuilder(url);
        statusUrl.Path = statusUrl.Path.TrimEnd('/') + "/status";
        while (clock.GetUtcNow() + delay < deadline)
        {
            await Task.Delay(delay >= TimeSpan.FromSeconds(1) ? delay : TimeSpan.FromSeconds(1), clock, timeout.Token)
                .ConfigureAwait(false);
            using var response = await client.GetAsync(statusUrl.Uri, timeout.Token).ConfigureAwait(false);
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
