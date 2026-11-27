using BarkFluff.Files.Features.AddMyStickerPack;
using BarkFluff.Files.Features.GetStickerPack;
using BarkFluff.Files.Features.GetStickerPackByName;
using BarkFluff.Files.Features.ListMyStickerPacks;
using BarkFluff.Files.Features.RemoveMyStickerPack;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions.Files;

using MediatR;

namespace BarkFluff.Files.Tests.Features.UserStickerPacks;

public class UserStickerPacksTests
{
    private const long UserId = 42;

    private readonly TestHelper _helper = new();
    private readonly AddMyStickerPackCommandHandler _add;
    private readonly RemoveMyStickerPackCommandHandler _remove;
    private readonly ListMyStickerPacksCommandHandler _list;
    private readonly GetStickerPackByNameCommandHandler _byName;

    public UserStickerPacksTests()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://example.com");
        var runSettings = new RunSettings { Http1Port = 7005 };

        _add = new AddMyStickerPackCommandHandler(_helper.StickerPacksStorage, _helper.UserStickerPacksStorage);
        _remove = new RemoveMyStickerPackCommandHandler(_helper.UserStickerPacksStorage);
        _list = new ListMyStickerPacksCommandHandler(_helper.UserStickerPacksStorage, config.Object, runSettings);

        var getPack = new GetStickerPackCommandHandler(
            _helper.StickerPacksStorage, _helper.UserStickerPacksStorage, config.Object, runSettings);
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<GetStickerPackCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<GetStickerPackResponse> c, CancellationToken ct) => getPack.Handle((GetStickerPackCommand)c, ct));
        _byName = new GetStickerPackByNameCommandHandler(_helper.StickerPacksStorage, mediator.Object);
    }

    [Fact]
    public async Task List_NewUser_IsEmpty()
    {
        await _helper.SeedStickerPack();

        var result = await _list.Handle(new ListMyStickerPacksCommand { UserId = UserId }, CancellationToken.None);

        result.Packs.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_ThenList_ReturnsOnlyOwnPacksNewestFirstWithStickers()
    {
        var first = await _helper.SeedStickerPack(name: "First");
        var second = await _helper.SeedStickerPack(name: "Second");
        var foreign = await _helper.SeedStickerPack(name: "Foreign");
        await _helper.SeedSticker(stickerPackId: first.Id, emoji: "😀");
        await _helper.SeedSticker(stickerPackId: second.Id, emoji: "😎");
        await _helper.SeedUserStickerPack(UserId, first.Id, DateTime.UtcNow.AddMinutes(-5));
        await _helper.SeedUserStickerPack(userId: 99, foreign.Id);

        await _add.Handle(new AddMyStickerPackCommand { UserId = UserId, PackId = second.Id }, CancellationToken.None);
        var result = await _list.Handle(new ListMyStickerPacksCommand { UserId = UserId }, CancellationToken.None);

        result.Packs.Select(p => p.Pack.Name).Should().Equal("Second", "First");
        result.Packs[0].Pack.StickerCount.Should().Be(1);
        result.Packs[0].Stickers.Single().Emoji.Should().Be("😎");
    }

    [Fact]
    public async Task Add_Twice_IsIdempotent()
    {
        var pack = await _helper.SeedStickerPack();

        await _add.Handle(new AddMyStickerPackCommand { UserId = UserId, PackId = pack.Id }, CancellationToken.None);
        await _add.Handle(new AddMyStickerPackCommand { UserId = UserId, PackId = pack.Id }, CancellationToken.None);

        (await _helper.UserStickerPacksStorage.CountAsync(UserId)).Should().Be(1);
    }

    [Fact]
    public async Task Add_UnknownPack_Throws()
    {
        var act = () => _add.Handle(
            new AddMyStickerPackCommand { UserId = UserId, PackId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<StickerPackNotFoundException>();
    }

    [Fact]
    public async Task Add_OverLimit_Throws()
    {
        for (var i = 0; i < AddMyStickerPackCommandHandler.MaxPacksPerUser; i++)
        {
            var existing = await _helper.SeedStickerPack();
            await _helper.SeedUserStickerPack(UserId, existing.Id);
        }
        var pack = await _helper.SeedStickerPack();

        var act = () => _add.Handle(new AddMyStickerPackCommand { UserId = UserId, PackId = pack.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<StickerPackLimitExceededException>();
    }

    [Fact]
    public async Task Remove_RemovesOnlyForThisUser_AndRepeatIsNoop()
    {
        var pack = await _helper.SeedStickerPack();
        await _helper.SeedUserStickerPack(UserId, pack.Id);
        await _helper.SeedUserStickerPack(userId: 99, pack.Id);

        await _remove.Handle(new RemoveMyStickerPackCommand { UserId = UserId, PackId = pack.Id }, CancellationToken.None);
        await _remove.Handle(new RemoveMyStickerPackCommand { UserId = UserId, PackId = pack.Id }, CancellationToken.None);

        (await _helper.UserStickerPacksStorage.ExistsAsync(UserId, pack.Id)).Should().BeFalse();
        (await _helper.UserStickerPacksStorage.ExistsAsync(99, pack.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task DeletePack_RemovesItFromUsers()
    {
        var pack = await _helper.SeedStickerPack();
        await _helper.SeedUserStickerPack(UserId, pack.Id);

        await _helper.StickerPacksStorage.DeleteAsync(pack.Id);

        (await _helper.UserStickerPacksStorage.CountAsync(UserId)).Should().Be(0);
    }

    [Fact]
    public async Task GetByName_IgnoresCase_AndReportsIsAdded()
    {
        var pack = await _helper.SeedStickerPack(shortName: "shark_liis");
        await _helper.SeedUserStickerPack(UserId, pack.Id);

        var result = await _byName.Handle(
            new GetStickerPackByNameCommand { ShortName = "Shark_Liis", UserId = UserId }, CancellationToken.None);

        result.Pack.Id.Should().Be(pack.Id.ToString());
        result.Pack.ShortName.Should().Be("shark_liis");
        result.IsAdded.Should().BeTrue();
    }

    [Theory]
    [InlineData("missing_pack")]
    [InlineData("../etc")]
    [InlineData("")]
    public async Task GetByName_UnknownOrInvalid_Throws(string shortName)
    {
        var act = () => _byName.Handle(
            new GetStickerPackByNameCommand { ShortName = shortName, UserId = UserId }, CancellationToken.None);

        await act.Should().ThrowAsync<StickerPackNotFoundException>();
    }
}
