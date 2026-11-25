using System.ComponentModel;
using BarkFluff.Client.Core.Services;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    public async Task EditChatSoundAsync(
        Func<ChatNotificationSound, Func<bool>, Action<ChatNotificationSound>, CancellationToken,
            Task<ChatNotificationSound?>> edit)
    {
        if (_notifications is not { CanEditSound: true } notifications || _realtimeSession is not { } session ||
            SelectedChat is not { } chat || !IsCurrent(session))
            return;

        var version = _chatVersion;
        var target = new MessageNotificationTarget(session.Node, session.UserId, chat.Id);
        var token = session.Lifetime.Token;
        bool Current() => IsOpen(session, chat, version);
        bool CanPreview() => Current() && !IsChatMuted(chat.Id);

        void ReportStorageError() => PostSession(session, _ =>
        {
            if (Current())
                ActionError = _localization.GetString("Messenger_ChatSoundFailed");
        });

        ChatNotificationSound sound;
        try
        {
            sound = await notifications.LoadSoundAsync(target, token);
        }
        catch (Exception)
        {
            ReportStorageError();
            return;
        }

        if (!Current())
            return;

        using var dialogLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        void OnDialogContextChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(SelectedChat))
                dialogLifetime.Cancel();
        }

        ChatNotificationSound? choice;
        PropertyChanged += OnDialogContextChanged;
        try
        {
            choice = await edit(sound, CanPreview, preview =>
            {
                if (Current())
                    notifications.PreviewSound(preview, IsChatMuted(chat.Id));
            }, dialogLifetime.Token);
        }
        finally
        {
            PropertyChanged -= OnDialogContextChanged;
        }

        if (!Current() || dialogLifetime.IsCancellationRequested || choice is not { } selected || selected == sound)
            return;

        try
        {
            await notifications.SaveSoundAsync(target, selected, token);
        }
        catch (Exception)
        {
            ReportStorageError();
        }
    }
}
