using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Developers;
using BarkFluff.Shared.Exceptions;
using BarkFluff.Shared.Identity;
using Barkfluff.Developers.Domain;
using Barkfluff.Developers.Host;
using Barkfluff.Developers.Infrastructure;
using Barkfluff.Developers.Persistence.Contexts;
using Barkfluff.Developers.Persistence.Services;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Barkfluff.Developers.Tests;

public class StartupAndAuthorizationTests
{
    [Fact]
    public async Task Startup_fails_when_a_published_physical_proto_is_missing()
    {
        await using var services = TestInfrastructure.CreateInitializerProvider(
            Guid.NewGuid().ToString(),
            new TestInfrastructure.TestPublishedProtoCatalog(["shared.proto"]));
        var initializer = services.GetRequiredService<DevelopersStartupInitializer>();

        var action = () => initializer.InitializeAsync();

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("shared.proto");
    }

    [Fact]
    public async Task Startup_fails_when_database_contains_duplicate_error_codes()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var context = TestInfrastructure.CreateContext(databaseName))
        {
            context.ErrorCodes.AddRange(
                new Barkfluff.Developers.Domain.ErrorCodeEntry { Id = Guid.NewGuid(), Code = "duplicate", ExceptionName = "First" },
                new Barkfluff.Developers.Domain.ErrorCodeEntry { Id = Guid.NewGuid(), Code = "duplicate", ExceptionName = "Second" });
            await context.SaveChangesAsync();
        }

        await using var services = TestInfrastructure.CreateInitializerProvider(databaseName);
        var initializer = services.GetRequiredService<DevelopersStartupInitializer>();

        var action = () => initializer.InitializeAsync();

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("duplicate error codes");
    }

    [Fact]
    public void Error_code_discovery_rejects_exception_without_parameterless_constructor()
    {
        var action = () => ErrorCodeSeeder.DiscoverEntries([typeof(MissingParameterlessException)]);

        var exception = action.Should().Throw<InvalidOperationException>();
        exception.Which.Message.Should().Contain("parameterless constructor");
    }

    [Fact]
    public async Task Startup_fails_when_existing_documentation_contains_invalid_json()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var context = TestInfrastructure.CreateContext(databaseName))
        {
            context.DocumentationSections.Add(TestInfrastructure.Documentation("overview", "{invalid"));
            await context.SaveChangesAsync();
        }

        await using var services = TestInfrastructure.CreateInitializerProvider(databaseName);
        var initializer = services.GetRequiredService<DevelopersStartupInitializer>();

        var action = () => initializer.InitializeAsync();

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("documentation section").And.Contain("overview");
    }

    [Fact]
    public async Task User_jwt_is_accepted_but_service_and_anonymous_principals_are_rejected()
    {
        using var services = new ServiceCollection()
            .AddDevelopersAuthorization()
            .AddLogging()
            .BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();

        var user = Principal(TokenType.User);
        var service = Principal(TokenType.Service);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        (await authorization.AuthorizeAsync(user, null, DevelopersAuthorizationExtensions.ReaderPolicyName))
            .Succeeded.Should().BeTrue();
        (await authorization.AuthorizeAsync(service, null, DevelopersAuthorizationExtensions.ReaderPolicyName))
            .Succeeded.Should().BeFalse();
        (await authorization.AuthorizeAsync(anonymous, null, DevelopersAuthorizationExtensions.ReaderPolicyName))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Grpc_pipeline_accepts_user_jwt_and_rejects_service_jwt_and_anonymous_calls()
    {
        await using var host = await DevelopersGrpcHost.StartAsync();

        var userHeaders = new Metadata { { "x-auth-token", CreateJwt(TokenType.User) } };
        var userResponse = await host.Client.GetErrorCodesAsync(new GetErrorCodesRequest(), userHeaders);
        userResponse.Should().NotBeNull();

        var serviceAction = () => host.Client.GetErrorCodesAsync(
            new GetErrorCodesRequest(),
            new Metadata { { "x-auth-token", CreateJwt(TokenType.Service) } }).ResponseAsync;
        var serviceException = await serviceAction.Should().ThrowAsync<RpcException>();
        serviceException.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);

        var anonymousAction = () => host.Client.GetErrorCodesAsync(new GetErrorCodesRequest()).ResponseAsync;
        var anonymousException = await anonymousAction.Should().ThrowAsync<RpcException>();
        anonymousException.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    private static ClaimsPrincipal Principal(TokenType tokenType)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(IdentityClaims.TokenType, tokenType.ToString())],
            authenticationType: "test"));
    }

    private static string CreateJwt(TokenType tokenType)
    {
        const string secret = "developers-test-secret-key-that-is-long-enough";
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(IdentityClaims.TokenType, tokenType.ToString())
        };
        if (tokenType == TokenType.User)
        {
            claims.Add(new Claim(IdentityClaims.UserId, "42"));
            claims.Add(new Claim(IdentityClaims.DeviceId, "developers-test-device"));
        }

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: DevelopersGrpcHost.Issuer,
            audience: DevelopersGrpcHost.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials));
    }

    private sealed class MissingParameterlessException : BaseGrpcException
    {
        public MissingParameterlessException(string reason)
        {
            _ = reason;
        }
    }

    private sealed class DevelopersGrpcHost : IAsyncDisposable
    {
        internal const string Issuer = "developers-tests";
        internal const string Audience = "developers-tests";
        private const string Secret = "developers-test-secret-key-that-is-long-enough";

        private readonly IHost _host;
        private readonly GrpcChannel _channel;

        private DevelopersGrpcHost(IHost host, GrpcChannel channel)
        {
            _host = host;
            _channel = channel;
            Client = new DevelopersApiTestClient(channel.CreateCallInvoker());
        }

        public DevelopersApiTestClient Client { get; }

        public static async Task<DevelopersGrpcHost> StartAsync()
        {
            var databaseName = Guid.NewGuid().ToString();
            var hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureAppConfiguration(configuration =>
                    {
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["JwtSettings:SecretKey"] = Secret,
                            ["JwtSettings:Issuer"] = Issuer,
                            ["JwtSettings:Audience"] = Audience
                        });
                    });
                    webHost.ConfigureServices((context, services) =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddGrpc();
                        services.AddXAuth(context.Configuration);
                        services.AddDevelopersAuthorization();
                        services.AddMediatR(configuration =>
                            configuration.RegisterServicesFromAssemblyContaining<Barkfluff.Developers.Program>());
                        services.AddDbContext<DevelopersContext>(options =>
                            options.UseInMemoryDatabase(databaseName));
                        services.AddTransient<DocumentationStorage>();
                        services.AddTransient<ProtoMetadataStorage>();
                        services.AddSingleton<IPublishedProtoCatalog>(
                            new TestInfrastructure.TestPublishedProtoCatalog());
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapGrpcService<DevelopersApiService>());
                    });
                });

            var host = await hostBuilder.StartAsync();
            var server = host.GetTestServer();
            var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpHandler = server.CreateHandler()
            });
            return new DevelopersGrpcHost(host, channel);
        }

        public async ValueTask DisposeAsync()
        {
            _channel.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    private sealed class DevelopersApiTestClient
    {
        private static readonly Marshaller<GetErrorCodesRequest> RequestMarshaller = Marshallers.Create(
            static (message, context) => context.Complete(message.ToByteArray()),
            static context => GetErrorCodesRequest.Parser.ParseFrom(context.PayloadAsNewBuffer()));

        private static readonly Marshaller<GetErrorCodesResponse> ResponseMarshaller = Marshallers.Create(
            static (message, context) => context.Complete(message.ToByteArray()),
            static context => GetErrorCodesResponse.Parser.ParseFrom(context.PayloadAsNewBuffer()));

        private static readonly Method<GetErrorCodesRequest, GetErrorCodesResponse> GetErrorCodesMethod = new(
            MethodType.Unary,
            "barkfluff.developers.DevelopersApi",
            "GetErrorCodes",
            RequestMarshaller,
            ResponseMarshaller);

        private readonly CallInvoker _callInvoker;

        public DevelopersApiTestClient(CallInvoker callInvoker)
        {
            _callInvoker = callInvoker;
        }

        public AsyncUnaryCall<GetErrorCodesResponse> GetErrorCodesAsync(
            GetErrorCodesRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return _callInvoker.AsyncUnaryCall(
                GetErrorCodesMethod,
                host: null,
                new CallOptions(headers, deadline, cancellationToken),
                request);
        }
    }
}
