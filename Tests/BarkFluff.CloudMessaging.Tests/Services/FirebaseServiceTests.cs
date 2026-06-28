using Barkfluff.CloudMessaging.Services;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Configuration;
using Moq;

namespace BarkFluff.CloudMessaging.Tests.Services;

public class FirebaseServiceTests : IDisposable
{
    private readonly Mock<ILogger<FirebaseService>> _loggerMock = new();

    private FirebaseService CreateServiceWithConfig(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config!)
            .Build();
        return new FirebaseService(_loggerMock.Object, configuration);
    }

    private static Dictionary<string, string?> FullFirebaseConfig() => new()
    {
        ["Firebase:ProjectId"] = "test-project",
        ["Firebase:PrivateKeyId"] = "key123",
        ["Firebase:PrivateKey"] = "-----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQCtest\n-----END PRIVATE KEY-----",
        ["Firebase:ClientEmail"] = "test@test-project.iam.gserviceaccount.com",
        ["Firebase:ClientId"] = "123456789"
    };

    private Dictionary<string, string?> NoFirebaseConfig() => [];

    public void Dispose()
    {
        try
        {
            var app = FirebaseApp.DefaultInstance;
            if (app != null)
                app.Delete();
        }
        catch
        {
        }
        GC.SuppressFinalize(this);
    }

    public class Constructor : FirebaseServiceTests
    {
        [Fact]
        public void NoFirebaseConfig_DoesNotThrow()
        {
            var service = CreateServiceWithConfig(NoFirebaseConfig());

            service.Should().NotBeNull();
        }

        [Fact]
        public void NoFirebaseConfig_LogsWarning()
        {
            CreateServiceWithConfig(NoFirebaseConfig());

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Firebase credentials not configured")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void MissingProjectId_LogsWarning()
        {
            var config = FullFirebaseConfig();
            config["Firebase:ProjectId"] = null;

            CreateServiceWithConfig(config!);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Firebase credentials not configured")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void MissingPrivateKey_LogsWarning()
        {
            var config = FullFirebaseConfig();
            config["Firebase:PrivateKey"] = null;

            CreateServiceWithConfig(config!);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Firebase credentials not configured")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void MissingClientEmail_LogsWarning()
        {
            var config = FullFirebaseConfig();
            config["Firebase:ClientEmail"] = null;

            CreateServiceWithConfig(config!);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Firebase credentials not configured")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void EmptyProjectId_LogsWarning()
        {
            var config = FullFirebaseConfig();
            config["Firebase:ProjectId"] = "";

            CreateServiceWithConfig(config);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Firebase credentials not configured")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void EmptyPrivateKey_LogsWarning()
        {
            var config = FullFirebaseConfig();
            config["Firebase:PrivateKey"] = "";

            CreateServiceWithConfig(config);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Firebase credentials not configured")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void EmptyClientEmail_LogsWarning()
        {
            var config = FullFirebaseConfig();
            config["Firebase:ClientEmail"] = "";

            CreateServiceWithConfig(config);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Firebase credentials not configured")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void InvalidPrivateKey_LogsError()
        {
            var config = FullFirebaseConfig();
            config["Firebase:PrivateKey"] = "not-a-valid-key";

            CreateServiceWithConfig(config);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Failed to initialize Firebase Admin SDK")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void DefaultInstanceAlreadyExists_DoesNotCreateSecond()
        {
            var config = FullFirebaseConfig();
            config["Firebase:PrivateKey"] = "not-a-valid-key-for-first";

            var service1 = CreateServiceWithConfig(config);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }

    public class SendNotificationBatchAsync : FirebaseServiceTests
    {
        [Fact]
        public async Task NotInitialized_DoesNotThrow()
        {
            var service = CreateServiceWithConfig(NoFirebaseConfig());

            var act = async () => await service.SendNotificationBatchAsync(
                ["token1"], "Sender", "Hello", "chat1", 1, 1, null, null, null, false, 0, null, 0);

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task EmptyTokens_DoesNotThrow()
        {
            var service = CreateServiceWithConfig(FullFirebaseConfig());

            var act = async () => await service.SendNotificationBatchAsync(
                [], "Sender", "Hello", "chat1", 1, 1, null, null, null, false, 0, null, 0);

            await act.Should().NotThrowAsync();
        }
    }

    public class SendDismissBatchAsync : FirebaseServiceTests
    {
        [Fact]
        public async Task NotInitialized_DoesNotThrow()
        {
            var service = CreateServiceWithConfig(NoFirebaseConfig());

            var act = async () => await service.SendDismissBatchAsync(["token1"], "chat1");

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task EmptyTokens_DoesNotThrow()
        {
            var service = CreateServiceWithConfig(FullFirebaseConfig());

            var act = async () => await service.SendDismissBatchAsync([], "chat1");

            await act.Should().NotThrowAsync();
        }
    }

    public class SendAdminBroadcastBatchAsync : FirebaseServiceTests
    {
        [Fact]
        public async Task NotInitialized_ReturnsZeros()
        {
            var service = CreateServiceWithConfig(NoFirebaseConfig());

            var (success, failure) = await service.SendAdminBroadcastBatchAsync(
                ["token1"], "Title", "Body", null);

            success.Should().Be(0);
            failure.Should().Be(0);
        }

        [Fact]
        public async Task EmptyTokens_ReturnsZeros()
        {
            var service = CreateServiceWithConfig(FullFirebaseConfig());

            var (success, failure) = await service.SendAdminBroadcastBatchAsync(
                [], "Title", "Body", null);

            success.Should().Be(0);
            failure.Should().Be(0);
        }
    }
}
