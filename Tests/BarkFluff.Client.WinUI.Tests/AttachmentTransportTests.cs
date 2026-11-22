using System.Net;
using System.Net.Http.Headers;
using BarkFluff.WebApi.Core.Managers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Time.Testing;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class AttachmentTransportTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"barkfluff-test-{Guid.NewGuid():N}");

    public AttachmentTransportTests() => File.WriteAllBytes(_path, new byte[256 * 1024]);

    public void Dispose() => File.Delete(_path);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnsDeduplicatedIdAndPreservesUnicodeName(bool conflict)
    {
        var reservedId = Guid.NewGuid();
        var deduplicatedId = Guid.NewGuid();
        var url = $"https://example.invalid/upload/{reservedId}";
        var reports = new List<double>();
        var requests = new List<HttpMethod>();
        var clock = new StatusClock();
        using var client = new HttpClient(new TestHandler(async (request, token) =>
        {
            requests.Add(request.Method);
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal(url + "/status", request.RequestUri!.AbsoluteUri);
                return new(HttpStatusCode.OK) { Content = new StringContent(
                    $$"""{"fileId":"{{deduplicatedId}}","state":"completed","retryAfterSeconds":0}""") };
            }

            using var payload = new MemoryStream();
            var mediaType = request.Content!.Headers.ContentType!;
            Assert.Equal("multipart/form-data", mediaType.MediaType);
            var boundary = Assert.Single(mediaType.Parameters, parameter => parameter.Name == "boundary").Value!;
            await request.Content.CopyToAsync(payload, token);
            payload.Position = 0;
            var reader = new MultipartReader(boundary.Trim('"'), payload);
            var part = Assert.IsType<MultipartSection>(await reader.ReadNextSectionAsync(token));
            Assert.Equal("отчёт.gif", ContentDispositionHeaderValue.Parse(part.ContentDisposition!).FileNameStar);
            Assert.Equal("image/gif", part.ContentType);
            using var file = new MemoryStream();
            await part.Body.CopyToAsync(file, token);
            Assert.Equal(await File.ReadAllBytesAsync(_path, token), file.ToArray());
            Assert.Null(await reader.ReadNextSectionAsync(token));
            if (conflict)
            {
                var response = new HttpResponseMessage(HttpStatusCode.Conflict);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return response;
            }

            return new(HttpStatusCode.OK) { Content = new StringContent($$"""{"fileId":"{{deduplicatedId}}"}""") };
        }));

        var upload = WebApiFileManager.PostAttachmentAsync(client, url, _path, "отчёт.gif", "image/gif",
            new TestProgress(reports), CancellationToken.None, clock);
        if (conflict)
        {
            await clock.PollScheduled.Task;
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        var result = await upload;

        Assert.True(result.error.IsSuccess);
        Assert.Equal(deduplicatedId.ToString(), result.fileId);
        Assert.Equal(conflict ? new[] { HttpMethod.Post, HttpMethod.Get } : [HttpMethod.Post], requests);
        Assert.Contains(0.5, reports);
        Assert.Equal(1, reports[^1]);
    }

    [Fact]
    public async Task UploadStallReturnsConnectionTimeout()
    {
        var clock = new FakeTimeProvider();
        using var client = new HttpClient(new TestHandler(async (request, token) =>
        {
            using var payload = new AdvancingStream(bytes =>
            {
                if (bytes == 128 * 1024)
                    clock.Advance(TimeSpan.FromSeconds(61));
            });
            await request.Content!.CopyToAsync(payload, token);
            return Success();
        }));

        var result = await WebApiFileManager.PostAttachmentAsync(client, "https://example.invalid/upload", _path,
            "файл.txt", null, null, CancellationToken.None, clock);

        Assert.Equal("Messenger_AttachmentConnectionTimeout", result.error.ErrorResourceKey);
        Assert.Null(result.fileId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowUploadKeepsItsConnectionAndGetsSeparateResponseBudget(bool responseTimesOut)
    {
        var clock = new FakeTimeProvider();
        using (var file = File.OpenWrite(_path))
            file.SetLength(2 * 1024 * 1024);
        using var client = new HttpClient(new TestHandler(async (request, token) =>
        {
            using var payload = new AdvancingStream(bytes =>
            {
                if (bytes == 128 * 1024)
                    clock.Advance(TimeSpan.FromSeconds(40));
            });
            await request.Content!.CopyToAsync(payload, token);
            Assert.True(clock.GetUtcNow() - clock.Start > TimeSpan.FromMinutes(5));
            token.ThrowIfCancellationRequested();

            clock.Advance(TimeSpan.FromSeconds(responseTimesOut ? 631 : 599));
            token.ThrowIfCancellationRequested();
            return Success();
        })) { Timeout = Timeout.InfiniteTimeSpan };

        var result = await WebApiFileManager.PostAttachmentAsync(client, "https://example.invalid/upload", _path,
            "файл.txt", null, null, CancellationToken.None, clock);

        Assert.Equal(!responseTimesOut, result.error.IsSuccess);
        if (responseTimesOut)
            Assert.Equal("Messenger_AttachmentResponseTimeout", result.error.ErrorResourceKey);
        else
            Assert.NotNull(result.fileId);
    }

    private static HttpResponseMessage Success() => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$"""{"fileId":"{{Guid.NewGuid()}}"}""")
    };

    private sealed class AdvancingStream(Action<int> advance) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            advance(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class StatusClock : FakeTimeProvider
    {
        public TaskCompletionSource PollScheduled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime == TimeSpan.FromSeconds(1))
                PollScheduled.TrySetResult();
            return timer;
        }
    }

    [Fact]
    public async Task CancellationReachesHttpRequest()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new TestHandler(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }));

        var upload = WebApiFileManager.PostAttachmentAsync(client, "https://example.invalid/upload", _path,
            "файл.txt", null, null, cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upload);
    }

    private sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class TestProgress(List<double> reports) : IProgress<double>
    {
        public void Report(double value) => reports.Add(value);
    }
}
