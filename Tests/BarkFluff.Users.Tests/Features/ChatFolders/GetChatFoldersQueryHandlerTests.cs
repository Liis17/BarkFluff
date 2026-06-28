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

public class GetChatFoldersQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ReturnsFoldersForCurrentUser()
    {
        var user = await _h.SeedUser();
        await _h.ChatFolderStorage.CreateAsync(user.Id, "Work", null);
        await _h.ChatFolderStorage.CreateAsync(user.Id, "Personal", null);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetChatFoldersQueryHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<GetChatFoldersQueryHandler>());

        var result = await handler.Handle(new GetChatFoldersQuery(), CancellationToken.None);

        result.Folders.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoFolders_ReturnsEmpty()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetChatFoldersQueryHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<GetChatFoldersQueryHandler>());

        var result = await handler.Handle(new GetChatFoldersQuery(), CancellationToken.None);

        result.Folders.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
