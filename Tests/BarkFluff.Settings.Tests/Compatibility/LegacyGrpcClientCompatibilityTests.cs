using System.Collections;
using System.Reflection;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Settings.Host;
using BarkFluff.Settings.Infrastructure;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Settings.Persistence.Services;
using BarkFluff.Shared.Identity;

using Grpc.Core;
using Grpc.Net.Client;

using MediatR;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace BarkFluff.Settings.Tests.Compatibility;

public sealed class LegacyGrpcClientCompatibilityTests
{
    [Fact]
    public async Task Existing_generated_client_loads_settings_from_the_new_server()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(new MetricsCollector());
        builder.Services.AddDbContext<SettingsContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<SettingsStorage>();
        builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<SettingsApiService>());
        await using var server = builder.Build();
        server.MapGrpcService<SettingsApiService>();
        await server.StartAsync();

        await using (var scope = server.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettingsContext>();
            await new SettingsSeeder(context, SettingsSeedOptions.ForTests()).SeedAsync();
        }

        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = server.GetTestServer().CreateHandler() });
        var clientAssembly = typeof(MetricsCollector).Assembly;
        var clientType = clientAssembly.GetType("BarkFluff.Proto.Configuration.ConfigurationApi+ConfigurationApiClient", true)!;
        var requestType = clientAssembly.GetType("BarkFluff.Proto.Configuration.GetConfigurationRequest", true)!;
        var client = Activator.CreateInstance(clientType, channel.CreateCallInvoker())!;
        var request = Activator.CreateInstance(requestType)!;
        requestType.GetProperty("ServiceId")!.SetValue(request, (int)ServiceId.Users);
        var method = clientType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate => candidate.Name == "GetConfiguration" && candidate.GetParameters().Length == 4);

        var response = method.Invoke(client, [request, null, null, CancellationToken.None])!;
        var configurations = (ICollection)response.GetType().GetProperty("Configurations")!.GetValue(response)!;

        Assert.True(configurations.Count > 0);
    }
}
