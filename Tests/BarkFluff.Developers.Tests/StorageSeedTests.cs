using Barkfluff.Developers.Infrastructure;
using Barkfluff.Developers.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Barkfluff.Developers.Tests;

public class StorageSeedTests
{
    [Fact]
    public async Task Documentation_seed_is_additive_and_idempotent()
    {
        await using var context = TestInfrastructure.CreateContext();
        var storage = new DocumentationStorage(context);

        (await storage.SeedMissingAsync()).Should().Be(6);
        (await storage.SeedMissingAsync()).Should().Be(0);
        (await context.DocumentationSections.CountAsync()).Should().Be(6);
    }

    [Fact]
    public async Task Existing_documentation_values_are_not_overwritten()
    {
        await using var context = TestInfrastructure.CreateContext();
        context.DocumentationSections.Add(TestInfrastructure.Documentation(
            "overview",
            "{\"custom\":true}"));
        await context.SaveChangesAsync();
        var storage = new DocumentationStorage(context);

        (await storage.SeedMissingAsync()).Should().Be(5);

        var existing = await context.DocumentationSections.SingleAsync(section => section.Key == "overview");
        existing.Content.Should().Be("{\"custom\":true}");
        existing.Title.Should().Be("overview");
    }

    [Fact]
    public async Task Proto_metadata_seed_adds_missing_rows_without_replacing_existing_values()
    {
        await using var context = TestInfrastructure.CreateContext();
        var existing = TestInfrastructure.ProtoMetadata("shared.proto");
        existing.DisplayName = "Manual title";
        context.ProtoMetadata.Add(existing);
        await context.SaveChangesAsync();
        var storage = new ProtoMetadataStorage(context);

        (await storage.SeedMissingAsync()).Should().Be(9);

        var stored = await context.ProtoMetadata.SingleAsync(item => item.FileName == "shared.proto");
        stored.DisplayName.Should().Be("Manual title");
        (await storage.SeedMissingAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Error_code_seed_is_additive_and_idempotent()
    {
        await using var context = TestInfrastructure.CreateContext();
        var definitions = ErrorCodeSeeder.DiscoverEntries();
        var existing = definitions[0];
        existing.Description = "Manual description";
        context.ErrorCodes.Add(existing);
        await context.SaveChangesAsync();
        var seeder = new ErrorCodeSeeder();

        (await seeder.SeedMissingAsync(context)).Should().Be(definitions.Count - 1);
        (await seeder.SeedMissingAsync(context)).Should().Be(0);
        (await context.ErrorCodes.CountAsync()).Should().Be(definitions.Count);
        (await context.ErrorCodes.SingleAsync(entry => entry.Code == existing.Code))
            .Description.Should().Be("Manual description");
    }

    [Fact]
    public async Task Reads_are_no_tracking_sorted_and_honor_cancellation()
    {
        await using var context = TestInfrastructure.CreateContext();
        context.DocumentationSections.AddRange(
            TestInfrastructure.Documentation("z"),
            TestInfrastructure.Documentation("a"));
        context.DocumentationSections.Local.First(section => section.Key == "z").Order = 2;
        await context.SaveChangesAsync();
        var storage = new DocumentationStorage(context);

        var sections = await storage.GetAllAsync();

        sections.Select(section => section.Key).Should().Equal("a", "z");
        context.Entry(sections[0]).State.Should().Be(EntityState.Detached);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => storage.GetAllAsync(cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }
}
