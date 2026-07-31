using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Shared;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessageActionsTests
{
    [Fact]
    public async Task StartForwardMessage_OffersEveryChatExceptPrivateOnes()
    {
        var (viewModel, message) = await CreateWithMessageAsync(withPrivateChat: true);

        viewModel.StartForwardMessageCommand.Execute(message);

        Assert.True(viewModel.IsForwardVisible);
        Assert.Equal(["a", "b"], viewModel.ForwardTargets.Select(target => target.ChatId).Order());
        // Owner нужен разметке: в WinUI нет RelativeSource AncestorType, командой владеет вьюмодель.
        Assert.All(viewModel.ForwardTargets, target => Assert.Same(viewModel, target.Owner));
    }

    [Fact]
    public async Task ToggleForwardTarget_DrivesSubmitAvailability()
    {
        var (viewModel, message) = await CreateWithMessageAsync();
        viewModel.StartForwardMessageCommand.Execute(message);
        var target = viewModel.ForwardTargets.First();

        Assert.False(viewModel.CanSubmitForward);

        viewModel.ToggleForwardTargetCommand.Execute(target);
        Assert.True(target.IsSelected);
        Assert.True(viewModel.CanSubmitForward);

        viewModel.ToggleForwardTargetCommand.Execute(target);
        Assert.False(viewModel.CanSubmitForward);
    }

    [Fact]
    public async Task CancelForward_ClearsTargetsAndComment()
    {
        var (viewModel, message) = await CreateWithMessageAsync();
        viewModel.StartForwardMessageCommand.Execute(message);
        viewModel.ForwardComment = "look";

        viewModel.CancelForwardCommand.Execute(null);

        Assert.False(viewModel.IsForwardVisible);
        Assert.Empty(viewModel.ForwardTargets);
        Assert.Empty(viewModel.ForwardComment);
    }

    [Fact]
    public async Task RequestDeleteMessage_OpensConfirmationAndCancelClosesIt()
    {
        var (viewModel, message) = await CreateWithMessageAsync();

        viewModel.RequestDeleteMessageCommand.Execute(message);
        Assert.True(viewModel.IsDeleteConfirmVisible);

        viewModel.CancelDeleteMessageCommand.Execute(null);
        Assert.False(viewModel.IsDeleteConfirmVisible);
    }

    [Fact]
    public async Task StartReplyMessage_ShowsComposerHintUntilCancelled()
    {
        var (viewModel, message) = await CreateWithMessageAsync();

        viewModel.StartReplyMessageCommand.Execute(message);
        Assert.True(viewModel.IsReplying);
        Assert.True(viewModel.IsComposerHintVisible);

        viewModel.CancelComposerHintCommand.Execute(null);
        Assert.False(viewModel.IsComposerHintVisible);
        Assert.Null(viewModel.ReplyTarget);
    }

    [Fact]
    public async Task StartEditMessage_PutsTheTextIntoTheComposer()
    {
        var (viewModel, message) = await CreateWithMessageAsync();

        viewModel.StartEditMessageCommand.Execute(message);

        Assert.True(viewModel.IsEditing);
        Assert.Equal(message.Text, viewModel.DraftText);
    }

    [Fact]
    public async Task ScrollToOriginal_IgnoresMessagesThatAreNotReplyQuotes()
    {
        var (viewModel, message) = await CreateWithMessageAsync();
        viewModel.ScrollRequest = null;

        viewModel.ScrollToOriginalCommand.Execute(message);

        Assert.Null(viewModel.ScrollRequest);
    }

    private static async Task<(MessengerViewModel ViewModel, MessageItemViewModel Message)> CreateWithMessageAsync(bool withPrivateChat = false)
    {
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alice", peerUserId: 2));
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("b", "Bob", peerUserId: 3));
        if (withPrivateChat)
        {
            messenger.Chats.Add(MessengerTestDoubles.CreateChat("p", "Private", peerUserId: 4, chatType: ChatType.Private));
        }

        messenger.Messages.Add(MessengerTestDoubles.CreateMessage(1, "a", senderId: 1, "hello"));
        var viewModel = MessengerTestDoubles.CreateViewModel(messenger);
        await viewModel.LoadAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");
        return (viewModel, viewModel.Messages.Single());
    }
}
