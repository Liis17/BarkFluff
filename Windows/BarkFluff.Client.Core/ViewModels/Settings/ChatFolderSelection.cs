using BarkFluff.Proto.Users;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed class ChatFolderSelection
{
    private readonly HashSet<string> _initial;
    private readonly HashSet<string> _selected;
    public string FolderId { get; }
    public CancellationToken Lifetime { get; }
    public IReadOnlyList<ChatItemViewModel> Chats { get; }

    internal ChatFolderSelection(ChatFolderData folder, IEnumerable<ChatItemViewModel> chats, CancellationToken lifetime)
    {
        FolderId = folder.FolderId;
        Lifetime = lifetime;
        _initial = folder.ChatList.ToHashSet();
        _selected = new HashSet<string>(_initial);
        Chats = chats.OrderBy(chat => chat.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public bool Includes(ChatItemViewModel chat) => _selected.Contains(chat.Id);

    public void SetIncluded(ChatItemViewModel chat, bool included)
    {
        if (included)
        {
            _selected.Add(chat.Id);
        }
        else
        {
            _selected.Remove(chat.Id);
        }
    }

    public IEnumerable<ChatItemViewModel> Search(string query) =>
        Chats.Where(chat => chat.Title.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));

    internal IReadOnlyDictionary<string, bool> Changes() => Chats
        .Where(chat => _initial.Contains(chat.Id) != Includes(chat))
        .ToDictionary(chat => chat.Id, Includes);
}
