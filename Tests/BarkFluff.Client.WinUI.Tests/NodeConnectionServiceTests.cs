using System.Net;
using System.Buffers.Binary;

using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Beacon;
using BarkFluff.WebApi.Core.MessengerData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Google.Protobuf;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class NodeConnectionServiceTests
{
    [Fact]
    public async Task ConnectAsync_AfterPreviousNodeStops_ConnectsToNewNode()
    {
        await using var firstNode = await TestBeaconNode.StartAsync("First node");
        using var webApi = new BarkFluff.WebApi.Core.WebApi();
        var service = new NodeConnectionService(webApi, new NodeAddressParser(), new TestClientSession());

        var firstResult = await service.ConnectAsync(firstNode.Address);
        Assert.True(firstResult.IsSuccess);

        await firstNode.StopAsync();

        await using var secondNode = await TestBeaconNode.StartAsync("Second node");
        var secondResult = await service.ConnectAsync(secondNode.Address);

        Assert.True(secondResult.IsSuccess);
        Assert.Equal("Second node", secondResult.Connection!.Profile.Name);
    }

    private sealed class TestClientSession : IClientSession
    {
        public NodeConnection? CurrentConnection { get; private set; }

        public void SetConnection(NodeConnection connection) => CurrentConnection = connection;
    }

    private sealed class TestBeaconNode : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private TestBeaconNode(WebApplication application, string address)
        {
            _application = application;
            Address = address;
        }

        public string Address { get; }

        public static async Task<TestBeaconNode> StartAsync(string name)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listenOptions =>
                listenOptions.Protocols = HttpProtocols.Http2));

            var application = builder.Build();
            var responseFactory = new GetServerInfoResponseFactory(name);
            application.MapPost("/barkfluff.beacon.BeaconApi/GetServerInfo", context =>
                WriteGrpcResponseAsync(context, responseFactory.Create()));
            await application.StartAsync();

            return new TestBeaconNode(application, application.Urls.Single());
        }

        public Task StopAsync() => _application.StopAsync();

        public async ValueTask DisposeAsync()
        {
            await _application.DisposeAsync();
        }

        private static async Task WriteGrpcResponseAsync(HttpContext context, GetServerInfoResponse response)
        {
            var payload = response.ToByteArray();
            var framedResponse = new byte[payload.Length + 5];
            BinaryPrimitives.WriteInt32BigEndian(framedResponse.AsSpan(1, 4), payload.Length);
            payload.CopyTo(framedResponse, 5);

            context.Response.ContentType = "application/grpc";
            context.Response.DeclareTrailer("grpc-status");
            await context.Response.Body.WriteAsync(framedResponse);
            context.Response.AppendTrailer("grpc-status", "0");
        }
    }

    private sealed class GetServerInfoResponseFactory
    {
        private readonly string _name;

        public GetServerInfoResponseFactory(string name)
        {
            _name = name;
        }

        public GetServerInfoResponse Create()
        {
            return new GetServerInfoResponse
            {
                Name = _name,
                Description = "Test node",
                Identity = CreateService(),
                Users = CreateService(),
                Files = CreateService(),
                Messages = CreateService(),
                Updates = CreateService(),
                Onliner = CreateService(),
                FastAuth = CreateService()
            };
        }

        private static Service CreateService() => new()
        {
            Endpoint = new ServiceEndpoint { Host = "127.0.0.1", Port = 1 },
            TlsEnabled = false
        };
    }
}
