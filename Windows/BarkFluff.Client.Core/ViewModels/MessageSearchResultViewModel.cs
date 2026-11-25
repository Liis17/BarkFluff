using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using System.Globalization;

namespace BarkFluff.Client.Core.ViewModels;

public sealed class MessageSearchResultViewModel
{
    internal MessageSearchResultViewModel(MessageSearchHit hit, string query, ILocalizationService localization)
    {
        ChatId = hit.ChatId;
        ChatTitle = string.IsNullOrWhiteSpace(hit.ChatTitle)
            ? localization.GetString("Messenger_SearchUnknownChat") : hit.ChatTitle;
        MessageId = hit.MessageId;
        Author = hit.Author;
        AuthorName = string.IsNullOrWhiteSpace(Author?.DisplayName)
            ? localization.GetString("Messenger_SearchUnknownAuthor") : Author.DisplayName;
        Date = hit.SentAt is { } sentAt
            ? string.Format(CultureInfo.InvariantCulture, localization.GetString("Messenger_SearchResultDateFormat"),
                sentAt.ToDateTime().ToLocalTime())
            : string.Empty;
        AttachmentTypes = hit.AttachmentTypes.ToArray();
        (BeforeMatch, Match, AfterMatch) = GetSnippet(hit.Text, query);
    }

    public string ChatId { get; }
    public string ChatTitle { get; }
    public long MessageId { get; }
    public MessageSearchAuthor? Author { get; }
    public string AuthorName { get; }
    public string Date { get; }
    public IReadOnlyList<MessageAttachmentType> AttachmentTypes { get; }
    public string BeforeMatch { get; }
    public string Match { get; }
    public string AfterMatch { get; }
    public string AccessibleName => string.Join(" · ", ChatTitle, AuthorName, Date, BeforeMatch + Match + AfterMatch);

    private static (string Before, string Match, string After) GetSnippet(string text, string query)
    {
        var index = query.Length == 0 ? -1 : text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return (text.Length <= 120 ? text : text[..120] + "…", string.Empty, string.Empty);

        var start = Math.Max(0, index - 35);
        var matchEnd = index + query.Length;
        var end = Math.Min(text.Length, matchEnd + 80);
        return ((start > 0 ? "…" : "") + text[start..index], text[index..matchEnd],
            text[matchEnd..end] + (end < text.Length ? "…" : ""));
    }
}
