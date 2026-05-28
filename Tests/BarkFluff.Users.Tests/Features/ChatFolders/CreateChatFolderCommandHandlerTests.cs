using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Features.ChatFolders.AddChatToFolder;
using BarkFluff.Users.Features.ChatFolders.CreateChatFolder;
using BarkFluff.Users.Features.ChatFolders.DeleteChatFolder;
using BarkFluff.Users.Features.ChatFolders.GetChatFolders;
using BarkFluff.Users.Features.ChatFolders.RemoveChatFromFolder;
using BarkFluff.Users.Features.ChatFolders.ReorderChatFolders;
using BarkFluff.Users.Features.ChatFolders.UpdateChatFolder;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.ChatFolders;

public class CreateChatFolderCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ValidName_CreatesFolder()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new CreateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<CreateChatFolderCommandHandler>());

        var result = await handler.Handle(new CreateChatFolderCommand { FolderName = "Work", FolderIcon = "💼" }, CancellationToken.None);

        result.Folder.Should().NotBeNull();
        result.Folder.FolderName.Should().Be("Work");
        result.Folder.FolderIcon.Should().Be("💼");
    }

    [Fact]
    public async Task Handle_EmptyName_ThrowsChatFolderInvalidNameException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new CreateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<CreateChatFolderCommandHandler>());

        var act = () => handler.Handle(new CreateChatFolderCommand { FolderName = "" }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatFolderInvalidNameException>();
    }

    [Fact]
    public async Task Handle_NameOver64Chars_ThrowsChatFolderInvalidNameException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new CreateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<CreateChatFolderCommandHandler>());

        var act = () => handler.Handle(new CreateChatFolderCommand { FolderName = new string('x', 65) }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatFolderInvalidNameException>();
    }

    [Fact]
    public async Task Handle_Exactly64Chars_Succeeds()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new CreateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<CreateChatFolderCommandHandler>());

        var result = await handler.Handle(new CreateChatFolderCommand { FolderName = new string('x', 64) }, CancellationToken.None);

        result.Folder.FolderName.Should().HaveLength(64);
    }

    [Fact]
    public async Task Handle_NullName_ThrowsChatFolderInvalidNameException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new CreateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<CreateChatFolderCommandHandler>());

        var act = () => handler.Handle(new CreateChatFolderCommand { FolderName = null }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatFolderInvalidNameException>();
    }

    [Fact]
    public async Task Handle_EmptyIcon_SetsToNull()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new CreateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<CreateChatFolderCommandHandler>());

        var result = await handler.Handle(new CreateChatFolderCommand { FolderName = "Test", FolderIcon = "" }, CancellationToken.None);

        result.Folder.FolderIcon.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AutoSortOrder_Increments()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new CreateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<CreateChatFolderCommandHandler>());

        var r1 = await handler.Handle(new CreateChatFolderCommand { FolderName = "First" }, CancellationToken.None);
        var r2 = await handler.Handle(new CreateChatFolderCommand { FolderName = "Second" }, CancellationToken.None);

        r1.Folder.SortOrder.Should().Be(0);
        r2.Folder.SortOrder.Should().Be(1);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
