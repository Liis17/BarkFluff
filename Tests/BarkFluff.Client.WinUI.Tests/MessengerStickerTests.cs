using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerStickerTests
{
    [Fact]
    public async Task PickerLoadsListOnceAndSharesLazyPackRequestsWithCovers()
    {
        var catalog = new Catalog("a", "b", "c", "d", "e");
        var picker = CreatePicker(catalog);
        await picker.OpenAsync(() => true);

        Assert.Equal(6, picker.Tabs.Count);
        Assert.Equal("Messenger_StickerRecent", picker.SelectedTab?.Name);
        Assert.True(picker.IsEmpty);
        Assert.Equal(1, catalog.ListCalls);
        Assert.Equal(0, catalog.PackCalls);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.Load = async id =>
        {
            await release.Task;
            return [Sticker(id)];
        };

        var covers = Task.WhenAll(picker.Tabs.Skip(1).Select(picker.LoadCoverAsync));
        picker.SelectedTab = picker.Tabs[1];
        var active = picker.LoadAsync();
        Assert.Equal(3, catalog.PackCalls);
        release.SetResult();
        await Task.WhenAll(covers, active);

        Assert.Equal(5, catalog.PackCalls);
        Assert.InRange(catalog.Peak, 1, 3);
        Assert.Equal("https://example.test/preview.webp", picker.Tabs[1].CoverUrl);
        Assert.Equal("a-file", Assert.Single(picker.Stickers).FileId);
        await picker.OpenAsync(() => true);
        Assert.Equal(5, catalog.PackCalls);
        Assert.Equal(1, catalog.ListCalls);
    }

    [Fact]
    public async Task RecentKeepsOrderAndLimitAndOmitsDeletedStickersAcrossScopedSessions()
    {
        var catalog = new Catalog("a");
        var items = Enumerable.Range(0, 34).Select(index => Sticker(index.ToString())).ToList();
        catalog.Load = _ => Task.FromResult<IReadOnlyList<StickerInfo>?>(items.ToArray());
        var picker = CreatePicker(catalog);

        foreach (var sticker in items)
            await picker.RememberAsync(new("a", sticker.Id, sticker.FileId, sticker.Emoji, sticker.PreviewUrl));
        await picker.RememberAsync(new("a", "20", "20-file", "x", ""));
        picker.Reset();
        Assert.Empty(picker.Stickers);
        Assert.Empty(picker.Tabs);

        picker.StartSession("node", 1, default, () => true);
        await picker.OpenAsync(() => true);
        Assert.Equal(32, picker.Stickers.Count);
        picker.Reset();

        items.RemoveAt(33);
        picker.StartSession("node", 1, default, () => true);
        await picker.OpenAsync(() => true);
        Assert.Equal(31, picker.Stickers.Count);
        Assert.Equal(new[] { "20", "32", "31" }, picker.Stickers.Take(3).Select(item => item.Id));
        Assert.DoesNotContain(picker.Stickers, item => item.Id is "33" or "0" or "1");

        foreach (var (node, user) in new[] { ("other", 1L), ("node", 2L) })
        {
            picker.Reset();
            picker.StartSession(node, user, default, () => true);
            await picker.OpenAsync(() => true);
            Assert.True(picker.IsEmpty);
        }
    }

    [Fact]
    public async Task PickerRetriesFailuresAndSearchesSelectedTabByEmoji()
    {
        var catalog = new Catalog("a") { FailList = true };
        var picker = CreatePicker(catalog);
        await picker.OpenAsync(() => true);
        Assert.True(picker.HasError);
        Assert.False(picker.IsEmpty);

        catalog.FailList = false;
        await picker.LoadAsync();
        Assert.True(picker.IsEmpty);
        catalog.Load = _ => Task.FromResult<IReadOnlyList<StickerInfo>?>(null);
        picker.SelectedTab = picker.Tabs[1];
        Assert.True(picker.HasError);

        catalog.Load = _ => Task.FromResult<IReadOnlyList<StickerInfo>?>(
            [Sticker("a"), new StickerInfo { Id = "b", FileId = "b-file", Emoji = "\U0001F40D" }]);
        await picker.LoadAsync();
        picker.Search = " \U0001F40D ";
        Assert.Equal("b", Assert.Single(picker.Stickers).Id);
        picker.Search = "\U0001F98A";
        Assert.True(picker.IsEmpty);
    }

    [Fact]
    public async Task ResetWhileLoadingDiscardsOldResponse()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<StickerInfo>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new Catalog("a") { Load = _ => pending.Task };
        var picker = CreatePicker(catalog);
        await picker.OpenAsync(() => true);
        picker.SelectedTab = picker.Tabs[1];
        var loading = picker.LoadAsync();

        picker.Reset();
        picker.StartSession("new-node", 2, default, () => true);
        await picker.OpenAsync(() => true);
        pending.SetResult([Sticker("old")]);
        await loading;

        Assert.True(picker.IsEmpty);
        Assert.Empty(picker.Stickers);
        Assert.Equal(2, catalog.ListCalls);
    }

    [Fact]
    public async Task SendUsesFileIdAndReplyAndKeepsDraft()
    {
        var (vm, service, sticker) = await OpenMessengerAsync();
        vm.StartReplyMessageCommand.Execute(Assert.Single(vm.Messages));
        vm.DraftText = "draft";
        var calls = 0;
        service.SendAttachments = (chat, text, reply, ids) =>
        {
            calls++;
            Assert.Equal("a", chat);
            Assert.Empty(text);
            Assert.Equal(50, reply);
            Assert.Equal("a-file", Assert.Single(ids));
            return SentAsync(chat, reply);
        };

        Assert.True(await vm.SendStickerAsync(sticker));
        Assert.Equal(1, calls);
        Assert.Null(vm.ReplyTarget);
        Assert.Equal("draft", vm.DraftText);
        var sent = vm.Messages.Single(message => message.Id == 51);
        Assert.True(sent.HasReply);
        Assert.False(sent.IsStickerOnly);
        Assert.True(sent.UseOwnBubbleText);
        Assert.Empty(sent.MediaAttachments);
        Assert.Equal("https://example.test/original.webp", Assert.Single(sent.StickerAttachments).StickerUrl);
    }

    [Fact]
    public async Task FailedSendPreservesReplyAndDoesNotAddRecent()
    {
        var (vm, service, sticker) = await OpenMessengerAsync();
        var reply = Assert.Single(vm.Messages);
        vm.StartReplyMessageCommand.Execute(reply);
        service.SendAttachments = (_, _, _, _) => Task.FromResult<(ErrorReturner, MessageModel?)>((null!, null));

        Assert.False(await vm.SendStickerAsync(sticker));
        Assert.Same(reply, vm.ReplyTarget);
        Assert.Equal("Messenger_ActionFailed", vm.ActionError);
        var picker = vm.StickerPicker!;
        picker.SelectedTab = picker.Tabs[0];
        await picker.LoadAsync();
        Assert.True(picker.IsEmpty);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("read-only")]
    [InlineData("edit")]
    [InlineData("chat")]
    [InlineData("user")]
    [InlineData("node")]
    public async Task SendRejectsUnavailableComposerOrStalePicker(string change)
    {
        var (vm, service, sticker) = await OpenMessengerAsync();

        switch (change)
        {
            case "private":
                vm.SelectedChat = vm.Chats.Single(chat => chat.IsPrivate);
                break;
            case "read-only":
                vm.SelectedChat = new ChatItemViewModel(MessengerTestDoubles.CreateChat("restricted", peerUserId: 2),
                    "", "", "", "", 2, new StubLocalizationService(), accountStatus: AccountStatus.Blocked);
                Assert.False(vm.SelectedChat.CanSend);
                break;
            case "edit":
                vm.StartEditMessageCommand.Execute(Assert.Single(vm.Messages));
                Assert.True(vm.IsEditing);
                break;
            case "chat":
                vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
                break;
            case "user":
                service.CurrentUserId = 2;
                break;
            case "node":
                service.CurrentNodeAddress = "other-node";
                break;
        }

        var unexpectedCalls = 0;
        service.SendAttachments = (chat, _, _, _) =>
        {
            unexpectedCalls++;
            return SentAsync(chat);
        };
        Assert.False(await vm.SendStickerAsync(sticker));
        Assert.Equal(0, unexpectedCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendCompletionDoesNotModifyNewChatOrSession(bool reset)
    {
        var (vm, service, sticker) = await OpenMessengerAsync();
        var pending = new TaskCompletionSource<(ErrorReturner, MessageModel?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SendAttachments = (chat, _, _, _) =>
        {
            Assert.Equal("a", chat);
            return pending.Task;
        };

        var sending = vm.SendStickerAsync(sticker);
        Assert.True(vm.StickerPicker!.IsSending);
        if (reset)
        {
            vm.Reset();
            service.CurrentUserId = 2;
            await vm.LoadAsync();
        }
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
        vm.DraftText = "other";

        pending.SetResult(await SentAsync("a"));
        Assert.False(await sending);
        Assert.Empty(vm.Messages);
        Assert.Null(vm.ActionError);
        Assert.Equal("other", vm.DraftText);
        Assert.False(vm.StickerPicker.IsSending);
    }

    [Fact]
    public async Task FeedUsesConfiguredStickerSizeAndNeutralPresentation()
    {
        var service = new FakeMessengerService();
        var prefs = new SettingsPreferences(new TestApplicationDataStore());
        var vm = MessengerTestDoubles.CreateViewModel(service, stickerPreferences: prefs);
        Assert.Equal(160, vm.StickerSize);
        var settings = new BarkFluff.Client.Core.ViewModels.Settings.SettingsPersonalizationViewModel(prefs);
        await settings.LoadAsync();
        await settings.SetChatStickerSizeAsync(300);
        Assert.Equal(240, settings.ChatStickerSizeDp);
        await vm.LoadStickerPreferencesAsync();
        Assert.Equal(240, vm.StickerSize);

        service.Chats.Add(MessengerTestDoubles.CreateChat("a"));
        service.Messages.Add((await SentAsync("a")).Item2!);
        await vm.LoadAsync();
        vm.SelectedChat = Assert.Single(vm.Chats);

        var row = Assert.Single(vm.Messages);
        Assert.True(row.IsMine);
        Assert.True(row.IsStickerOnly);
        Assert.True(row.UseCompactBubblePadding);
        Assert.True(row.HasMetadataBelowMedia);
        Assert.False(row.UseOwnBubbleText);

        var changes = new List<string?>();
        row.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        row.Reply = new ReplyContentViewModel(50, "Sender", "Original", false);
        Assert.False(row.IsStickerOnly);
        Assert.Contains(nameof(row.IsStickerOnly), changes);
        Assert.Contains(nameof(row.UseOwnBubbleText), changes);
        row.Reply = null;
        Assert.True(row.IsStickerOnly);

        row.Forwards = [new ForwardedContentViewModel("Sender", 50, "Original")];
        Assert.False(row.IsStickerOnly);
        row.Forwards = [];
        row.Text = "Caption";
        Assert.False(row.IsStickerOnly);
        row.Text = "";
        var sticker = Assert.Single(row.Attachments);
        row.Attachments = [sticker, sticker];
        Assert.False(row.IsStickerOnly);
    }

    private static StickerPickerViewModel CreatePicker(Catalog catalog)
    {
        var picker = new StickerPickerViewModel(catalog, new TestApplicationDataStore(),
            new StubLocalizationService(), new InlineUiDispatcher());
        picker.StartSession("node", 1, default, () => true);
        return picker;
    }

    private static async Task<(MessengerViewModel, FakeMessengerService, PickerSticker)> OpenMessengerAsync()
    {
        var service = new FakeMessengerService();
        service.Chats.AddRange([
            MessengerTestDoubles.CreateChat("a"),
            MessengerTestDoubles.CreateChat("b"),
            MessengerTestDoubles.CreateChat("private", chatType: ChatType.Private)
        ]);
        service.Messages.Add(MessengerTestDoubles.CreateMessage(50, "a", 1));
        var picker = CreatePicker(new("a"));
        var vm = MessengerTestDoubles.CreateViewModel(service, stickerPicker: picker);
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "a");
        await vm.OpenStickerPickerAsync();
        picker.SelectedTab = picker.Tabs[1];
        return (vm, service, Assert.Single(picker.Stickers));
    }

    private static Task<(ErrorReturner, MessageModel?)> SentAsync(string chat, long reply = 0)
    {
        var message = MessengerTestDoubles.CreateMessage(51, chat, 1, "");
        if (reply != 0)
            message.ReplyTo = new ReplyPreviewModel { MessageId = reply, SenderName = "Sender", TextPreview = "Original" };
        message.Attachments.Add(new AttachmentsModel
        {
            Type = MessageAttachmentType.Sticker,
            FileId = "a-file",
            PreviewUrl = "https://example.test/original.webp"
        });
        return Task.FromResult<(ErrorReturner, MessageModel?)>((new(true), message));
    }

    private static StickerInfo Sticker(string id) => new()
    {
        Id = id, FileId = id + "-file", Emoji = "\U0001F431", PreviewUrl = "https://example.test/preview.webp"
    };

    private sealed class Catalog(params string[] ids) : IStickerCatalog
    {
        private int _active;
        private int _packCalls;
        public int PackCalls => _packCalls;
        public int Peak { get; private set; }
        public int ListCalls { get; private set; }
        public bool FailList { get; set; }
        public Func<string, Task<IReadOnlyList<StickerInfo>?>> Load { get; set; } =
            id => Task.FromResult<IReadOnlyList<StickerInfo>?>([Sticker(id)]);

        public Task<IReadOnlyList<StickerPackInfo>?> ListAsync(string node, long user, CancellationToken token)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<StickerPackInfo>?>(FailList ? null :
                ids.Select(id => new StickerPackInfo { Id = id, Name = id, CoverStickerId = id }).ToArray());
        }

        public async Task<IReadOnlyList<StickerInfo>?> LoadPackAsync(string node, long user, string packId, CancellationToken token)
        {
            Interlocked.Increment(ref _packCalls);
            var active = Interlocked.Increment(ref _active);
            lock (this)
                Peak = Math.Max(Peak, active);
            try { return await Load(packId); }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
