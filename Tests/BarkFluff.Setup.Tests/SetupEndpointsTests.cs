using BarkFluff.Proto.SettingsSetup;
using BarkFluff.Setup.Endpoints;
using BarkFluff.Setup.Setup;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Json;

using Xunit;

namespace BarkFluff.Setup.Tests;

public sealed class SetupEndpointsTests
{
    [Fact]
    public async Task Setup_mutations_require_the_session_csrf_token_and_forward_values()
    {
        var options = new SetupOptions(
            7032,
            new Uri("http://settings:7003"),
            "a-test-setup-token-with-enough-entropy",
            null,
            TimeSpan.FromHours(1));
        var fake = new FakeSettingsSetupClient();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<SetupSessionStore>();
        builder.Services.AddSingleton<ISettingsSetupClient>(fake);

        await using var app = builder.Build();
        app.MapSetupEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var login = await client.PostAsJsonAsync("/api/session", new { token = options.Secret });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<LoginResponse>();
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var withoutCsrf = new HttpRequestMessage(HttpMethod.Put, "/api/setup/groups/server")
        {
            Content = JsonContent.Create(new { values = new Dictionary<string, string> { ["1:ServerProps:Name"] = "Home" } })
        };
        withoutCsrf.Headers.Add("Cookie", cookie);
        var forbidden = await client.SendAsync(withoutCsrf);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/setup/groups/server")
        {
            Content = JsonContent.Create(new { values = new Dictionary<string, string> { ["1:ServerProps:Name"] = "Home" } })
        };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("X-CSRF-Token", loginBody!.CsrfToken);
        request.Headers.Add("Origin", "http://localhost");
        var saved = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("server", fake.LastGroupId);
        Assert.Equal("Home", fake.LastValues!["1:ServerProps:Name"]);
    }

    private sealed record LoginResponse(string CsrfToken, DateTimeOffset ExpiresAtUtc);

    private sealed class FakeSettingsSetupClient : ISettingsSetupClient
    {
        public string? LastGroupId { get; private set; }
        public IReadOnlyDictionary<string, string?>? LastValues { get; private set; }

        public Task<GetSetupStateResponse> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GetSetupStateResponse());

        public Task<SaveSetupGroupResponse> SaveGroupAsync(
            string groupId,
            IReadOnlyDictionary<string, string?> values,
            string editedFrom,
            CancellationToken cancellationToken = default)
        {
            LastGroupId = groupId;
            LastValues = values;
            return Task.FromResult(new SaveSetupGroupResponse { Success = true, State = new GetSetupStateResponse() });
        }

        public Task<CompleteSetupResponse> CompleteAsync(string completedFrom, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompleteSetupResponse { Success = true, State = new GetSetupStateResponse() });
    }
}
