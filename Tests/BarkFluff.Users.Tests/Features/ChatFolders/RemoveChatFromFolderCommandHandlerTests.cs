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

public class RemoveChatFromFolderCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_RemovesChatFromFolder()
    {
        var user = await _h.SeedUser();
        var chatId = Guid.NewGuid();
        var folder = await _h.ChatFolderStorage.CreateAsync(user.Id, "Work", null);
        await _h.ChatFolderStorage.AddChatAsync(user.Id, folder.FolderId, chatId);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new RemoveChatFromFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<RemoveChatFromFolderCommandHandler>());

        var result = await handler.Handle(new RemoveChatFromFolderCommand
        {
            FolderId = folder.FolderId.ToString(),
            ChatId = chatId
        }, CancellationToken.None);

        result.Folder.ChatList.Should().NotContain(chatId.ToString());
    }

    [Fact]
    public async Task Handle_NonExistentChat_Idempotent()
    {
        var user = await _h.SeedUser();
        var folder = await _h.ChatFolderStorage.CreateAsync(user.Id, "Work", null);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new RemoveChatFromFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<RemoveChatFromFolderCommandHandler>());

        var act = () => handler.Handle(new RemoveChatFromFolderCommand
        {
            FolderId = folder.FolderId.ToString(),
            ChatId = Guid.NewGuid()
        }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_InvalidFolderId_ThrowsChatFolderNotFoundException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new RemoveChatFromFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<RemoveChatFromFolderCommandHandler>());

        var act = () => handler.Handle(new RemoveChatFromFolderCommand
        {
            FolderId = "invalid",
            ChatId = Guid.NewGuid()
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatFolderNotFoundException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
