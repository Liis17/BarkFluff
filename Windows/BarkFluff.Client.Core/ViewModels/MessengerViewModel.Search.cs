using System.Collections.ObjectModel;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private CancellationTokenSource? _searchLifetime;
    private int _searchRequestVersion;
    private int _searchOpenVersion;
    private SearchMessagesRequest? _messageQuery;
    private MessageSearchCursor? _nextSearchCursor;
    private bool _canRetryMessageSearch;
    private readonly HashSet<long> _searchMessageIds = [];

    internal Func<TimeSpan, CancellationToken, Task> SearchDelay { get; set; } = Task.Delay;

    public ObservableCollection<MessageSearchResultViewModel> MessageSearchResults { get; } = [];
    public IReadOnlyList<ChatItemViewModel> ChatSearchResults { get; private set; } = [];
    public bool IsChatSearchResultsVisible => ChatSearchResults.Count != 0;
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText) || HasMessageSearchFilters;
    public bool IsChatListVisible => !IsSearching;
    public bool HasMessageSearchFilters => SearchAuthor is not null || SearchFrom is not null ||
        SearchThrough is not null || SearchAttachmentPresence != MessageSearchAttachmentPresence.Any ||
        SearchAttachmentTypes.Count != 0;
    public bool CanLoadMoreMessages => _nextSearchCursor is not null && !IsMessageSearchBusy && MessageSearchError is null;
    public bool CanRetryMessageSearch => _canRetryMessageSearch;
    public bool ShowMessageSearch => _messageQuery is not null || MessageSearchError is not null;
    public bool ShowMessageSearchEmpty => ShowMessageSearch && !IsMessageSearchBusy &&
        MessageSearchError is null && MessageSearchResults.Count == 0 && _nextSearchCursor is null;

    public MessageSearchAuthor? SearchAuthor { get; private set; }
    public DateTimeOffset? SearchFrom { get; private set; }
    public DateTimeOffset? SearchThrough { get; private set; }
    public MessageSearchAttachmentPresence SearchAttachmentPresence { get; private set; }
    public IReadOnlyList<MessageAttachmentType> SearchAttachmentTypes { get; private set; } = [];

    [ObservableProperty] private bool _isMessageSearchBusy;
    [ObservableProperty] private string? _messageSearchError;
    [ObservableProperty] private string? _peopleSearchError;

    partial void OnSearchTextChanged(string value)
    {
        ApplyChatFilter();
        StartSearch();
    }

    public void ApplyMessageSearchFilters(MessageSearchAuthor? author, DateTimeOffset? from, DateTimeOffset? through,
        MessageSearchAttachmentPresence presence, IReadOnlyList<MessageAttachmentType> types)
    {
        SearchAuthor = author;
        if (from?.Date > through?.Date)
            (from, through) = (through, from);

        SearchFrom = from;
        SearchThrough = through;
        SearchAttachmentPresence = presence;
        SearchAttachmentTypes = presence == MessageSearchAttachmentPresence.Without ? [] : types.ToArray();
        StartSearch();
    }

    public IReadOnlyList<MessageSearchAuthor> GetSearchAuthors()
    {
        var currentUserId = _realtimeSession?.UserId;
        var authors = MessageSearchResults.Select(result => result.Author)
            .Concat(SearchResults.Where(user => !user.IsRestricted)
                .Select(user => new MessageSearchAuthor { UserId = user.UserId, DisplayName = user.DisplayName }))
            .Concat(Chats.Where(chat => chat.PeerUserId is > 0)
                .Select(chat => new MessageSearchAuthor { UserId = chat.PeerUserId!.Value, DisplayName = chat.Title }))
            .Prepend(SearchAuthor);

        var otherAuthors = authors.OfType<MessageSearchAuthor>()
            .Where(author => author.UserId > 0 || author.UserUuid.Length > 0)
            .Where(author => author.UserId != currentUserId)
            .DistinctBy(author => author.UserId > 0 ? (author.UserId, string.Empty) : (0L, author.UserUuid))
            .OrderBy(author => author.DisplayName, StringComparer.CurrentCultureIgnoreCase);

        return currentUserId is > 0
            ? [new MessageSearchAuthor { UserId = currentUserId.Value, DisplayName = _localization.GetString("Messenger_SearchAuthorSelf") }, .. otherAuthors]
            : [.. otherAuthors];
    }

    private void ResetSearch()
    {
        SearchAuthor = null;
        SearchFrom = SearchThrough = null;
        SearchAttachmentPresence = MessageSearchAttachmentPresence.Any;
        SearchAttachmentTypes = [];
        if (SearchText.Length != 0)
            SearchText = string.Empty;
        else
            StartSearch();
    }

    private static bool MatchesChat(ChatItemViewModel chat, string query) =>
        chat.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        chat.Username.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private void UpdateChatSearchResults(string query)
    {
        var results = query.Length == 0 ? [] : Chats.Where(chat => MatchesChat(chat, query)).ToArray();
        if (ChatSearchResults.SequenceEqual(results))
            return;

        ChatSearchResults = results;
        OnPropertyChanged(nameof(ChatSearchResults));
        OnPropertyChanged(nameof(IsChatSearchResultsVisible));
    }

    [RelayCommand]
    private Task RetryPeopleSearchAsync()
    {
        var session = _realtimeSession;
        if (PeopleSearchError is null || session is null || !IsCurrent(session) || _searchLifetime is not { } lifetime)
            return Task.CompletedTask;

        PeopleSearchError = null;
        return SearchPeopleAsync(SearchText.Trim(), session, _searchRequestVersion, lifetime.Token);
    }

    private void StartSearch()
    {
        var version = ++_searchRequestVersion;
        _searchLifetime?.Cancel();
        _searchLifetime?.Dispose();
        _searchLifetime = null;
        _messageQuery = null;
        _nextSearchCursor = null;
        _canRetryMessageSearch = false;
        _searchMessageIds.Clear();
        MessageSearchResults.Clear();
        SearchResults.Clear();
        OnPropertyChanged(nameof(IsSearchResultsVisible));
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(IsChatListVisible));
        OnPropertyChanged(nameof(HasMessageSearchFilters));
        IsMessageSearchBusy = false;
        MessageSearchError = null;
        PeopleSearchError = null;

        var query = SearchText.Trim();
        var session = _realtimeSession;
        if (!IsCurrent(session) || !IsSearching)
        {
            NotifyMessageSearch();
            return;
        }

        _searchLifetime = CancellationTokenSource.CreateLinkedTokenSource(session!.Lifetime.Token);
        var token = _searchLifetime.Token;
        if (query.Length >= 3)
            _ = SearchPeopleAsync(query, session, version, token);

        if (query.Length >= 2 || HasMessageSearchFilters)
        {
            if (session.MessageSearchUnavailable)
                MessageSearchError = _localization.GetString("Messenger_SearchUnavailable");
            else if (query.Length > 256)
                MessageSearchError = _localization.GetString("Messenger_SearchInvalid");
            else
            {
                var request = new SearchMessagesRequest
                {
                    Query = query,
                    PageSize = 20,
                    AttachmentPresence = SearchAttachmentPresence,
                    AttachmentTypes = { SearchAttachmentTypes }
                };
                if (SearchAuthor is { UserId: > 0 } local)
                    request.AuthorUserId = local.UserId;
                else if (SearchAuthor is { } remote)
                    request.AuthorUserUuid = remote.UserUuid;
                if (SearchFrom is { } from)
                    request.SentFrom = LocalDayStart(from.Date);
                if (SearchThrough is { } through)
                    request.SentBefore = LocalDayStart(through.Date.AddDays(1));

                _messageQuery = request;
                _ = LoadMessageSearchPageAsync(session, version, token, debounce: true);
            }
        }

        NotifyMessageSearch();
    }

    private static Timestamp LocalDayStart(DateTime date)
    {
        var zone = TimeZoneInfo.Local;
        var offset = zone.IsAmbiguousTime(date) ? zone.GetAmbiguousTimeOffsets(date).Max() : zone.GetUtcOffset(date);
        return Timestamp.FromDateTimeOffset(new DateTimeOffset(date, offset));
    }

    private bool IsCurrentSearch(RealtimeSession session, int version, CancellationToken token) =>
        !token.IsCancellationRequested && version == _searchRequestVersion && IsCurrent(session);

    private async Task SearchPeopleAsync(string query, RealtimeSession session, int version, CancellationToken token)
    {
        List<UserData>? people = null;
        try
        {
            await SearchDelay(TimeSpan.FromMilliseconds(300), token);
            token.ThrowIfCancellationRequested();
            var result = await _messenger.SearchUsersAsync(query, token).ConfigureAwait(false);
            if (result.error is { IsSuccess: true })
                people = result.users;
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
        }

        _uiDispatcher.Post(() =>
        {
            if (!IsCurrentSearch(session, version, token))
                return;
            if (people is null)
            {
                PeopleSearchError = _localization.GetString("Messenger_SearchPeopleFailed");
                return;
            }

            foreach (var user in people)
                SearchResults.Add(new UserSearchResultViewModel(user, _localization));
            OnPropertyChanged(nameof(IsSearchResultsVisible));
        });
    }

    [RelayCommand]
    private Task LoadMoreMessagesAsync() => CanLoadMoreMessages ? LoadCurrentMessagePageAsync() : Task.CompletedTask;

    [RelayCommand]
    private Task RetryMessageSearchAsync() => CanRetryMessageSearch ? LoadCurrentMessagePageAsync() : Task.CompletedTask;

    private Task LoadCurrentMessagePageAsync() =>
        _realtimeSession is { } session && IsCurrent(session) && _searchLifetime is { } lifetime
            ? LoadMessageSearchPageAsync(session, _searchRequestVersion, lifetime.Token)
            : Task.CompletedTask;

    private async Task LoadMessageSearchPageAsync(RealtimeSession session, int version, CancellationToken token, bool debounce = false)
    {
        if (IsMessageSearchBusy || _messageQuery is null)
            return;

        var request = _messageQuery.Clone();
        var cursor = _nextSearchCursor;
        request.Cursor = cursor;
        _canRetryMessageSearch = false;
        IsMessageSearchBusy = true;
        MessageSearchError = null;
        NotifyMessageSearch();

        SearchMessagesResponse? page = null;
        var status = StatusCode.Unknown;
        try
        {
            if (debounce)
                await SearchDelay(TimeSpan.FromMilliseconds(300), token);
            token.ThrowIfCancellationRequested();
            var result = await _messenger.SearchMessagesAsync(request, token).ConfigureAwait(false);
            if (result.error is { IsSuccess: true })
                page = result.page;
            else if (result.error is { } error)
                status = (StatusCode)error.ErrorCode;
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
        }

        _uiDispatcher.Post(() =>
        {
            if (!IsCurrentSearch(session, version, token))
                return;

            IsMessageSearchBusy = false;
            if (page is not null)
            {
                foreach (var hit in page.Hits)
                {
                    if (_searchMessageIds.Add(hit.MessageId))
                        MessageSearchResults.Add(new MessageSearchResultViewModel(hit, request.Query, _localization));
                }
                _nextSearchCursor = Equals(cursor, page.NextCursor) ? null : page.NextCursor;
            }
            else
            {
                _canRetryMessageSearch = status is not (StatusCode.Unimplemented or StatusCode.InvalidArgument);
                if (status == StatusCode.Unimplemented)
                    session.MessageSearchUnavailable = true;
                MessageSearchError = _localization.GetString(status switch
                {
                    StatusCode.Unimplemented => "Messenger_SearchUnavailable",
                    StatusCode.InvalidArgument => "Messenger_SearchInvalid",
                    _ => "Messenger_SearchMessagesFailed"
                });
            }

            NotifyMessageSearch();
        });
    }

    private void NotifyMessageSearch()
    {
        OnPropertyChanged(nameof(CanLoadMoreMessages));
        OnPropertyChanged(nameof(CanRetryMessageSearch));
        OnPropertyChanged(nameof(ShowMessageSearch));
        OnPropertyChanged(nameof(ShowMessageSearchEmpty));
    }

    public void OpenChatSearchResult(ChatItemViewModel chat)
    {
        if (IsCurrent(_realtimeSession) && Chats.Contains(chat))
        {
            ResetSearch();
            SelectedChat = chat;
        }
    }

    public async Task OpenMessageSearchResultAsync(MessageSearchResultViewModel result)
    {
        var session = _realtimeSession;
        if (session is null || !IsCurrent(session) || _searchLifetime is not { } lifetime ||
            !MessageSearchResults.Contains(result))
            return;

        var searchVersion = _searchRequestVersion;
        var openVersion = ++_searchOpenVersion;
        var chatVersion = _chatVersion;
        var token = lifetime.Token;
        bool CanOpen() => openVersion == _searchOpenVersion && IsCurrentSearch(session, searchVersion, token);
        var opened = await OpenChatByIdAsync(session, result.ChatId, token, CanOpen);
        if (!CanOpen())
            return;

        if (!opened || SelectedChat is not { } chat || chat.Id != result.ChatId || chat.IsPrivate)
        {
            if (chatVersion == _chatVersion)
                ActionError = _localization.GetString("Messenger_HistoryMessageUnavailable");
            return;
        }

        ResetSearch();
        await NavigateToMessageAsync(result.MessageId);
    }
}
