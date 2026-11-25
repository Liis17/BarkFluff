using BarkFluff.Users.Domain;
using BarkFluff.Users.Features.SearchUsersServer;
using BarkFluff.Users.Persistence.Contexts;
using BarkFluff.Users.Persistence.Services;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using ProtoAccountStatus = BarkFluff.Proto.Users.AccountStatus;

namespace BarkFluff.Users.Tests.Features.Search;

// Trigram-ветка поиска использует similarity() PostgreSQL, поэтому EF InMemory её не проверяет.
public class SearchUsersServerPostgresTests
{
    [SearchPostgresFact]
    public async Task Handle_TrigramQuery_AppliesStatusFilterBeforePagination()
    {
        var root = Environment.GetEnvironmentVariable("BARKFLUFF_PREKEY_POSTGRES")!;
        var databaseName = "barkfluff_search_" + Guid.NewGuid().ToString("N");
        await CreateDatabaseAsync(root, databaseName);
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(root) { Database = databaseName, Pooling = false };
            var options = new DbContextOptionsBuilder<UsersContext>().UseNpgsql(builder.ConnectionString).Options;
            await using var context = new UsersContext(options);
            await context.Database.MigrateAsync();

            // Все совпадают по имени "Ivan". Удалённые созданы последними и в выборке по ID шли бы первыми.
            AddUser(context, "ivan_active_1", BarkFluff.Users.Domain.AccountStatus.Active);
            AddUser(context, "ivan_active_2", BarkFluff.Users.Domain.AccountStatus.Active);
            AddUser(context, "ivan_active_3", BarkFluff.Users.Domain.AccountStatus.Active);
            AddUser(context, "ivan_blocked", BarkFluff.Users.Domain.AccountStatus.Blocked);
            for (var i = 1; i <= 5; i++)
                AddUser(context, $"ivan_deleted_{i}", BarkFluff.Users.Domain.AccountStatus.Deleted);
            await context.SaveChangesAsync();

            var handler = new SearchUsersServerQueryHandler(new UsersStorage(context), TestHelper.CreateLogger<SearchUsersServerQueryHandler>());
            var statuses = new[] { ProtoAccountStatus.Active, ProtoAccountStatus.Blocked };

            var firstPage = await handler.Handle(new SearchUsersServerQuery
            {
                Query = "Ivan", Offset = 0, Size = 2, StatusFilters = statuses,
            }, CancellationToken.None);
            var secondPage = await handler.Handle(new SearchUsersServerQuery
            {
                Query = "Ivan", Offset = 2, Size = 2, StatusFilters = statuses,
            }, CancellationToken.None);
            var deletedOnly = await handler.Handle(new SearchUsersServerQuery
            {
                Query = "Ivan", Offset = 0, Size = 10, StatusFilters = [ProtoAccountStatus.Deleted],
            }, CancellationToken.None);

            firstPage.Users.Should().HaveCount(2);
            firstPage.TotalCount.Should().Be(4);
            secondPage.Users.Should().HaveCount(2);
            secondPage.TotalCount.Should().Be(4);
            firstPage.Users.Concat(secondPage.Users).Should().OnlyContain(u =>
                u.AccountStatus == ProtoAccountStatus.Active || u.AccountStatus == ProtoAccountStatus.Blocked);
            firstPage.Users.Select(u => u.Id).Intersect(secondPage.Users.Select(u => u.Id)).Should().BeEmpty();
            deletedOnly.TotalCount.Should().Be(5);
            deletedOnly.Users.Should().OnlyContain(u => u.AccountStatus == ProtoAccountStatus.Deleted);

            // Выборка без запроса проходит через EF-трансляцию GetAllUsersDescending.
            var allPage = await handler.Handle(new SearchUsersServerQuery
            {
                Query = "", Offset = 0, Size = 2, StatusFilters = statuses,
            }, CancellationToken.None);
            allPage.TotalCount.Should().Be(4);
            allPage.Users.Should().HaveCount(2);
            allPage.Users.Should().OnlyContain(u =>
                u.AccountStatus == ProtoAccountStatus.Active || u.AccountStatus == ProtoAccountStatus.Blocked);
        }
        finally
        {
            await DropDatabaseAsync(root, databaseName);
        }
    }

    private static void AddUser(UsersContext context, string username, BarkFluff.Users.Domain.AccountStatus status)
    {
        context.Users.Add(new User
        {
            Username = username,
            FirstName = "Ivan",
            LastName = "User",
            RegistrationDate = DateTime.UtcNow,
            AccountStatus = status,
            Contact = new UserContact { Email = $"{username}@test.com" },
        });
    }

    private static async Task CreateDatabaseAsync(string root, string databaseName)
    {
        await using var connection = new NpgsqlConnection(root);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string root, string databaseName)
    {
        await using var connection = new NpgsqlConnection(root);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class SearchPostgresFactAttribute : FactAttribute
{
    public SearchPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BARKFLUFF_PREKEY_POSTGRES")))
        {
            Skip = "BARKFLUFF_PREKEY_POSTGRES must point to a disposable PostgreSQL instance with CREATE DATABASE access.";
        }
    }
}
