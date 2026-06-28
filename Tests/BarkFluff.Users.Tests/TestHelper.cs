using System.Security.Claims;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Users.Infrastructure;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BarkFluff.Users.Tests;

public class TestHelper
{
    private static long _nextUserId = 1000;

    public UsersContext DbContext { get; }
    public UsersStorage UsersStorage { get; }
    public DevicesStorage DevicesStorage { get; }
    public PrivacyStorage PrivacyStorage { get; }
    public PersonalizationStorage PersonalizationStorage { get; }
    public ChatFolderStorage ChatFolderStorage { get; }
    public PrekeyStorage PrekeyStorage { get; }
    public Mock<IPublishEndpoint> PublishEndpointMock { get; }
    public UserInfoQueueSender QueueSender { get; }
    public MetricsCollector Metrics { get; }

    public TestHelper()
    {
        var options = new DbContextOptionsBuilder<UsersContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        DbContext = new UsersContext(options);
        UsersStorage = new UsersStorage(DbContext);
        DevicesStorage = new DevicesStorage(DbContext);
        PrivacyStorage = new PrivacyStorage(DbContext);
        PersonalizationStorage = new PersonalizationStorage(DbContext);
        ChatFolderStorage = new ChatFolderStorage(DbContext);
        PrekeyStorage = new PrekeyStorage(DbContext);
        PublishEndpointMock = new Mock<IPublishEndpoint>();
        Metrics = new MetricsCollector();
        QueueSender = new BarkFluff.Users.Infrastructure.UserInfoQueueSender(PublishEndpointMock.Object, Metrics);
    }

    public UserContext CreateUserContext(long userId, string? deviceId = null)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaims.UserId, userId.ToString()),
            new(IdentityClaims.TokenType, "User"),
        };
        if (deviceId != null)
            claims.Add(new Claim(IdentityClaims.DeviceId, deviceId));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        return new UserContext(httpContextAccessor.Object);
    }

    public UserContext CreateServiceContext()
    {
        var claims = new List<Claim>
        {
            new(IdentityClaims.TokenType, "Service"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        return new UserContext(httpContextAccessor.Object);
    }

    public static ILogger<T> CreateLogger<T>()
    {
        return Mock.Of<ILogger<T>>();
    }

    public async Task<User> SeedUser(
        long? id = null,
        string username = "testuser",
        string firstName = "Test",
        string lastName = "User",
        string email = "test@test.com",
        bool isDraft = false,
        string? bio = null,
        string? profilePicture = null)
    {
        var user = new User
        {
             Id = id ?? Interlocked.Increment(ref _nextUserId),
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            RegistrationDate = DateTime.UtcNow,
            IsDraft = isDraft,
            Bio = bio,
            ProfilePicture = profilePicture,
            Contact = new UserContact { Email = email }
        };

        DbContext.Users.Add(user);
        await DbContext.SaveChangesAsync();
        return user;
    }

    public async Task<UserDevice> SeedDevice(
        Guid? deviceId = null,
        long userId = 0,
        string originalName = "Test Device",
        string? appName = "TestApp",
        string? os = "TestOS",
        string? firebaseToken = null)
    {
        if (userId == 0)
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var user = await SeedUser(username: $"devuser_{suffix}", email: $"dev_{suffix}@test.com");
            userId = user.Id;
        }

        var device = new UserDevice
        {
            Id = deviceId ?? Guid.NewGuid(),
            UserId = userId,
            OriginalName = originalName,
            AuthorizedAt = DateTime.UtcNow,
            AppName = appName,
            OperationSystem = os,
            FirebaseDeviceToken = firebaseToken,
        };

        DbContext.UserDevices.Add(device);
        await DbContext.SaveChangesAsync();
        return device;
    }

    public async Task<Badge> SeedBadge(
        string name = "Test Badge",
        string? description = "Test Description",
        string imageUrl = "https://test.com/badge.png",
        bool isActive = true)
    {
        var badge = new Badge
        {
            Name = name,
            Description = description,
            ImageUrl = imageUrl,
            IsActive = isActive,
            CreatedDate = DateTime.UtcNow,
        };

        DbContext.Badges.Add(badge);
        await DbContext.SaveChangesAsync();
        return badge;
    }

    public async Task<Privacy> SeedPrivacy(
        long userId,
        bool profileVisibleOnSite = true,
        ProfileFieldVisibility avatarVisibility = ProfileFieldVisibility.All,
        ProfileFieldVisibility bioVisibility = ProfileFieldVisibility.All,
        bool searchVisible = true)
    {
        var privacy = new Privacy
        {
            UserId = userId,
            ProfileVisibleOnSite = profileVisibleOnSite,
            AvatarVisibility = avatarVisibility,
            BioVisibility = bioVisibility,
            SearchVisible = searchVisible,
        };

        DbContext.Privacies.Add(privacy);
        await DbContext.SaveChangesAsync();
        return privacy;
    }
}
