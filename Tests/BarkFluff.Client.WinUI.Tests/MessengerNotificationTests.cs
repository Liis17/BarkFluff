using System.Xml.Linq;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.Core.ViewModels.Settings;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Time.Testing;
using static BarkFluff.Client.WinUI.Tests.MessengerTestDoubles;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerNotificationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatSoundAppliesToNotificationsAndPreviewUnlessMuted(bool muted)
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        await dispatcher.RunAsync(async () =>
        {
            var sounds = new SoundStore();
            var fixture = new Fixture(dispatcher: dispatcher, sounds: sounds);
            if (muted)
                fixture.Messenger.MutedChats = [new() { ChatId = "known" }];
            await fixture.ViewModel.LoadAsync();
            fixture.ViewModel.SelectedChat = Assert.Single(fixture.ViewModel.Chats);

            await fixture.ViewModel.EditChatSoundAsync((current, canPreview, preview, _) =>
            {
                Assert.Equal(ChatNotificationSound.Default, current);
                Assert.Equal(!muted, canPreview());
                preview(ChatNotificationSound.Silent);
                preview(ChatNotificationSound.Mail);
                return Task.FromResult<ChatNotificationSound?>(ChatNotificationSound.Silent);
            });

            Assert.Equal(ChatNotificationSound.Silent, sounds.Sound);
            Assert.Equal(new MessageNotificationTarget(fixture.Messenger.CurrentNodeAddress!, 1, "known"), sounds.Target);
            fixture.Receive(1);
            if (muted)
            {
                Assert.Empty(fixture.Presenter.Previews);
                Assert.Empty(fixture.Presenter.Shown);
            }
            else
            {
                Assert.Equal(ChatNotificationSound.Mail, Assert.Single(fixture.Presenter.Previews));
                Assert.Equal(ChatNotificationSound.Silent, Assert.Single(fixture.Presenter.Shown).Sound);
            }
        });
    }

    [Fact]
    public async Task ChatSoundEditorErrorsAreNotReportedAsStorageErrors()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        await dispatcher.RunAsync(async () =>
        {
            var fixture = new Fixture(dispatcher: dispatcher, sounds: new SoundStore());
            await fixture.ViewModel.LoadAsync();
            fixture.ViewModel.SelectedChat = Assert.Single(fixture.ViewModel.Chats);

            var error = new InvalidOperationException("Editor failed");
            Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.ViewModel.EditChatSoundAsync((_, _, _, _) => Task.FromException<ChatNotificationSound?>(error))));
            Assert.Null(fixture.ViewModel.ActionError);
        });
    }

    [Fact]
    public async Task ResolveSoundUsesDefaultWhenStoreFails()
    {
        var sounds = new SoundStore { LoadError = new IOException("Store is unavailable") };
        var notifications = new MessageNotificationService(new CapturingPresenter(), new StubLocalizationService(), sounds);

        Assert.Equal(ChatNotificationSound.Default,
            await notifications.ResolveSoundAsync(new("node", 1, "known"), CancellationToken.None));
    }

    [Fact]
    public async Task ChatSoundAppliesAgainAfterMuteExpires()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        await dispatcher.RunAsync(async () =>
        {
            var clock = new FakeTimeProvider();
            var duration = TimeSpan.FromMinutes(1);
            var sounds = new SoundStore();
            var fixture = new Fixture(dispatcher: dispatcher, sounds: sounds, timeProvider: clock);
            fixture.Messenger.MutedChats =
                [new() { ChatId = "known", MutedUntil = Timestamp.FromDateTimeOffset(clock.GetUtcNow() + duration) }];
            await sounds.SaveAsync(new(fixture.Messenger.CurrentNodeAddress, 1, "known"), ChatNotificationSound.Mail,
                CancellationToken.None);
            await fixture.ViewModel.LoadAsync();

            fixture.Receive(1);
            Assert.Empty(fixture.Presenter.Shown);
            clock.Advance(duration);
            fixture.Receive(2);
            Assert.Equal(ChatNotificationSound.Mail, Assert.Single(fixture.Presenter.Shown).Sound);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatSoundDialogIsCancelledWhenChatOrSessionChanges(bool changeSession)
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        await dispatcher.RunAsync(async () =>
        {
            var sounds = new SoundStore();
            var fixture = new Fixture(dispatcher: dispatcher, sounds: sounds);
            fixture.Messenger.Chats.Add(CreateChat("other"));
            await fixture.ViewModel.LoadAsync();
            fixture.ViewModel.SelectedChat = fixture.ViewModel.Chats.Single(chat => chat.Id == "known");

            await fixture.ViewModel.EditChatSoundAsync(async (_, canPreview, preview, lifetime) =>
            {
                if (changeSession)
                {
                    fixture.Messenger.CurrentUserId = 5;
                    await fixture.ViewModel.LoadAsync();
                }
                else
                {
                    fixture.ViewModel.SelectedChat = fixture.ViewModel.Chats.Single(chat => chat.Id == "other");
                }
                Assert.True(lifetime.IsCancellationRequested);
                Assert.False(canPreview());
                preview(ChatNotificationSound.Mail);
                return ChatNotificationSound.Mail;
            });

            Assert.Null(sounds.Target);
            Assert.Empty(fixture.Presenter.Previews);
        });
    }

    [Theory]
    [InlineData(ChatType.Regular, AccountStatus.Active, true)]
    [InlineData(ChatType.Regular, AccountStatus.Blocked, false)]
    [InlineData(ChatType.Private, AccountStatus.Active, false)]
    public async Task ReplyActionRequiresWritableRegularChat(ChatType type, AccountStatus status, bool canReply)
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        await dispatcher.RunAsync(async () =>
        {
            var fixture = new Fixture(dispatcher: dispatcher);
            fixture.Messenger.Chats[0] = CreateChat("known", peerUserId: 2, chatType: type);
            fixture.Messenger.UserData = new() { AccountStatus = status };
            await fixture.ViewModel.LoadAsync();
            if (type == ChatType.Private)
                fixture.Realtime.Raise(new MessageUpdate("known", true, 42, MessageUpdateKind.New,
                    Encrypted: new EncryptedMessage { Id = 42, ChatId = "known", SenderId = 2 }));
            else
                fixture.Receive(42);

            var action = Assert.Single(fixture.Presenter.Shown).Reply;
            Assert.Equal(canReply ? 42L : null, action?.MessageId);
        });
    }

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, false, false, true, true)]
    public async Task AppliesDeviceMuteAndForegroundPolicy(
        bool enabled, bool muted, bool selected, bool active, bool shows)
    {
        var fixture = new Fixture(enabled);
        if (muted)
        {
            fixture.Messenger.MutedChats = [new() { ChatId = "known" }];
        }

        await fixture.ViewModel.LoadAsync();
        if (selected)
        {
            fixture.ViewModel.SelectedChat = Assert.Single(fixture.ViewModel.Chats);
        }

        fixture.ViewModel.SetNotificationWindowState(active, messengerVisible: true);
        fixture.Receive(1);
        Assert.Equal(shows ? 1 : 0, fixture.Presenter.Shown.Count);
    }

    [Fact]
    public async Task OffPageMuteAppliesBeforeAnyChatLookup()
    {
        var fixture = new Fixture();
        fixture.Messenger.MutedChats = [new() { ChatId = "off-page" }];
        await fixture.ViewModel.LoadAsync();
        fixture.Receive(1, "off-page");
        fixture.Receive(2, "other-off-page");

        Assert.DoesNotContain(fixture.ViewModel.Chats, chat => chat.Id == "off-page");
        Assert.Equal("other-off-page", Assert.Single(fixture.Presenter.Shown).Target.ChatId);
    }

    [Fact]
    public async Task RealtimeSkipsOwnSystemEditsAndDuplicates()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.LoadAsync();

        fixture.Realtime.Raise(new IncomingMessage("known", CreateMessage(1, "known", 1)));
        var system = CreateMessage(2, "known", 2);
        system.Type = MessageContentType.System;
        fixture.Realtime.Raise(new IncomingMessage("known", system));
        var message = CreateMessage(3, "known", 2);
        fixture.Realtime.Raise(new MessageUpdate("known", false, 3, MessageUpdateKind.Edit, Message: message));

        fixture.Receive(4);
        fixture.Realtime.Raise(new MessageUpdate("known", false, 4, MessageUpdateKind.New,
            Message: CreateMessage(4, "known", 2)));
        Assert.Single(fixture.Presenter.Shown);
    }

    [Fact]
    public async Task HistoryAndResyncDoNotNotify()
    {
        var fixture = new Fixture();
        var history = Enumerable.Range(1, 60).Select(id =>
        {
            var message = CreateMessage(id, "known", 2);
            message.SentAt.Seconds = id;
            return message;
        }).ToList();
        fixture.Messenger.HistoryPage = (_, anchor, before, after) =>
        {
            var index = anchor == 0 ? history.Count : history.FindIndex(row => row.MessageId == anchor);
            var start = Math.Max(0, index - before);
            return Task.FromResult<(ErrorReturner, List<MessageModel>?)>(
                (new(true), history.Skip(start).Take(index - start + 1 + after).ToList()));
        };
        await fixture.ViewModel.LoadAsync();
        fixture.ViewModel.SelectedChat = Assert.Single(fixture.ViewModel.Chats);
        Assert.DoesNotContain(fixture.ViewModel.Messages, message => message.Id == 1);
        await fixture.ViewModel.LoadOlderHistoryCommand.ExecuteAsync(null);
        Assert.Contains(fixture.ViewModel.Messages, message => message.Id == 1);

        var incoming = CreateMessage(61, "known", 2);
        incoming.SentAt.Seconds = 61;
        history.Add(incoming);
        fixture.Realtime.Resync();

        Assert.Contains(fixture.ViewModel.Messages, message => message.Id == 61);
        Assert.Empty(fixture.Presenter.Shown);
    }

    [Fact]
    public async Task PreviewIsPlainXmlAndPrivateMessageDoesNotExposeChatOrMessage()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        await dispatcher.RunAsync(async () =>
        {
            var sounds = new SoundStore();
            var fixture = new Fixture(dispatcher: dispatcher, sounds: sounds);
            fixture.Messenger.Chats[0].Title = "Chat <A> & B";
            fixture.Messenger.UserData = new() { FirstName = "Alice" };
            fixture.Messenger.Chats.Add(CreateChat("private", "Private title", chatType: ChatType.Private));
            await sounds.SaveAsync(new(fixture.Messenger.CurrentNodeAddress, 1, "private"), ChatNotificationSound.Sms,
                CancellationToken.None);
            await fixture.ViewModel.LoadAsync();
            fixture.Realtime.Raise(new IncomingMessage("known", CreateMessage(1, "known", 2, "**hello** & world")));
            fixture.Realtime.Raise(new MessageUpdate("private", true, 2, MessageUpdateKind.New,
                Encrypted: new EncryptedMessage { Id = 2, ChatId = "private", SenderId = 2 }));

            var normal = fixture.Presenter.Shown[0];
            var xml = MessageNotificationPayload.CreateXml(normal);
            Assert.Contains("&lt;A&gt; &amp; B", xml);
            Assert.Equal(new[] { "Chat <A> & B · Alice", "hello & world" },
                XElement.Parse(xml).Descendants("text").Select(text => text.Value));

            var encrypted = fixture.Presenter.Shown[1];
            Assert.Equal("BarkFluff", encrypted.Title);
            Assert.Equal("Notifications_PrivateMessage", encrypted.Text);
            Assert.Equal(ChatNotificationSound.Sms, encrypted.Sound);
        });
    }

    [Theory]
    [InlineData(0x0001)]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public async Task NotificationXmlDropsInvalidCharactersFromChatSenderAndPreview(int invalidCodeUnit)
    {
        var invalid = (char)invalidCodeUnit;
        const string emoji = "\U0001F431";
        var fixture = new Fixture();
        fixture.Messenger.Chats[0].Title = $"Chat{invalid}{emoji}";
        fixture.Messenger.UserData = new() { FirstName = $"Alice{invalid}{emoji}" };
        await fixture.ViewModel.LoadAsync();
        fixture.Realtime.Raise(new IncomingMessage("known", CreateMessage(1, "known", 2, $"Hello{invalid}{emoji}")));

        var xml = MessageNotificationPayload.CreateXml(Assert.Single(fixture.Presenter.Shown));
        Assert.Equal(new[] { $"Chat{emoji} · Alice{emoji}", $"Hello{emoji}" },
            XElement.Parse(xml).Descendants("text").Select(text => text.Value));
    }

    [Fact]
    public async Task ResyncRetriesDeviceFlagUntilFirstSuccess()
    {
        var fixture = new Fixture();
        var device = fixture.Device.CurrentDevice;
        fixture.Device.CurrentDevice = null;
        await fixture.ViewModel.LoadAsync();
        fixture.Receive(1);
        Assert.Empty(fixture.Presenter.Shown);
        Assert.Equal(1, fixture.Device.CurrentDeviceReads);

        fixture.Realtime.Resync();
        Assert.Equal(2, fixture.Device.CurrentDeviceReads);
        Assert.False(fixture.Settings.IsEnabled);

        fixture.Device.CurrentDevice = device;
        fixture.Realtime.Resync();
        Assert.Equal(3, fixture.Device.CurrentDeviceReads);
        Assert.True(fixture.Settings.IsEnabled);
        fixture.Receive(2);
        Assert.Single(fixture.Presenter.Shown);

        fixture.Realtime.Resync();
        Assert.Equal(3, fixture.Device.CurrentDeviceReads);
    }

    [Fact]
    public async Task ActivationRoundTripsAndOpensOnlyMatchingSessionIncludingOffPageChat()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.LoadAsync();

        var target = new MessageNotificationTarget("node?one=1&two=2", 1, "off-page");
        var xml = XElement.Parse(MessageNotificationPayload.CreateXml(new(target, "title", "text")));
        var argument = xml.Attribute("launch")!.Value.Split('=', 2)[1];
        Assert.Equal(target, MessageNotificationPayload.Parse(argument));
        Assert.Null(MessageNotificationPayload.Parse("!invalid!"));

        fixture.Messenger.FindChat = id => Task.FromResult<Chat?>(CreateChat(id, chatType: ChatType.Private));
        Assert.False(await fixture.ViewModel.OpenNotificationChatAsync(target with { UserId = 2 }));
        Assert.False(await fixture.ViewModel.OpenNotificationChatAsync(target with { Node = "wrong" }));
        Assert.Null(fixture.ViewModel.SelectedChat);

        Assert.True(await fixture.ViewModel.OpenNotificationChatAsync(target));
        Assert.Equal(target.ChatId, fixture.ViewModel.SelectedChat!.Id);
        Assert.True(fixture.ViewModel.SelectedChat.IsPrivate);
        Assert.Contains(target, fixture.Presenter.ClearedChats);
    }

    [Fact]
    public async Task SessionChangeInvalidatesPendingChatLookup()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.LoadAsync();
        var lookup = new TaskCompletionSource<Chat?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Messenger.FindChat = _ => lookup.Task;
        var open = fixture.ViewModel.OpenNotificationChatAsync(new(fixture.Messenger.CurrentNodeAddress, 1, "off-page"));

        fixture.Messenger.CurrentUserId = 5;
        await fixture.ViewModel.LoadAsync();
        lookup.SetResult(CreateChat("off-page"));
        Assert.False(await open);
        Assert.Null(fixture.ViewModel.SelectedChat);
        Assert.DoesNotContain(fixture.ViewModel.Chats, chat => chat.Id == "off-page");
        Assert.Equal(1, fixture.Presenter.ClearCount);
    }

    [Theory]
    [InlineData("  ответ  ", true, "  ответ  ")]
    [InlineData(null, true, "")]
    [InlineData(null, false, null)]
    public void ActivationDistinguishesOpeningFromReplying(string? input, bool isReply, string? expectedText)
    {
        var target = new MessageNotificationTarget("https://node.invalid", 1, "chat");
        var operationId = Guid.Parse("657262a0-df45-4669-ac3c-4f18f21beb60");
        var arguments = new Dictionary<string, string> { ["messageChat"] = MessageNotificationPayload.Encode(target) };
        if (isReply)
        {
            arguments["replyOperation"] = operationId.ToString("N");
            arguments["replyMessage"] = "42";
        }

        var activation = MessageNotificationPayload.ParseActivation(arguments, input);

        Assert.Equal(target, activation.Target);
        Assert.Equal(expectedText is null ? null : new NotificationReply(target, operationId, 42, expectedText),
            activation.Reply);
    }

    [Fact]
    public async Task SettingsReloadDeviceFlagAndDisableClearsImmediatelyEvenWhileSaving()
    {
        var fixture = new Fixture(enabled: false);
        await fixture.ViewModel.LoadAsync();
        Assert.False(fixture.Settings.IsEnabled);
        Assert.True(fixture.Settings.CanChange);

        fixture.Device.CurrentDevice!.NotificationsEnabled = true;
        await fixture.Settings.LoadAsync();
        Assert.True(fixture.Settings.IsEnabled);
        fixture.Receive(1);
        Assert.Single(fixture.Presenter.Shown);

        var save = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Device.SetNotificationsEnabled = enabled =>
        {
            Assert.False(enabled);
            return save.Task;
        };
        var disabling = fixture.Settings.SetEnabledAsync(false);
        Assert.False(fixture.Settings.IsEnabled);
        fixture.Receive(2);
        Assert.Empty(fixture.Presenter.Shown);

        save.SetResult("Error_SettingsSaveFailed");
        await disabling;
        Assert.True(fixture.Settings.IsEnabled);
        Assert.Equal("Error_SettingsSaveFailed", fixture.Settings.ErrorMessage);
        fixture.Receive(3);
        Assert.Single(fixture.Presenter.Shown);
        fixture.ViewModel.Reset();
        Assert.Empty(fixture.Presenter.Shown);
        fixture.Receive(4);
        Assert.Empty(fixture.Presenter.Shown);
    }

    private sealed class Fixture
    {
        public Fixture(bool enabled = true, InlineUiDispatcher? dispatcher = null, IChatNotificationSoundStore? sounds = null,
            TimeProvider? timeProvider = null)
        {
            Device.CurrentDevice!.NotificationsEnabled = enabled;
            Messenger.Chats.Add(CreateChat("known"));
            var localization = new StubLocalizationService();
            var notifications = new MessageNotificationService(Presenter, localization, sounds);
            Settings = new(Device, localization, notifications);
            ViewModel = CreateViewModel(Messenger, Realtime, dispatcher: dispatcher, timeProvider: timeProvider, notifications: notifications,
                notificationSettings: Settings);
        }

        public FakeRealtimeMessengerService Realtime { get; } = new();
        public FakeMessengerService Messenger { get; } = new() { CurrentNodeAddress = "node?one=1&two=2", MutedChats = [] };
        public FakeDeviceSettingsService Device { get; } = new();
        public CapturingPresenter Presenter { get; } = new();
        public SettingsNotificationsViewModel Settings { get; }
        public MessengerViewModel ViewModel { get; }

        public void Receive(long id, string chat = "known") =>
            Realtime.Raise(new IncomingMessage(chat, CreateMessage(id, chat, 2)));
    }

    private sealed class SoundStore : IChatNotificationSoundStore
    {
        public ChatNotificationSound Sound { get; private set; }
        public MessageNotificationTarget? Target { get; private set; }
        public Exception? LoadError { get; init; }

        public Task<ChatNotificationSound> LoadAsync(MessageNotificationTarget target, CancellationToken cancellationToken) =>
            LoadError is { } error ? Task.FromException<ChatNotificationSound>(error) :
                Task.FromResult(target == Target ? Sound : ChatNotificationSound.Default);

        public Task SaveAsync(MessageNotificationTarget target, ChatNotificationSound sound, CancellationToken cancellationToken)
        {
            Target = target;
            Sound = sound;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingPresenter : INotificationPresenter
    {
        public List<MessageNotification> Shown { get; } = [];
        public List<ChatNotificationSound> Previews { get; } = [];
        public List<MessageNotificationTarget> ClearedChats { get; } = [];
        public int ClearCount { get; private set; }

        public void Show(MessageNotification notification) => Shown.Add(notification);
        public void PreviewSound(ChatNotificationSound sound, string text) => Previews.Add(sound);
        public void RemoveReply(NotificationReply reply) { }
        public void RemoveChat(MessageNotificationTarget target)
        {
            ClearedChats.Add(target);
            Shown.RemoveAll(notification => notification.Target == target);
        }
        public void RemoveAll()
        {
            ClearCount++;
            Shown.Clear();
        }
    }
}
