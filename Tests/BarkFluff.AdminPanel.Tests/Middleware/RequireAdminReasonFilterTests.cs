using Barkfluff.AdminPanel.Middleware;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

using System.Net;
using System.Net.Http.Json;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Middleware;

public sealed class RequireAdminReasonFilterTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public RequireAdminReasonFilterTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.MapPost("/action", (ReasonPayload payload) => Results.Ok(new { payload.Reason }))
            .RequireAdminReasonFromArguments(context =>
                context.Arguments.OfType<ReasonPayload>().FirstOrDefault()?.Reason);

        _app.StartAsync().GetAwaiter().GetResult();
        _client = _app.GetTestClient();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingReason_IsRejectedBeforeAction(string? reason)
    {
        var response = await _client.PostAsJsonAsync("/action", new ReasonPayload(reason));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("Укажите причину действия", body!["message"]);
    }

    [Fact]
    public async Task ReasonLongerThan500Characters_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/action", new ReasonPayload(new string('x', 501)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("Причина не должна превышать 500 символов", body!["message"]);
    }

    [Fact]
    public async Task NonEmptyReason_AllowsAction()
    {
        var response = await _client.PostAsJsonAsync("/action", new ReasonPayload("Запрос владельца аккаунта"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private sealed record ReasonPayload(string? Reason);
}
