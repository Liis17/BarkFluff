using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    public StickerPickerViewModel? StickerPicker { get; }
    private readonly StickerFrameCache? _stickerFrames;

    [ObservableProperty] private double _stickerSize = 160;

    public async Task LoadStickerPreferencesAsync()
    {
        if (_stickerPreferences is null)
            return;

        try
        {
            var preferences = await _stickerPreferences.GetInterfacePreferencesAsync();
            _uiDispatcher.Post(() => StickerSize = Math.Clamp(preferences.ChatStickerSizeDp,
                InterfacePreferences.MinChatStickerSizeDp, InterfacePreferences.MaxChatStickerSizeDp));
        }
        catch (Exception)
        {
            // При недоступном хранилище сохраняем текущий размер.
        }
    }

    public Task OpenStickerPickerAsync()
    {
        if (!CanAttach || SelectedChat is not { } chat)
            return Task.CompletedTask;

        var session = _realtimeSession;
        var version = _chatVersion;
        return StickerPicker?.OpenAsync(() => CanAttach && IsOpen(session, chat, version)) ?? Task.CompletedTask;
    }

    public void CloseStickerPicker() => StickerPicker?.Close();

    public async Task<bool> SendStickerAsync(PickerSticker sticker)
    {
        if (!CanAttach || AttachmentComposer.IsSending || StickerPicker is not { } picker || !picker.CanChoose(sticker) ||
            _realtimeSession is not { } session || SelectedChat is not { } chat || !IsCurrent(session))
            return false;

        var version = _chatVersion;
        var token = AttachmentComposer.ScopeToken;
        var reply = ReplyTarget;
        using var reading = session.Journal.ReadHistory(chat.Id, chat.IsPrivate);
        var revision = session.Journal.Revision;
        picker.IsSending = true;
        ActionError = null;
        try
        {
            var (error, message) = await _messenger.SendMessageAsync(chat.Id, "", reply?.Id ?? 0,
                cancellationToken: token, attachmentIds: [sticker.FileId]);
            if (!IsCurrent(session))
                return false;

            if (error is not { IsSuccess: true } || message is null)
            {
                _uiDispatcher.Post(() =>
                {
                    if (IsOpen(session, chat, version))
                        ActionError = error is null ? _localization.GetString("Messenger_ActionFailed") : DescribeError(error);
                });
                return false;
            }

            if (IsOpen(session, chat, version))
            {
                if (_feedIncludesTail)
                    await MergeSentRowAsync(session, chat, version,
                        new(message.MessageId, message.SentAt.ToDateTimeOffset(), message, null), revision);

                _uiDispatcher.Post(() =>
                {
                    if (IsOpen(session, chat, version) && ReferenceEquals(ReplyTarget, reply))
                        ReplyTarget = null;
                });
            }

            if (IsCurrent(session))
                await picker.RememberAsync(sticker);

            return IsOpen(session, chat, version);
        }
        catch (Exception) when (!IsOpen(session, chat, version) || token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            _uiDispatcher.Post(() =>
            {
                if (IsOpen(session, chat, version))
                    ActionError = _localization.GetString("Messenger_ActionFailed");
            });
            return false;
        }
        finally
        {
            _uiDispatcher.Post(() =>
            {
                if (IsCurrent(session))
                    picker.IsSending = false;
            });
        }
    }
}
