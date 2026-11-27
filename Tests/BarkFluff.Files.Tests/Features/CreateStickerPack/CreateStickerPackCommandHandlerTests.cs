using BarkFluff.Files.Features.CreateStickerPack;
using BarkFluff.Shared.Exceptions.Files;

namespace BarkFluff.Files.Tests.Features.CreateStickerPack;

public class CreateStickerPackCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private CreateStickerPackCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        _handler = new CreateStickerPackCommandHandler(
            _helper.StickerPacksStorage,
            TestHelper.CreateLogger<CreateStickerPackCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_CreatesPackWithCorrectFields()
    {
        var command = new CreateStickerPackCommand
        {
            CreatorUserId = 42,
            Name = "Cool Stickers",
            Description = "Best stickers ever"
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Pack.Should().NotBeNull();
        result.Pack.Name.Should().Be("Cool Stickers");
        result.Pack.Description.Should().Be("Best stickers ever");
        result.Pack.CreatorUserId.Should().Be(42);
        result.Pack.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_PersistsToDatabase()
    {
        var command = new CreateStickerPackCommand
        {
            CreatorUserId = 1,
            Name = "Test",
            Description = "Desc"
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        var packId = Guid.Parse(result.Pack.Id);
        var fetched = await _helper.StickerPacksStorage.GetByIdAsync(packId);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task Handle_NoShortName_GeneratesFromPackId()
    {
        var result = await _handler.Handle(new CreateStickerPackCommand { Name = "Pack" }, CancellationToken.None);

        result.Pack.ShortName.Should().Be("pack_" + Guid.Parse(result.Pack.Id).ToString("N")[..12]);
    }

    [Fact]
    public async Task Handle_ShortNameGiven_StoresLowercase()
    {
        var result = await _handler.Handle(
            new CreateStickerPackCommand { Name = "Pack", ShortName = "Shark_Liis" }, CancellationToken.None);

        result.Pack.ShortName.Should().Be("shark_liis");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("1pack")]
    [InlineData("pack-name")]
    [InlineData("пак_котиков")]
    public async Task Handle_InvalidShortName_Throws(string shortName)
    {
        var act = () => _handler.Handle(
            new CreateStickerPackCommand { Name = "Pack", ShortName = shortName }, CancellationToken.None);

        await act.Should().ThrowAsync<StickerPackShortNameNotValidException>();
    }

    [Fact]
    public async Task Handle_ShortNameTaken_Throws()
    {
        await _helper.SeedStickerPack(shortName: "cats");

        var act = () => _handler.Handle(
            new CreateStickerPackCommand { Name = "Pack", ShortName = "Cats" }, CancellationToken.None);

        await act.Should().ThrowAsync<StickerPackShortNameTakenException>();
    }
}
