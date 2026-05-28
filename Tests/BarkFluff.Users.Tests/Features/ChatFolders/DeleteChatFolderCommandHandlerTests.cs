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

public class DeleteChatFolderCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_DeletesExistingFolder()
    {
        var user = await _h.SeedUser();
        var folder = await _h.ChatFolderStorage.CreateAsync(user.Id, "ToDelete", null);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new DeleteChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<DeleteChatFolderCommandHandler>());

        await handler.Handle(new DeleteChatFolderCommand { FolderId = folder.FolderId.ToString() }, CancellationToken.None);

        var folders = await _h.ChatFolderStorage.GetByOwnerAsync(user.Id);
        folders.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InvalidFolderId_ThrowsChatFolderNotFoundException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new DeleteChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<DeleteChatFolderCommandHandler>());

        var act = () => handler.Handle(new DeleteChatFolderCommand { FolderId = "invalid" }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatFolderNotFoundException>();
    }

    [Fact]
    public async Task Handle_NonExistingFolder_ThrowsChatFolderNotFoundException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new DeleteChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<DeleteChatFolderCommandHandler>());

        var act = () => handler.Handle(new DeleteChatFolderCommand { FolderId = Guid.NewGuid().ToString() }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatFolderNotFoundException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
