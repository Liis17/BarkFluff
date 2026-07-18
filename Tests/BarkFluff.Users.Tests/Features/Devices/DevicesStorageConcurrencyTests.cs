using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;
using BarkFluff.Users.Persistence.Services;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Tests.Features.Devices;

public class DevicesStorageConcurrencyTests
{
    [Fact]
    public async Task RegisterOrUpdateDevice_ConcurrentCalls_KeepSingleDevice()
    {
        var connectionString = $"Data Source=devices-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();

        await using (var setupContext = CreateContext(anchor))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Users.Add(new User
            {
                Id = 42,
                Username = "concurrent_device_user",
                FirstName = "Concurrent",
                LastName = "User",
                RegistrationDate = DateTime.UtcNow,
                Contact = new UserContact { Email = "concurrent-device@test.com" }
            });
            await setupContext.SaveChangesAsync();
        }

        await using var firstConnection = new SqliteConnection(connectionString);
        await using var secondConnection = new SqliteConnection(connectionString);
        await firstConnection.OpenAsync();
        await secondConnection.OpenAsync();
        await using var firstContext = CreateContext(firstConnection);
        await using var secondContext = CreateContext(secondConnection);
        var deviceId = Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstRegistration = RegisterAfterStart(new DevicesStorage(firstContext), "First");
        var secondRegistration = RegisterAfterStart(new DevicesStorage(secondContext), "Second");
        start.SetResult();

        await Task.WhenAll(firstRegistration, secondRegistration);

        await using var verificationContext = CreateContext(anchor);
        var devices = await verificationContext.UserDevices
            .AsNoTracking()
            .Where(device => device.Id == deviceId)
            .ToListAsync();
        devices.Should().ContainSingle();
        devices[0].UserId.Should().Be(42);

        async Task RegisterAfterStart(DevicesStorage storage, string originalName)
        {
            await start.Task;
            await storage.RegisterOrUpdateDevice(
                deviceId,
                42,
                originalName,
                "BarkFluff",
                "TestOS",
                null);
        }
    }

    private static UsersContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<UsersContext>()
            .UseSqlite(connection)
            .Options;
        return new UsersContext(options);
    }
}
