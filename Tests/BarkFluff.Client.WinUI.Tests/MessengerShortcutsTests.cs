using BarkFluff.Client.Core.ViewModels;
using static BarkFluff.Client.Core.ViewModels.MessengerShortcutKey;
using static BarkFluff.Client.Core.ViewModels.MessengerShortcutFocus;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerShortcutsTests
{
    [Theory]
    [InlineData(CtrlK)]
    [InlineData(CtrlF)]
    [InlineData(Escape)]
    [InlineData(Up)]
    [InlineData(CtrlUp)]
    [InlineData(CtrlDown)]
    public void ProtectedFocusKeepsItsKeys(MessengerShortcutKey key) =>
        Assert.Equal(MessengerShortcutAction.None, Choose(key, Protected, overlay: true));

    [Fact]
    public void EscapeClosesOneLayerAtATime()
    {
        Assert.Equal(MessengerShortcutAction.CloseOverlay, Choose(Escape, overlay: true, editing: true, reply: true));
        Assert.Equal(MessengerShortcutAction.CancelEdit, Choose(Escape, editing: true, reply: true));
        Assert.Equal(MessengerShortcutAction.CancelReply, Choose(Escape, reply: true));
        Assert.Equal(MessengerShortcutAction.None, Choose(Escape));
        Assert.Equal(MessengerShortcutAction.None, Choose(CtrlK, overlay: true));
    }

    [Fact]
    public void SearchAndEditingRequireTheirOwnContext()
    {
        Assert.Equal(MessengerShortcutAction.FocusSearch, Choose(CtrlK, chat: false));
        Assert.Equal(MessengerShortcutAction.None, Choose(CtrlF, chat: false));
        Assert.Equal(MessengerShortcutAction.FocusSearch, Choose(CtrlF));
        Assert.Equal(MessengerShortcutAction.None, Choose(Up));
        Assert.Equal(MessengerShortcutAction.None, Choose(Up, Composer, empty: false));
        Assert.Equal(MessengerShortcutAction.None, Choose(Up, Composer, editing: true));
        Assert.Equal(MessengerShortcutAction.EditLastMessage, Choose(Up, Composer));
    }

    [Fact]
    public async Task EditsNewestEligibleMessageAndRestoresReplyOnEscape()
    {
        var api = new FakeMessengerService();
        api.Chats.Add(MessengerTestDoubles.CreateChat("a"));
        api.Messages.Add(MessengerTestDoubles.CreateMessage(1, "a", 1, "older"));
        api.Messages.Add(MessengerTestDoubles.CreateMessage(2, "a", 1, "edit me"));
        api.Messages.Add(MessengerTestDoubles.CreateMessage(3, "a", 2, "reply to me"));
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var vm = MessengerTestDoubles.CreateViewModel(api, dispatcher: ui);
        await ui.RunAsync(vm.LoadAsync);
        ui.Post(() =>
        {
            vm.SelectedChat = Assert.Single(vm.Chats);
            var reply = vm.Messages.Single(message => message.Id == 3);
            vm.StartReplyMessageCommand.Execute(reply);
            Assert.True(vm.TryInvokeShortcut(Up, Composer, UnexpectedSearch));
            Assert.Equal(2, vm.EditingMessage?.Id);
            Assert.Equal("edit me", vm.DraftText);
            vm.IsProfileVisible = true;
            Assert.True(vm.TryInvokeShortcut(Escape, Composer, UnexpectedSearch));
            Assert.False(vm.IsProfileVisible);
            Assert.True(vm.IsEditing);
            Assert.True(vm.TryInvokeShortcut(Escape, Composer, UnexpectedSearch));
            Assert.False(vm.IsEditing);
            Assert.Same(reply, vm.ReplyTarget);
            Assert.True(vm.TryInvokeShortcut(Escape, Composer, UnexpectedSearch));
            Assert.Null(vm.ReplyTarget);
            Assert.False(vm.TryInvokeShortcut(Escape, Composer, UnexpectedSearch));
            vm.Reset();
        });
    }

    [Theory]
    [InlineData(CtrlUp)]
    [InlineData(CtrlDown)]
    public void CtrlArrowsKeepParagraphNavigationInMultilineComposer(MessengerShortcutKey key) =>
        Assert.Equal(MessengerShortcutAction.None, Choose(key, Composer, empty: false, multiline: true));

    [Fact]
    public async Task NavigationUsesVisibleSearchResultsAndStopsAtTheEdges()
    {
        var api = new FakeMessengerService();
        api.Chats.AddRange([
            MessengerTestDoubles.CreateChat("a", "Match A"),
            MessengerTestDoubles.CreateChat("b", "Other"),
            MessengerTestDoubles.CreateChat("c", "Match C")
        ]);
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var vm = MessengerTestDoubles.CreateViewModel(api, dispatcher: ui);
        await ui.RunAsync(vm.LoadAsync);
        ui.Post(() =>
        {
            vm.SearchText = "match";
            Assert.Equal(2, vm.ChatSearchResults.Count);
            var first = vm.ChatSearchResults[0];
            var second = vm.ChatSearchResults[1];
            vm.SelectedChat = first;
            Assert.False(vm.TryInvokeShortcut(CtrlUp, Composer, UnexpectedSearch));
            Assert.True(vm.TryInvokeShortcut(CtrlDown, Composer, UnexpectedSearch));
            Assert.Same(second, vm.SelectedChat);
            Assert.Equal("match", vm.SearchText);
            Assert.False(vm.TryInvokeShortcut(CtrlDown, Composer, UnexpectedSearch));
            Assert.True(vm.TryInvokeShortcut(CtrlUp, Composer, UnexpectedSearch));
            Assert.Same(first, vm.SelectedChat);
            vm.Reset();
        });
    }

    private static MessengerShortcutAction Choose(MessengerShortcutKey key, MessengerShortcutFocus focus = Other,
        bool overlay = false, bool chat = true, bool editing = false, bool reply = false, bool empty = true,
        bool multiline = false) =>
        MessengerShortcuts.Choose(key, focus, overlay, chat, editing, reply, empty, multiline);

    private static bool UnexpectedSearch() => throw new InvalidOperationException("Unexpected search focus");
}
