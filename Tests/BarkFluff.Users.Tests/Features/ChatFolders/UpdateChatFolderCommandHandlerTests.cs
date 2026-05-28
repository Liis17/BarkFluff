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

public class UpdateChatFolderCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_UpdatesName()
    {
        var user = await _h.SeedUser();
        var folder = await _h.ChatFolderStorage.CreateAsync(user.Id, "Old", null);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new UpdateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<UpdateChatFolderCommandHandler>());

        var result = await handler.Handle(new UpdateChatFolderCommand
        {
            FolderId = folder.FolderId.ToString(),
            FolderName = "New"
        }, CancellationToken.None);

        result.Folder.FolderName.Should().Be("New");
    }

    [Fact]
    public async Task Handle_InvalidFolderId_ThrowsChatFolderNotFoundException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new UpdateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<UpdateChatFolderCommandHandler>());

        var act = () => handler.Handle(new UpdateChatFolderCommand
        {
            FolderId = "not-a-guid",
            FolderName = "New"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatFolderNotFoundException>();
    }

    [Fact]
    public async Task Handle_FolderNotFound_ThrowsChatFolderNotFoundException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new UpdateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<UpdateChatFolderCommandHandler>());

        var act = () => handler.Handle(new UpdateChatFolderCommand
        {
            FolderId = Guid.NewGuid().ToString(),
            FolderName = "New"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatFolderNotFoundException>();
    }

    [Fact]
    public async Task Handle_NameOver64Chars_ThrowsChatFolderInvalidNameException()
    {
        var user = await _h.SeedUser();
        var folder = await _h.ChatFolderStorage.CreateAsync(user.Id, "Old", null);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new UpdateChatFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<UpdateChatFolderCommandHandler>());

        var act = () => handler.Handle(new UpdateChatFolderCommand
        {
            FolderId = folder.FolderId.ToString(),
            FolderName = new string('x', 65)
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatFolderInvalidNameException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
