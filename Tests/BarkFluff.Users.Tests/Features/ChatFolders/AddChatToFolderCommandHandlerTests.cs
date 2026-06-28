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

public class AddChatToFolderCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_AddsChatToFolder()
    {
        var user = await _h.SeedUser();
        var folder = await _h.ChatFolderStorage.CreateAsync(user.Id, "Work", null);
        var ctx = _h.CreateUserContext(user.Id);
        var chatId = Guid.NewGuid();
        var handler = new AddChatToFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<AddChatToFolderCommandHandler>());

        var result = await handler.Handle(new AddChatToFolderCommand
        {
            FolderId = folder.FolderId.ToString(),
            ChatId = chatId
        }, CancellationToken.None);

        result.Folder.ChatList.Should().Contain(chatId.ToString());
    }

    [Fact]
    public async Task Handle_DuplicateChat_Idempotent()
    {
        var user = await _h.SeedUser();
        var folder = await _h.ChatFolderStorage.CreateAsync(user.Id, "Work", null);
        var ctx = _h.CreateUserContext(user.Id);
        var chatId = Guid.NewGuid();
        var handler = new AddChatToFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<AddChatToFolderCommandHandler>());

        await handler.Handle(new AddChatToFolderCommand { FolderId = folder.FolderId.ToString(), ChatId = chatId }, CancellationToken.None);
        var result = await handler.Handle(new AddChatToFolderCommand { FolderId = folder.FolderId.ToString(), ChatId = chatId }, CancellationToken.None);

        result.Folder.ChatList.Count(c => c == chatId.ToString()).Should().Be(1);
    }

    [Fact]
    public async Task Handle_FolderNotFound_ThrowsChatFolderNotFoundException()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new AddChatToFolderCommandHandler(ctx, _h.ChatFolderStorage, TestHelper.CreateLogger<AddChatToFolderCommandHandler>());

        var act = () => handler.Handle(new AddChatToFolderCommand
        {
            FolderId = Guid.NewGuid().ToString(),
            ChatId = Guid.NewGuid()
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatFolderNotFoundException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
