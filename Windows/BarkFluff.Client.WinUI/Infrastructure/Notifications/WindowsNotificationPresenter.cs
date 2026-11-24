using BarkFluff.Client.Core.Services;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Windows.ApplicationModel.Activation;

namespace BarkFluff.Client.WinUI.Infrastructure.Notifications;

internal sealed class WindowsNotificationPresenter : INotificationPresenter
{
    private AppNotificationManager? _manager;
    private Action<MessageNotificationTarget?>? _invoke;
    private Task _pending = Task.CompletedTask;

    public void Register(Action<MessageNotificationTarget?> invoke)
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

    public MessageNotificationTarget? ReadStartupActivation()
    {
        if (_manager is null)
        {
            return null;
        }

        try
        {
            // Холодный запуск может получить первый клик через AppLifecycle, до событий NotificationInvoked.
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            return activation.Data switch
            {
                AppNotificationActivatedEventArgs args => ParseTarget(args),
                ToastNotificationActivatedEventArgs args when args.Argument.StartsWith(
                    MessageNotificationPayload.ArgumentName + "=", StringComparison.Ordinal) =>
                    MessageNotificationPayload.Parse(args.Argument[(MessageNotificationPayload.ArgumentName.Length + 1)..]),
                _ => null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        MessageNotificationTarget? target = null;
        try
        {
            target = ParseTarget(args);
        }
        catch (Exception)
        {
            // При недоступных аргументах достаточно показать окно.
        }

        _invoke?.Invoke(target);
    }

    private static MessageNotificationTarget? ParseTarget(AppNotificationActivatedEventArgs args) =>
        args.Arguments.TryGetValue(MessageNotificationPayload.ArgumentName, out var value)
            ? MessageNotificationPayload.Parse(value)
            : null;

    public void Show(MessageNotification notification) => Enqueue(manager =>
    {
        manager.Show(new AppNotification(MessageNotificationPayload.CreateXml(notification))
        {
            Group = MessageNotificationPayload.Group(notification.Target)
        });
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
        return RunAsync(_pending, () =>
        {
            manager.NotificationInvoked -= OnInvoked;
            manager.Unregister();
            return Task.CompletedTask;
        });
    }
}
