using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerAttachmentTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"barkfluff-test-{Guid.NewGuid():N}");

    public MessengerAttachmentTests() => File.WriteAllBytes(_path, [1, 2, 3]);

    public void Dispose() => File.Delete(_path);

    [Theory]
    [InlineData("фото.jpg", null, UploadFileType.MessageAttachmentImage)]
    [InlineData("анимация.gif", "application/octet-stream", UploadFileType.MessageAttachmentGif)]
    [InlineData("видео.mp4", null, UploadFileType.MessageAttachmentVideo)]
    [InlineData("отчёт.pdf", "application/pdf", UploadFileType.MessageAttachmentDocument)]
    [InlineData("video.unknown", "video/mp4", UploadFileType.MessageAttachmentVideo)]
    public void MIMEAndExtensionSelectUploadType(string name, string? mime, UploadFileType type) =>
        Assert.Equal(type, PendingAttachment.ResolveType(name, mime));

    [Fact]
    public async Task BatchLimitPreservesOrderAndAllowsAttachmentOnlyMessages()
    {
        var (vm, service) = await OpenChatAsync();
        string[] names = ["фото.jpg", "анимация.gif", "видео.mp4", .. Enumerable.Range(1, 8).Select(i => $"{i}.pdf")];
        await AddAsync(vm, names);
        Assert.Equal(10, vm.AttachmentComposer.Files.Count);
        Assert.Equal("Messenger_AttachmentLimit", vm.AttachmentComposer.Error);
        vm.AttachmentComposer.Files[0].AsDocument = true;
        vm.AttachmentComposer.Files[2].AsDocument = true;
        service.UploadAttachment = (name, type, _, _) =>
        {
            if (name == names[0])
                Assert.Equal(UploadFileType.MessageAttachmentDocument, type);
            if (name == names[1])
                Assert.Equal(UploadFileType.MessageAttachmentGif, type);
            if (name == names[2])
                Assert.Equal(UploadFileType.MessageAttachmentVideo, type);
            return Task.FromResult<(ErrorReturner, string?)>((new(true), name));
        };
        service.SendAttachments = (chat, text, reply, ids) =>
        {
            Assert.Equal("a", chat);
            Assert.Empty(text);
            Assert.Equal(0, reply);
            Assert.Equal(names[..10], ids);
            return SentAsync(chat, text);
        };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.False(vm.AttachmentComposer.HasFiles);
    }

    [Fact]
    public async Task BatchLimitsConcurrentUploadsAndSendsInComposerOrder()
    {
        var (vm, service) = await OpenChatAsync();
        string[] names = ["first.pdf", "second.pdf", "third.pdf", "fourth.pdf"];
        await AddAsync(vm, names);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOthers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new List<string>();
        service.UploadAttachment = async (name, _, _, _) =>
        {
            started.Add(name);
            if (name == names[2])
                thirdStarted.SetResult();
            await (name == names[0] ? releaseFirst.Task : releaseOthers.Task);
            return (new(true), name);
        };
        IReadOnlyList<string>? sent = null;
        service.SendAttachments = (chat, text, _, ids) =>
        {
            sent = ids;
            return SentAsync(chat, text);
        };

        var sending = vm.SendCommand.ExecuteAsync(null);
        var initiallyStarted = started.ToArray();
        releaseOthers.SetResult();
        await thirdStarted.Task;
        releaseFirst.SetResult();
        await sending;

        Assert.Equal(names[..2], initiallyStarted);
        Assert.Equal(names, sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChecksCombinedStorageOnceAndOnlyBlocksOnKnownShortage(bool checkFails)
    {
        var (vm, service) = await OpenChatAsync();
        await AddAsync(vm, "first.pdf", "second.pdf");
        var checks = 0;
        service.GetAttachmentStorageAvailable = _ =>
        {
            checks++;
            return checkFails
                ? Task.FromException<long?>(new IOException())
                : Task.FromResult<long?>(5);
        };
        var uploads = 0;
        service.UploadAttachment = (name, _, _, _) =>
        {
            uploads++;
            return Task.FromResult<(ErrorReturner, string?)>((new(true), name));
        };
        service.SendAttachments = (chat, text, _, _) => SentAsync(chat, text);

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal(1, checks);
        Assert.Equal(checkFails ? 2 : 0, uploads);
        if (!checkFails)
            Assert.Equal("Messenger_AttachmentStorageFull", vm.AttachmentComposer.Error);
    }

    [Fact]
    public async Task CancelAndRetryOneFilePreservesReadyFileCaptionAndReply()
    {
        var (vm, service) = await OpenChatAsync();
        vm.StartReplyMessageCommand.Execute(Assert.Single(vm.Messages));
        vm.DraftText = "caption";
        await AddAsync(vm, "ready.gif", "retry.pdf");
        var attempt = new TaskCompletionSource<(ErrorReturner, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uploads = new List<string>();
        service.UploadAttachment = (name, _, progress, token) =>
        {
            uploads.Add(name);
            if (uploads.Count == 2)
            {
                progress.Report(0.4);
                return attempt.Task.WaitAsync(token);
            }
            if (uploads.Count == 3)
                return Task.FromResult<(ErrorReturner, string?)>((new(false, errorResourceKey: "Messenger_AttachmentStorageFull"), null));
            return Task.FromResult<(ErrorReturner, string?)>((new(true), name));
        };
        var sends = 0;
        service.SendAttachments = (chat, text, reply, ids) =>
        {
            sends++;
            Assert.Equal("caption", text);
            Assert.Equal(50, reply);
            Assert.Equal(new[] { "ready.gif", "retry.pdf" }, ids);
            return SentAsync(chat, text);
        };

        var sending = vm.SendCommand.ExecuteAsync(null);
        var retry = vm.AttachmentComposer.Files[1];
        Assert.Equal(40, retry.Progress);
        retry.CancelCommand.Execute(null);
        await sending;
        Assert.Equal("Messenger_AttachmentCancelled", retry.Status);
        Assert.Equal("caption", vm.DraftText);
        Assert.Equal(0, sends);

        await vm.RetryAttachmentAsync(retry);
        Assert.Equal("Messenger_AttachmentStorageFull", retry.Status);
        await vm.RetryAttachmentAsync(retry);
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "ready.gif", "retry.pdf", "retry.pdf", "retry.pdf" }, uploads);
        Assert.Equal(1, sends);
        Assert.Empty(vm.AttachmentComposer.Files);
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("reset")]
    [InlineData("exit")]
    public async Task LeavingComposerCancelsUploadAndDeletesTemporaryInput(string reason)
    {
        var (vm, service) = await OpenChatAsync();
        await vm.AddAttachmentsAsync(_ => Task.FromResult<IReadOnlyList<AttachmentSourceFile>>(
            [new(_path, "изображение.png", IsTemporary: true)]));
        var response = new TaskCompletionSource<(ErrorReturner, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken uploadToken = default;
        service.UploadAttachment = (_, _, _, token) =>
        {
            uploadToken = token;
            return response.Task;
        };
        var sends = 0;
        service.SendAttachments = (chat, text, _, _) =>
        {
            sends++;
            return SentAsync(chat, text);
        };
        var send = vm.SendCommand.ExecuteAsync(null);

        Task? exiting = null;
        if (reason == "reset")
            vm.Reset();
        else if (reason == "exit")
            exiting = vm.CancelAttachmentsAsync();
        else
            vm.SelectedChat = vm.Chats.Single(c => c.Id == "b");
        Assert.True(uploadToken.IsCancellationRequested);
        Assert.Empty(vm.AttachmentComposer.Files);
        response.SetResult((new(true), "late-file-id"));
        await send;
        await (exiting ?? vm.CancelAttachmentsAsync());
        Assert.Equal(0, sends);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task LateClipboardResultIsDeletedAndPrivateChatDoesNotReadClipboard()
    {
        var (vm, _) = await OpenChatAsync();
        var picker = new TaskCompletionSource<IReadOnlyList<AttachmentSourceFile>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adding = vm.AddAttachmentsAsync(_ => picker.Task);
        vm.SelectedChat = vm.Chats.Single(c => c.Id == "private");
        picker.SetResult([new(_path, "clipboard.png", IsTemporary: true)]);
        await adding;

        await vm.AddAttachmentsAsync(_ => throw new InvalidOperationException("Буфер недоступен в приватном чате"));
        Assert.True(vm.HasPrivateAttachmentRestriction);
        Assert.False(vm.CanAttach);
        Assert.False(File.Exists(_path));
        Assert.Empty(vm.AttachmentComposer.Files);
        Assert.Null(vm.AttachmentComposer.Error);
    }

    [Fact]
    public async Task FileAtRequestLimitIsRejectedBeforeUpload()
    {
        var (vm, _) = await OpenChatAsync();
        using (var file = File.OpenWrite(_path))
            file.SetLength(BarkFluff.WebApi.Core.WebApi.MaxFileUploadRequestSize);
        await AddAsync(vm, "big.zip");
        Assert.Equal("Messenger_AttachmentTooLarge", vm.AttachmentComposer.Error);
        Assert.Empty(vm.AttachmentComposer.Files);
    }

    [Fact]
    public async Task ChatListSnapshotUsesAttachmentTypeWhenTextIsEmpty()
    {
        var service = new FakeMessengerService();
        var chat = MessengerTestDoubles.CreateChat("a");
        chat.LastMessage = new() { Id = 10, Content = new() };
        chat.LastMessage.Content.Attachments.Add(new MessageAttachment { Type = MessageAttachmentType.Image });
        service.Chats.Add(chat);
        var vm = MessengerTestDoubles.CreateViewModel(service);
        await vm.LoadAsync();
        Assert.Equal("Messenger_AttachmentPhoto", Assert.Single(vm.Chats).Preview);
    }

    [Fact]
    public async Task ReceivedGifUsesAnimatedOriginalWhenServerProvidesJpegPreview()
    {
        var service = new FakeMessengerService();
        service.Chats.Add(MessengerTestDoubles.CreateChat("a"));
        var message = MessengerTestDoubles.CreateMessage(50, "a", 2, string.Empty);
        message.Attachments.Add(new AttachmentsModel
        {
            Type = MessageAttachmentType.Gif,
            FileId = "animated",
            FileName = "анимация.gif",
            PreviewUrl = "https://example.invalid/still.jpg",
        });
        service.Messages.Add(message);
        service.FileUrl = id => Task.FromResult($"https://example.invalid/{id}.gif");
        var vm = MessengerTestDoubles.CreateViewModel(service);
        await vm.LoadAsync();
        vm.SelectedChat = Assert.Single(vm.Chats);

        var gif = Assert.Single(Assert.Single(vm.Messages).MediaAttachments);
        Assert.Equal("анимация.gif", gif.FileName);
        Assert.Equal("https://example.invalid/animated.gif", gif.PreviewUrl);
    }

    private Task AddAsync(MessengerViewModel vm, params string[] names) =>
        vm.AddAttachmentsAsync(_ => Task.FromResult<IReadOnlyList<AttachmentSourceFile>>(names.Select(name =>
            new AttachmentSourceFile(_path, name)).ToArray()));

    private static Task<(ErrorReturner, MessageModel?)> SentAsync(string chat, string text) =>
        Task.FromResult<(ErrorReturner, MessageModel?)>((new(true), MessengerTestDoubles.CreateMessage(51, chat, 1, text)));

    private static async Task<(MessengerViewModel, FakeMessengerService)> OpenChatAsync()
    {
        var service = new FakeMessengerService();
        service.Chats.AddRange([
            MessengerTestDoubles.CreateChat("a"),
            MessengerTestDoubles.CreateChat("b"),
            MessengerTestDoubles.CreateChat("private", chatType: ChatType.Private)
        ]);
        service.Messages.Add(MessengerTestDoubles.CreateMessage(50, "a", 2));
        var vm = MessengerTestDoubles.CreateViewModel(service);
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single(c => c.Id == "a");
        return (vm, service);
    }
}
