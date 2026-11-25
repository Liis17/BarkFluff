using BarkFluff.Client.Core.Services;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace BarkFluff.Client.WinUI.Infrastructure.Notifications;

internal sealed record NotificationActivation(MessageNotificationTarget? Target, NotificationReply? Reply = null,
    bool IsSoundPreview = false);

internal sealed class WindowsNotificationPresenter : INotificationPresenter
{
    private const string ReplyInput = "replyText";
    private const string SoundPreview = "soundPreview";
    private AppNotificationManager? _manager;
    private Action<NotificationActivation>? _invoke;
    private Task _pending = Task.CompletedTask;

    public void Register(Action<NotificationActivation> invoke)
    {
        try
        {
            var manager = AppNotificationManager.Default;
            _invoke = invoke;
            manager.NotificationInvoked += OnInvoked;
            try
            {
                manager.Register();
                _manager = manager;
            }
            catch (Exception)
            {
                manager.NotificationInvoked -= OnInvoked;
                _invoke = null;
            }
        }
        catch (Exception)
        {
            _invoke = null;
        }
    }

    public NotificationActivation? ReadStartupActivation()
    {
        if (_manager is null)
        {
            return null;
        }

        try
        {
            // Холодный запуск может получить первый клик через AppLifecycle, до событий NotificationInvoked.
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            return activation?.Data is AppNotificationActivatedEventArgs args ? ParseActivation(args) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        NotificationActivation activation = new(null);
        try
        {
            activation = ParseActivation(args);
        }
        catch (Exception)
        {
            // При недоступных аргументах достаточно показать окно.
        }

        if (!activation.IsSoundPreview)
            _invoke?.Invoke(activation);
    }

    private static NotificationActivation ParseActivation(AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue(SoundPreview, out var preview) && preview == "1")
            return new(null, IsSoundPreview: true);

        var (target, reply) = MessageNotificationPayload.ParseActivation(
            args.Arguments, args.UserInput.TryGetValue(ReplyInput, out var text) ? text : null);
        return new(target, reply);
    }

    public void Show(MessageNotification notification) => Enqueue(manager =>
    {
        AppNotification toast;
        if (notification.Reply is null && notification.Sound == ChatNotificationSound.Default)
        {
            toast = new AppNotification(MessageNotificationPayload.CreateXml(notification));
        }
        else
        {
            var target = MessageNotificationPayload.Encode(notification.Target);
            var builder = new AppNotificationBuilder()
                .AddArgument(MessageNotificationPayload.ArgumentName, target)
                .AddText(MessageNotificationPayload.RemoveInvalidXmlCharacters(notification.Title))
                .AddText(MessageNotificationPayload.RemoveInvalidXmlCharacters(notification.Text));
            string? operation = null;
            if (notification.Reply is { } reply)
            {
                operation = Guid.NewGuid().ToString("N");
                builder.AddTextBox(ReplyInput, reply.Placeholder, string.Empty)
                    .AddButton(new AppNotificationButton(reply.Label)
                        .SetInputId(ReplyInput)
                        .AddArgument(MessageNotificationPayload.ArgumentName, target)
                        .AddArgument(MessageNotificationPayload.ReplyOperationArgument, operation)
                        .AddArgument(MessageNotificationPayload.ReplyMessageArgument, reply.MessageId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)));
            }
            toast = WithSound(builder, notification.Sound).BuildNotification();
            if (operation is not null)
                toast.Tag = operation;
        }
        toast.Group = MessageNotificationPayload.Group(notification.Target);
        manager.Show(toast);
        return Task.CompletedTask;
    });

    public void PreviewSound(ChatNotificationSound sound, string text) => Enqueue(manager =>
    {
        var toast = WithSound(new AppNotificationBuilder().AddArgument(SoundPreview, "1").AddText(text), sound)
            .BuildNotification();
        toast.Tag = SoundPreview;
        toast.Group = SoundPreview;
        toast.Expiration = DateTimeOffset.UtcNow.AddSeconds(10);
        manager.Show(toast);
        return Task.CompletedTask;
    });

    private static AppNotificationBuilder WithSound(AppNotificationBuilder builder, ChatNotificationSound sound) =>
        sound switch
        {
            ChatNotificationSound.Silent => builder.MuteAudio(),
            ChatNotificationSound.InstantMessage => builder.SetAudioEvent(AppNotificationSoundEvent.IM),
            ChatNotificationSound.Mail => builder.SetAudioEvent(AppNotificationSoundEvent.Mail),
            ChatNotificationSound.Reminder => builder.SetAudioEvent(AppNotificationSoundEvent.Reminder),
            ChatNotificationSound.Sms => builder.SetAudioEvent(AppNotificationSoundEvent.SMS),
            _ => builder
        };

    public void RemoveReply(NotificationReply reply) => Enqueue(async manager =>
        await manager.RemoveByTagAndGroupAsync(reply.OperationId.ToString("N"), MessageNotificationPayload.Group(reply.Target)));

    public void ShowReplyError(string text) => Enqueue(manager =>
    {
        manager.Show(new AppNotificationBuilder().AddText("BarkFluff").AddText(text).BuildNotification());
        return Task.CompletedTask;
    });

    public void RemoveChat(MessageNotificationTarget target) => Enqueue(async manager =>
        await manager.RemoveByGroupAsync(MessageNotificationPayload.Group(target)));

    public void ShowCall(Guid id, string title) => Enqueue(manager =>
    {
        var xml = new System.Xml.Linq.XElement("toast", new System.Xml.Linq.XAttribute("launch", "audioCall=1"),
            new System.Xml.Linq.XElement("visual", new System.Xml.Linq.XElement("binding",
                new System.Xml.Linq.XAttribute("template", "ToastGeneric"), new System.Xml.Linq.XElement("text", title))));
        manager.Show(new AppNotification(xml.ToString()) { Group = id.ToString("N") });
        return Task.CompletedTask;
    });

    public void RemoveCall(Guid id) => Enqueue(async manager => await manager.RemoveByGroupAsync(id.ToString("N")));

    public void RemoveAll() => Enqueue(async manager => await manager.RemoveAllAsync());

    private void Enqueue(Func<AppNotificationManager, Task> operation)
    {
        if (_manager is { } manager)
        {
            _pending = RunAsync(_pending, () => operation(manager));
        }
    }

    private static async Task RunAsync(Task previous, Func<Task> operation)
    {
        try
        {
            await previous;
            await operation();
        }
        catch (Exception)
        {
            // Уведомления могут быть недоступны в текущем окружении.
        }
    }

    public Task StopAsync()
    {
        if (_manager is not { } manager)
        {
            return Task.CompletedTask;
        }

        _manager = null;
        _invoke = null;
        return RunAsync(_pending, async () =>
        {
            manager.NotificationInvoked -= OnInvoked;
            try
            {
                await manager.RemoveByTagAndGroupAsync(SoundPreview, SoundPreview);
            }
            finally
            {
                manager.Unregister();
            }
        });
    }
}
