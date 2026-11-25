using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly IChatDraftStore? _draftStore;
    private readonly IChatDraftRemote? _draftRemote;
    private ChatDraftSession? _drafts;
    private Task _draftWrites = Task.CompletedTask;
    private bool _applyingDraft;
    private (string Text, MessageItemViewModel? Reply)? _beforeEdit;

    private void StartDraftSession(RealtimeSession session)
    {
        if (_draftStore is null || _draftRemote is null)
            return;

        _drafts = new(session.Node, session.UserId, _draftStore, _draftRemote, _uiDispatcher,
            _muteClock, session.Lifetime.Token, () => IsCurrent(session), (id, apply) =>
            {
                if (Chats.FirstOrDefault(chat => chat.Id == id) is { } item)
                    UpdateDraftPreview(item);
                if (apply && SelectedChat is { } chat && chat.Id == id)
                    ApplyDraft(chat);
            }, _draftWrites);
        _drafts.Resume();
    }

    internal Task FlushDraftsAsync() => _drafts?.Flush() ?? _draftWrites;

    public Task StopDraftsAsync()
    {
        if (_drafts is { } drafts)
        {
            _drafts = null;
            _draftWrites = drafts.Stop();
        }
        return _draftWrites;
    }

    internal async Task<bool> StopDraftsForLogoutAsync()
    {
        var previous = StopDraftsAsync();
        if (_draftStore is not { } store || _messenger.CurrentUserId is not { } userId || userId <= 0)
        {
            await previous;
            return true;
        }

        var node = _messenger.CurrentNodeAddress;
        var cleanup = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            try
            {
                store.RemoveNonRegularDrafts(node, userId);
                return true;
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                return false;
            }
        });
        _draftWrites = cleanup;
        return await cleanup;
    }

    private void ChangeComposerChat(ChatItemViewModel? previous, ChatItemViewModel? current)
    {
        _applyingDraft = true;
        try
        {
            ResetMessageActionState();
            Messages.Clear();
            _beforeEdit = null;
            SetComposerDraft("");
        }
        finally
        {
            _applyingDraft = false;
        }
        if (previous is not null)
            _drafts?.Leave(previous.Id);
        if (current is not null)
        {
            ApplyDraft(current);
            _drafts?.Open(current.Id, current.Definition.ChatType);
        }
    }

    private void SaveComposerDraft(bool replyChanged = false)
    {
        if (!_applyingDraft && !IsEditing && IsCurrent(_realtimeSession) && SelectedChat is { } chat)
        {
            var replyId = ReplyTarget?.Id ?? 0;
            if (!replyChanged && replyId == 0 && _drafts?.Get(chat.Id) is { HasContent: true } draft)
                replyId = draft.ReplyId;
            _drafts?.Set(chat.Id, chat.Definition.ChatType, DraftText, replyId);
        }
    }

    partial void OnReplyTargetChanged(MessageItemViewModel? value) => SaveComposerDraft(replyChanged: true);

    private void SetComposerDraft(string text, MessageItemViewModel? reply = null)
    {
        var wasApplying = _applyingDraft;
        _applyingDraft = true;
        try
        {
            DraftText = text;
            ReplyTarget = reply;
        }
        finally
        {
            _applyingDraft = wasApplying;
        }
    }

    private void BeginDraftEdit(MessageItemViewModel message)
    {
        _beforeEdit ??= (DraftText, ReplyTarget);
        EditingMessage = message;
        SetComposerDraft(message.Text);
    }

    private void RestoreDraftAfterEdit()
    {
        if (!IsEditing)
            return;

        EditingMessage = null;
        if (_drafts is not null && SelectedChat is { } chat)
            ApplyDraft(chat);
        else if (_beforeEdit is { } draft)
            SetComposerDraft(draft.Text, draft.Reply);
        _beforeEdit = null;
    }

    private void ApplyDraft(ChatItemViewModel chat)
    {
        if (IsEditing)
            return;

        var draft = _drafts?.Get(chat.Id);
        if (draft is not { HasContent: true })
        {
            SetComposerDraft("");
            return;
        }

        var reply = ReplyTarget?.Id == draft.ReplyId ? ReplyTarget :
            Messages.FirstOrDefault(message => message.Id == draft.ReplyId);
        SetComposerDraft(draft.Text, reply);
        if (reply is null && draft.ReplyId != 0 && chat.Definition.ChatType == ChatType.Regular &&
            _realtimeSession is { } session)
            _ = RestoreDraftReplyAsync(session, chat, _chatVersion, draft.ReplyId);
    }

    private async Task RestoreDraftReplyAsync(RealtimeSession session, ChatItemViewModel chat, int version, long replyId)
    {
        var token = session.Lifetime.Token;
        bool IsStillCurrent() => IsOpen(session, chat, version) && !IsEditing &&
            _drafts?.Get(chat.Id) is { HasContent: true } draft && draft.ReplyId == replyId && ReplyTarget is null;

        try
        {
            var (error, rows) = await GetHistoryPageAsync(chat, null, replyId, 1, 1, token);
            if (!IsStillCurrent())
                return;
            var found = rows.FirstOrDefault(row => row.Id == replyId && !row.IsDeleted)?.Regular;
            var reply = found is null ? null : await CreateMessageItemAsync(found, session.UserId);
            _uiDispatcher.Post(() =>
            {
                if (!IsStillCurrent())
                    return;
                if (reply is not null && session.Journal.Find(chat.Id, false, replyId)?.Kind != MessageUpdateKind.Delete)
                    SetComposerDraft(DraftText, reply);
                else if (error is { IsSuccess: true } || error?.ErrorCode is 1 or ErrorReturner.HistoryAnchorNotFound)
                    _drafts?.Set(chat.Id, chat.Definition.ChatType, DraftText, 0);
            });
        }
        catch (Exception)
        {
            // Недоступная история не мешает восстановить текст черновика.
        }
    }

    private void UpdateDraftPreview(ChatItemViewModel chat) => chat.SetDraftPreview(_drafts?.Get(chat.Id));
}
