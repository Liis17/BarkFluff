using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Shared;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class ProfileAttachmentsTabViewModelTests
{
    [Fact]
    public async Task LoadMoreAsync_AppendsItemsAndStopsAtTotalCount()
    {
        var messenger = new FakeMessengerService();
        for (var i = 1; i <= 5; i++)
        {
            messenger.Attachments.Add(MessengerTestDoubles.CreateAttachmentInfo(i, MessageAttachmentType.Image));
        }

        var tab = new ProfileAttachmentsTabViewModel(messenger, new StubLocalizationService(), MessageAttachmentType.Image);
        tab.Reset("chat-1");

        await tab.EnsureLoadedAsync();

        Assert.Equal(5, tab.Items.Count);
        Assert.False(tab.HasMore);

        // Повторный вызов после исчерпания списка не должен уходить в сеть заново.
        await tab.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(5, tab.Items.Count);
    }

    [Fact]
    public async Task EnsureLoadedAsync_IsLazyAndCallsOnlyOnce()
    {
        var messenger = new FakeMessengerService();
        messenger.Attachments.Add(MessengerTestDoubles.CreateAttachmentInfo(1, MessageAttachmentType.Document));
        var tab = new ProfileAttachmentsTabViewModel(messenger, new StubLocalizationService(), MessageAttachmentType.Document);
        tab.Reset("chat-1");

        await tab.EnsureLoadedAsync();
        messenger.Attachments.Add(MessengerTestDoubles.CreateAttachmentInfo(2, MessageAttachmentType.Document));
        await tab.EnsureLoadedAsync();

        // Второй вызов EnsureLoadedAsync — no-op, поэтому новое вложение не подхватилось.
        Assert.Single(tab.Items);
    }

    [Fact]
    public void Reset_WithoutChatId_HasNoMoreAndStaysEmpty()
    {
        var messenger = new FakeMessengerService();
        var tab = new ProfileAttachmentsTabViewModel(messenger, new StubLocalizationService(), MessageAttachmentType.Voice);

        tab.Reset(null);

        Assert.True(tab.IsEmpty);
        Assert.False(tab.HasMore);
    }

    [Fact]
    public async Task LoadMoreAsync_Failure_ReportsErrorAndKeepsHasMoreFalse()
    {
        var messenger = new FakeMessengerService { AttachmentsFail = true };
        var tab = new ProfileAttachmentsTabViewModel(messenger, new StubLocalizationService(), MessageAttachmentType.Voice);
        tab.Reset("chat-1");

        await tab.EnsureLoadedAsync();

        Assert.Equal("Error_AttachmentsLoadFailed", tab.ErrorMessage);
        Assert.Empty(tab.Items);
        Assert.False(tab.HasMore);
    }
}
