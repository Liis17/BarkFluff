using BarkFluff.Client.Core.Infrastructure.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class ChatFolderTabViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    internal ChatFolderTabViewModel(string? id, ILocalizationService localization)
    {
        Id = id;
        _localization = localization;
    }

    public string? Id { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AutomationName))]
    private string _title = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(BadgeText), nameof(AutomationName))]
    private long _unreadCount;

    public string BadgeText => UnreadCount switch
    {
        0 => string.Empty,
        > 99 => "99+",
        _ => UnreadCount.ToString()
    };

    public string AutomationName => UnreadCount == 0 ? Title :
        string.Format(_localization.GetString("Messenger_FolderUnread"), Title, UnreadCount);
}
