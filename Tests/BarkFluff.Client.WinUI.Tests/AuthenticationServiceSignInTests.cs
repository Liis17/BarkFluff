using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Text;

using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.WebApi.Core.MessengerData;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class AuthenticationServiceSignInTests
{
    [Fact]
    public async Task AttemptSwitch_CancelsLateChallengeOnOldNodeAndKeepsMetadata()
    {
        await using var oldNode = await TestIdentityNode.StartAsync("old");
        await using var newNode = await TestIdentityNode.StartAsync("new");
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var session = new TestClientSession(CreateConnection(oldNode.Address, "device-old"));
        var store = new TestSessionStore();
        var authentication = new AuthenticationService(mainWebApi, session, store);

        var oldAttempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        var resolution = await authentication.ResolveSignInAsync(oldAttempt, "alice@example.com");
        Assert.True(resolution.IsSuccess);
        var oldChallenge = await authentication.BeginSignInAsync(
            oldAttempt,
            "alice@example.com",
            "password",
            AuthLoginMode.Password,
            OtpTypeId.Unknown,
            false);
        Assert.True(oldChallenge.IsSuccess);
        Assert.Equal("old-challenge", oldChallenge.Challenge!.Reference.Id);
        Assert.True(oldNode.BeginRequests.TryPeek(out var beginRequest));
        Assert.True(beginRequest.ReportLoginModeMismatch);
        Assert.True(oldNode.RequestMetadata.TryPeek(out var headers));
        Assert.Equal(Environment.MachineName, DecodeHeader(headers, "x-device-name"));
        Assert.Equal("device-old", DecodeHeader(headers, "x-device-id"));
        Assert.False(string.IsNullOrWhiteSpace(DecodeHeader(headers, "x-os-name")));
        Assert.False(string.IsNullOrWhiteSpace(DecodeHeader(headers, "x-app-name")));

        session.SetConnection(CreateConnection(newNode.Address, "device-new"));
        var newAttempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        await oldNode.CancelledChallengeSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("old-challenge", Assert.Single(oldNode.CancelledReferences).Id);
        Assert.Empty(newNode.CancelledReferences);
        var newResolution = await authentication.ResolveSignInAsync(newAttempt, "bob");
        Assert.True(newResolution.IsSuccess);
        Assert.Equal("bob", Assert.Single(newNode.ResolvedLogins));

        await authentication.CancelAuthenticationAttemptAsync(newAttempt);
    }

    [Fact]
    public async Task ReplacingAttemptDuringProfileLoad_CancelsProfileCallAndNeverPersistsTokens()
    {
        await using var node = await TestIdentityNode.StartAsync("profile", blockProfile: true);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var connection = CreateConnection(node.Address, "device-profile");
        var session = new TestClientSession(connection);
        var store = new TestSessionStore { Value = new StoredSession("previous-access", "previous-refresh") };
        var authentication = new AuthenticationService(mainWebApi, session, store);
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        var reference = await BeginChallengeAsync(authentication, attempt);

        var completion = authentication.CompleteAuthChallengeAsync(attempt, reference, string.Empty, false);
        await node.ProfileRequestSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var replacement = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        node.ReleaseProfile();

        var result = await completion;
        Assert.False(result.IsSuccess);
        Assert.Equal("Error_LoginUnavailable", result.ErrorResourceKey);
        Assert.Equal(new StoredSession("previous-access", "previous-refresh"), store.Value);
        Assert.Null(connection.ConnectionParameters.AccessToken);
        Assert.Null(connection.ConnectionParameters.RefreshToken);
        await authentication.CancelAuthenticationAttemptAsync(replacement);
    }

    [Fact]
    public async Task AttemptReplacedDuringStartedSave_RestoresPreviousSnapshotBeforeNewAttemptCommits()
    {
        await using var oldNode = await TestIdentityNode.StartAsync("old-save");
        await using var newNode = await TestIdentityNode.StartAsync("new-save");
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var oldConnection = CreateConnection(oldNode.Address, "device-old");
        var newConnection = CreateConnection(newNode.Address, "device-new");
        var session = new TestClientSession(oldConnection);
        var store = new TestSessionStore
        {
            Value = new StoredSession("previous-access", "previous-refresh"),
            BlockFirstSave = true
        };
        var authentication = new AuthenticationService(mainWebApi, session, store);
        var oldAttempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        var oldReference = await BeginChallengeAsync(authentication, oldAttempt);

        var oldCompletion = authentication.CompleteAuthChallengeAsync(oldAttempt, oldReference, string.Empty, false);
        var firstSaveOrCompletion = await Task.WhenAny(store.FirstSaveStarted.Task, oldCompletion);
        if (firstSaveOrCompletion == oldCompletion)
        {
            var earlyResult = await oldCompletion;
            Assert.Fail($"Authentication ended before persistence started: {earlyResult.ErrorResourceKey}");
        }

        await store.FirstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        session.SetConnection(newConnection);
        var newAttempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        store.ReleaseFirstSave();
        var staleResult = await oldCompletion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(staleResult.IsSuccess);
        Assert.Equal("Error_LoginUnavailable", staleResult.ErrorResourceKey);

        var newReference = await BeginChallengeAsync(authentication, newAttempt);
        var newResult = await authentication.CompleteAuthChallengeAsync(newAttempt, newReference, string.Empty, false);

        Assert.True(newResult.IsSuccess);
        Assert.Equal(new StoredSession("new-save-access", "new-save-refresh"), store.Value);
        Assert.Equal("new-save-access", newConnection.ConnectionParameters.AccessToken?.Value);
        Assert.Null(oldConnection.ConnectionParameters.AccessToken);
        await oldNode.CancelledChallengeSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("old-save-challenge", Assert.Single(oldNode.CancelledReferences).Id);
    }

    [Fact]
    public async Task ResolveUnimplemented_ReportsServerUpdateRequiredWithoutLegacyFallback()
    {
        await using var node = await TestIdentityNode.StartAsync("old-api", unimplementedResolve: true);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var session = new TestClientSession(CreateConnection(node.Address, "device"));
        var authentication = new AuthenticationService(mainWebApi, session, new TestSessionStore());
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());

        var result = await authentication.ResolveSignInAsync(attempt, "alice");

        Assert.False(result.IsSuccess);
        Assert.Equal("Error_LoginUpdateRequired", result.ErrorResourceKey);
        Assert.Empty(node.ResolvedLogins);
        await authentication.CancelAuthenticationAttemptAsync(attempt);
    }

    [Fact]
    public async Task ResolveUnspecifiedMode_IsTreatedAsUnknownAccount()
    {
        await using var node = await TestIdentityNode.StartAsync(
            "unspecified-mode",
            resolveLoginMode: AuthLoginMode.LoginModeUnspecified);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var session = new TestClientSession(CreateConnection(node.Address, "device"));
        var authentication = new AuthenticationService(mainWebApi, session, new TestSessionStore());
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());

        var result = await authentication.ResolveSignInAsync(attempt, "alice");

        Assert.False(result.IsSuccess);
        Assert.Equal("account_not_found", result.ErrorCode);
        Assert.Equal("Error_LoginAccountNotFound", result.ErrorResourceKey);
        await authentication.CancelAuthenticationAttemptAsync(attempt);
    }

    private static async Task<AuthChallengeReference> BeginChallengeAsync(
        AuthenticationService authentication,
        AuthenticationAttempt attempt)
    {
        var resolution = await authentication.ResolveSignInAsync(attempt, "alice");
        Assert.True(resolution.IsSuccess);
        var begin = await authentication.BeginSignInAsync(
            attempt,
            "alice",
            "password",
            resolution.LoginMode,
            OtpTypeId.Unknown,
            false);
        Assert.True(begin.IsSuccess);
        return begin.Challenge!.Reference;
    }

    private static NodeConnection CreateConnection(string address, string deviceId)
    {
        var parameters = new GlobalParam
        {
            SocketBeacon = address,
            SocketUsers = address,
            SocketIdentity = address,
            SocketFiles = address,
            SocketMessages = address,
            SocketUpdates = address,
            SocketOnliner = address,
            SocketFastAuth = address,
            DeviceId = deviceId
        };
        return new NodeConnection(new NodeProfile(address, "Test node", "Local gRPC fixture"), parameters);
    }

    private static string DecodeHeader(IReadOnlyDictionary<string, string> headers, string name) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(headers[name]));

    private sealed class TestClientSession(NodeConnection connection) : IClientSession
    {
        public NodeConnection? CurrentConnection { get; private set; } = connection;
        public void SetConnection(NodeConnection value) => CurrentConnection = value;
    }

    private sealed class TestSessionStore : ISecureSessionStore
    {
        private int _firstSaveClaimed;
        private readonly TaskCompletionSource _releaseFirstSave = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StoredSession? Value { get; set; }
        public bool BlockFirstSave { get; init; }
        public TaskCompletionSource FirstSaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);

        public async Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default)
        {
            if (BlockFirstSave && Interlocked.Exchange(ref _firstSaveClaimed, 1) == 0)
            {
                FirstSaveStarted.TrySetResult();
                await _releaseFirstSave.Task;
            }

            Value = session;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Value = null;
            return Task.CompletedTask;
        }

        public void ReleaseFirstSave() => _releaseFirstSave.TrySetResult();
    }

    private sealed class TestIdentityNode : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private readonly TaskCompletionSource _releaseProfile = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TestIdentityNode(WebApplication application, string address, string name)
        {
            _application = application;
            Address = address;
            Name = name;
        }

        public string Address { get; }
        public string Name { get; }
        public bool BlockProfile { get; init; }
        public bool UnimplementedResolve { get; init; }
        public AuthLoginMode ResolveLoginMode { get; init; } = AuthLoginMode.Password;
        public ConcurrentQueue<string> ResolvedLogins { get; } = new();
        public ConcurrentQueue<BeginSignInRequest> BeginRequests { get; } = new();
        public ConcurrentQueue<AuthChallengeReference> CancelledReferences { get; } = new();
        public ConcurrentQueue<IReadOnlyDictionary<string, string>> RequestMetadata { get; } = new();
        public TaskCompletionSource CancelledChallengeSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProfileRequestSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<TestIdentityNode> StartAsync(
            string name,
            bool blockProfile = false,
            bool unimplementedResolve = false,
            AuthLoginMode resolveLoginMode = AuthLoginMode.Password)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listenOptions =>
                listenOptions.Protocols = HttpProtocols.Http2));
            var application = builder.Build();
            TestIdentityNode? node = null;

            application.MapPost("/barkfluff.identity.IdentityApi/ResolveSignIn", async context =>
            {
                var payload = await ReadGrpcPayloadAsync(context);
                var request = ResolveSignInRequest.Parser.ParseFrom(payload);
                if (node!.UnimplementedResolve)
                {
                    await WriteGrpcResponseAsync(context, new ResolveSignInResponse(), "12");
                    return;
                }

                node.ResolvedLogins.Enqueue(request.Login);
                node.RequestMetadata.Enqueue(CaptureHeaders(context));
                await WriteGrpcResponseAsync(context, new ResolveSignInResponse { LoginMode = node.ResolveLoginMode });
            });

            application.MapPost("/barkfluff.identity.IdentityApi/BeginSignIn", async context =>
            {
                var payload = await ReadGrpcPayloadAsync(context);
                var request = BeginSignInRequest.Parser.ParseFrom(payload);
                node!.BeginRequests.Enqueue(request);
                node.RequestMetadata.Enqueue(CaptureHeaders(context));
                await WriteGrpcResponseAsync(context, new AuthChallengeResponse
                {
                    Challenge = new AuthChallengeReference { Id = $"{node.Name}-challenge", Secret = "challenge-secret" },
                    State = AuthChallengeState.Waiting,
                    Factor = request.Factor,
                    NeedsCode = false
                });
            });

            application.MapPost("/barkfluff.identity.IdentityApi/CancelAuthChallenge", async context =>
            {
                var payload = await ReadGrpcPayloadAsync(context);
                var reference = AuthChallengeReference.Parser.ParseFrom(payload);
                node!.CancelledReferences.Enqueue(reference);
                node.CancelledChallengeSeen.TrySetResult();
                await WriteGrpcResponseAsync(context, new AuthChallengeResponse
                {
                    Challenge = reference,
                    State = AuthChallengeState.Cancelled
                });
            });

            application.MapPost("/barkfluff.identity.IdentityApi/CompleteAuthChallenge", async context =>
            {
                _ = await ReadGrpcPayloadAsync(context);
                await WriteGrpcResponseAsync(context, new CompleteAuthChallengeResponse
                {
                    State = AuthChallengeState.Completed,
                    Session = new AuthResponse
                    {
                        AccessToken = new Token { Value = $"{node!.Name}-access" },
                        RefreshToken = new Token { Value = $"{node.Name}-refresh" }
                    }
                });
            });

            application.MapPost("/barkfluff.users.UsersApi/GetUser", async context =>
            {
                _ = await ReadGrpcPayloadAsync(context);
                node!.ProfileRequestSeen.TrySetResult();
                if (node.BlockProfile)
                {
                    try
                    {
                        await node._releaseProfile.Task.WaitAsync(context.RequestAborted);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                await WriteGrpcResponseAsync(context, new GetUserResponse
                {
                    User = new User
                    {
                        Id = 42,
                        Username = node.Name,
                        FirstName = "Test",
                        RegistrationDate = Timestamp.FromDateTime(DateTime.UtcNow)
                    }
                });
            });

            await application.StartAsync();
            node = new TestIdentityNode(application, application.Urls.Single(), name)
            {
                BlockProfile = blockProfile,
                UnimplementedResolve = unimplementedResolve,
                ResolveLoginMode = resolveLoginMode
            };
            return node;
        }

        public void ReleaseProfile() => _releaseProfile.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            _releaseProfile.TrySetResult();
            await _application.DisposeAsync();
        }

        private static IReadOnlyDictionary<string, string> CaptureHeaders(HttpContext context) =>
            context.Request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        private static async Task<byte[]> ReadGrpcPayloadAsync(HttpContext context)
        {
            var header = new byte[5];
            await ReadExactlyAsync(context.Request.Body, header, context.RequestAborted);
            var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
            var payload = new byte[length];
            await ReadExactlyAsync(context.Request.Body, payload, context.RequestAborted);
            return payload;
        }

        private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("Incomplete gRPC frame.");
                }

                offset += read;
            }
        }

        private static async Task WriteGrpcResponseAsync(HttpContext context, IMessage response, string grpcStatus = "0")
        {
            var payload = response.ToByteArray();
            var frame = new byte[payload.Length + 5];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), payload.Length);
            payload.CopyTo(frame, 5);
            context.Response.ContentType = "application/grpc";
            context.Response.DeclareTrailer("grpc-status");
            await context.Response.Body.WriteAsync(frame, context.RequestAborted);
            context.Response.AppendTrailer("grpc-status", grpcStatus);
        }
    }
}
