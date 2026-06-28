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

public class ReorderChatFoldersCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_UpdatesSortOrders()
    {
        var user = await _h.SeedUser();
        var f1 = await _h.ChatFolderStorage.CreateAsync(user.Id, "First", null);
        var f2 = await _h.ChatFolderStorage.CreateAsync(user.Id, "Second", null);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ReorderChatFoldersCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<ReorderChatFoldersCommandHandler>());

        await handler.Handle(new ReorderChatFoldersCommand
        {
            Orders =
            [
                new() { FolderId = f1.FolderId.ToString(), SortOrder = 5 },
                new() { FolderId = f2.FolderId.ToString(), SortOrder = 0 },
            ]
        }, CancellationToken.None);

        var folders = await _h.ChatFolderStorage.GetByOwnerAsync(user.Id);
        var updated1 = folders.First(f => f.FolderId == f1.FolderId);
        var updated2 = folders.First(f => f.FolderId == f2.FolderId);
        updated1.SortOrder.Should().Be(5);
        updated2.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task Handle_NullOrders_NoChanges()
    {
        var user = await _h.SeedUser();
        var f1 = await _h.ChatFolderStorage.CreateAsync(user.Id, "Test", null);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ReorderChatFoldersCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<ReorderChatFoldersCommandHandler>());

        await handler.Handle(new ReorderChatFoldersCommand { Orders = null }, CancellationToken.None);

        var folders = await _h.ChatFolderStorage.GetByOwnerAsync(user.Id);
        folders.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_InvalidGuidInOrder_SkipsInvalidEntry()
    {
        var user = await _h.SeedUser();
        var f1 = await _h.ChatFolderStorage.CreateAsync(user.Id, "Test", null);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new ReorderChatFoldersCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<ReorderChatFoldersCommandHandler>());

        await handler.Handle(new ReorderChatFoldersCommand
        {
            Orders =
            [
                new() { FolderId = f1.FolderId.ToString(), SortOrder = 10 },
                new() { FolderId = "invalid-guid", SortOrder = 20 },
            ]
        }, CancellationToken.None);

        var folders = await _h.ChatFolderStorage.GetByOwnerAsync(user.Id);
        folders.First(f => f.FolderId == f1.FolderId).SortOrder.Should().Be(10);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
