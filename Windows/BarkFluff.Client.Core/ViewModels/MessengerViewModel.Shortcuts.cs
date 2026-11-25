namespace BarkFluff.Client.Core.ViewModels;

public enum MessengerShortcutKey { CtrlK, CtrlF, Escape, Up, CtrlUp, CtrlDown }

public enum MessengerShortcutFocus { Other, Composer, Protected }

internal enum MessengerShortcutAction
{
    None, FocusSearch, CloseOverlay, CancelEdit, CancelReply, EditLastMessage, PreviousChat, NextChat
}

internal static class MessengerShortcuts
{
    internal static MessengerShortcutAction Choose(MessengerShortcutKey key, MessengerShortcutFocus focus,
        bool hasOverlay, bool hasChat, bool isEditing, bool isReplying, bool isComposerEmpty, bool isComposerMultiline)
    {
        if (focus == MessengerShortcutFocus.Protected)
            return MessengerShortcutAction.None;

        if (hasOverlay)
            return key == MessengerShortcutKey.Escape ? MessengerShortcutAction.CloseOverlay : MessengerShortcutAction.None;

        var canSwitchChat = hasChat && (focus != MessengerShortcutFocus.Composer || !isComposerMultiline);
        return key switch
        {
            MessengerShortcutKey.CtrlK => MessengerShortcutAction.FocusSearch,
            MessengerShortcutKey.CtrlF when hasChat => MessengerShortcutAction.FocusSearch,
            MessengerShortcutKey.Escape when isEditing => MessengerShortcutAction.CancelEdit,
            MessengerShortcutKey.Escape when isReplying => MessengerShortcutAction.CancelReply,
            MessengerShortcutKey.Up when focus == MessengerShortcutFocus.Composer && isComposerEmpty && !isEditing
                => MessengerShortcutAction.EditLastMessage,
            MessengerShortcutKey.CtrlUp when canSwitchChat => MessengerShortcutAction.PreviousChat,
            MessengerShortcutKey.CtrlDown when canSwitchChat => MessengerShortcutAction.NextChat,
            _ => MessengerShortcutAction.None
        };
    }
}

public sealed partial class MessengerViewModel
{
    public bool TryInvokeShortcut(MessengerShortcutKey key, MessengerShortcutFocus focus, Func<bool> focusSearch)
    {
        // Порядок соответствует наложению панелей в MessengerPage.
        Action? closeOverlay = MediaViewer.IsOpen ? MediaViewer.Close
            : SecretChats.IsOpen ? SecretChats.Close
            : IsProfileVisible ? CloseProfile
            : IsForwardVisible ? CancelForward
            : IsDeleteConfirmVisible ? CancelDeleteMessage
            : IsPrivateUnlockVisible ? CancelPrivateUnlock
            : null;

        switch (MessengerShortcuts.Choose(key, focus, closeOverlay is not null, HasSelectedChat,
                    IsEditing, ReplyTarget is not null, DraftText.Length == 0 && !AttachmentComposer.HasFiles,
                    DraftText.Contains('\r') || DraftText.Contains('\n')))
        {
            case MessengerShortcutAction.FocusSearch:
                return focusSearch();
            case MessengerShortcutAction.CloseOverlay:
                closeOverlay!();
                return true;
            case MessengerShortcutAction.CancelEdit:
            case MessengerShortcutAction.CancelReply:
                CancelComposerHint();
                return true;
            case MessengerShortcutAction.EditLastMessage:
                if (!CanEditComposerText || Messages.LastOrDefault(message => message.CanModify) is not { } message)
                    return false;
                StartEditMessage(message);
                return true;
            case MessengerShortcutAction.PreviousChat:
                return TrySelectAdjacentChat(-1);
            case MessengerShortcutAction.NextChat:
                return TrySelectAdjacentChat(1);
            default:
                return false;
        }
    }

    private bool TrySelectAdjacentChat(int offset)
    {
        IReadOnlyList<ChatItemViewModel> chats = IsSearching ? ChatSearchResults : VisibleChats;
        for (var index = 0; index < chats.Count; index++)
        {
            if (chats[index] != SelectedChat)
                continue;

            var next = index + offset;
            if (next < 0 || next >= chats.Count)
                return false;

            SelectedChat = chats[next];
            return true;
        }
        return false;
    }
}
