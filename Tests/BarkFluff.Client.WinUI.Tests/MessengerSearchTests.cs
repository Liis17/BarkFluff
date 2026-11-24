using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Grpc.Core;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerSearchTests
{
    [Fact]
    public async Task LoadsAllChatPagesAndSearchesThePrivateChatBeyondTheFirstFifty()
    {
        var offsets = new List<int>();
        var definitions = Enumerable.Range(0, 101)
            .Select(id => MessengerTestDoubles.CreateChat(id.ToString(), $"Room {id}")).ToList();
        definitions[100] = MessengerTestDoubles.CreateChat("100", "Private room", chatType: ChatType.Private);
        var api = new FakeMessengerService
        {
            ChatSnapshot = () => MessengerService.LoadChatPagesAsync((offset, size, _) =>
            {
                offsets.Add(offset);
                Assert.Equal(50, size);
                var page = definitions.Skip(offset).Take(size).ToList();
                if (offset == 50)
                    page[0] = definitions[49];
                return Task.FromResult<(ErrorReturner, List<Chat>?, int)>((new(true), page, definitions.Count));
            }, null, default)
        };
        var realtime = new FakeRealtimeMessengerService();
        var (vm, ui) = await LoadAsync(api, realtime);
        var resynced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(() =>
        {
            Assert.Equal([0, 50, 100], offsets);
            Assert.Equal(100, vm.Chats.Count);
            Assert.Equal(1, vm.Chats.Count(chat => chat.Id == "49"));
            vm.SearchText = "private room";
            Assert.Equal("100", Assert.Single(vm.ChatSearchResults).Id);

            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.ChatSearchResults) && vm.ChatSearchResults.Count == 2)
                    resynced.TrySetResult();
            };
            definitions.Add(MessengerTestDoubles.CreateChat("101", "Second private room"));
            offsets.Clear();
            realtime.Resync();
        });
        await resynced.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ui.Post(() =>
        {
            Assert.Equal([0, 50, 100], offsets);
            Assert.Contains(vm.ChatSearchResults, chat => chat.Id == "101");
            vm.Reset();
        });
    }

    [Fact]
    public async Task FailedPageDiscardsPartialSnapshot()
    {
        var failure = new ErrorReturner(false, "failed page");
        var result = await MessengerService.LoadChatPagesAsync((offset, _, _) =>
            Task.FromResult<(ErrorReturner, List<Chat>?, int)>(offset == 0
                ? (new(true), Enumerable.Range(0, 50).Select(id => new Chat { Id = id.ToString() }).ToList(), 51)
                : (failure, null, 0)), null, default);
        Assert.Same(failure, result.error);
        Assert.Null(result.chats);
    }

    [Fact]
    public async Task GetChatsRequestsEveryPageAndDeduplicatesOverlappingChats()
    {
        var rpc = new ChatPagesClient((offset, _, _) => Task.FromResult(ChatPage(offset)));
        using var api = new WebApiClient { MessagesAC = rpc };

        var (error, chats) = await CreateService(api).GetChatsAsync();

        Assert.True(error.IsSuccess);
        Assert.NotNull(chats);
        Assert.Equal(Enumerable.Range(0, 101).Where(id => id != 50).Select(id => id.ToString()),
            chats.Select(chat => chat.Id));
        Assert.Equal([(0, 50), (50, 50), (100, 50)], rpc.Requests);
    }

    [Fact]
    public async Task GetChatsDiscardsPartialSnapshotWhenSecondPageFails()
    {
        var rpc = new ChatPagesClient((offset, _, _) => offset == 0
            ? Task.FromResult(ChatPage(offset))
            : Task.FromException<ListChatsResponse>(new RpcException(new Status(StatusCode.Internal, "Page failed"))));
        using var api = new WebApiClient { MessagesAC = rpc };

        var (error, chats) = await CreateService(api).GetChatsAsync();

        Assert.False(error.IsSuccess);
        Assert.Null(chats);
        Assert.Equal([(0, 50), (50, 50)], rpc.Requests);
    }

    [Fact]
    public async Task GetChatsCancelsSecondPageAndDoesNotRequestThird()
    {
        using var cancellation = new CancellationTokenSource();
        var requested = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<ListChatsResponse>();
        var rpc = new ChatPagesClient((offset, _, token) =>
        {
            if (offset == 0)
                return Task.FromResult(ChatPage(offset));

            requested.SetResult(token);
            return pending.Task.WaitAsync(token);
        });
        using var api = new WebApiClient { MessagesAC = rpc };

        var loading = CreateService(api).GetChatsAsync(cancellation.Token);
        Assert.Equal(cancellation.Token, await requested.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal([(0, 50), (50, 50)], rpc.Requests);
    }

    [Fact]
    public async Task GetFirstChatPageRequestsOnlyOnePage()
    {
        var rpc = new ChatPagesClient((offset, _, _) => Task.FromResult(ChatPage(offset)));
        using var api = new WebApiClient { MessagesAC = rpc };

        var (error, chats) = await CreateService(api).GetFirstChatPageAsync(default);

        Assert.True(error.IsSuccess);
        Assert.Equal(50, chats?.Count);
        Assert.Equal([(0, 50)], rpc.Requests);
    }

    [Fact]
    public async Task NewInputCancelsRequestsAndIgnoresLateMessagesAndPeople()
    {
        var lateMessages = new TaskCompletionSource<(ErrorReturner, SearchMessagesResponse?)>();
        var latePeople = new TaskCompletionSource<(ErrorReturner, List<UserData>?)>();
        CancellationToken oldMessagesToken = default;
        CancellationToken oldPeopleToken = default;
        var api = new FakeMessengerService
        {
            OnSearchMessages = (request, token) =>
            {
                if (request.Query == "old")
                {
                    oldMessagesToken = token;
                    return lateMessages.Task;
                }

                return Task.FromResult(Page(2, "new"));
            },
            OnSearchPeople = (query, token) =>
            {
                if (query == "old")
                {
                    oldPeopleToken = token;
                    return latePeople.Task;
                }

                return Task.FromResult<(ErrorReturner, List<UserData>?)>((new(true), [new UserData { Id = 2 }]));
            }
        };
        var (vm, ui) = await LoadAsync(api);

        ui.Post(() =>
        {
            vm.SearchText = "old";
            vm.SearchText = "new";
            Assert.True(oldMessagesToken.IsCancellationRequested);
            Assert.True(oldPeopleToken.IsCancellationRequested);
            lateMessages.SetResult(Page(1, "old"));
            latePeople.SetResult((new(true), [new UserData { Id = 1 }]));
            Assert.Equal(2, Assert.Single(vm.MessageSearchResults).MessageId);
            Assert.Equal(2, Assert.Single(vm.SearchResults).UserId);
            vm.Reset();
        });
    }

    [Fact]
    public async Task SearchUsesCursorAndRetriesTheSamePageWithoutDuplicatingResults()
    {
        var cursor = new MessageSearchCursor { MessageId = 3, SentAt = new() { Seconds = 3 } };
        var requests = new List<SearchMessagesRequest>();
        var api = new FakeMessengerService
        {
            OnSearchMessages = (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult<(ErrorReturner, SearchMessagesResponse?)>(requests.Count switch
                {
                    1 => (new(true), new() { Hits = { Hit(3, "MATCH") }, NextCursor = cursor }),
                    2 => (new(false), null),
                    _ => (new(true), new() { Hits = { Hit(3, "MATCH"), Hit(2, "match") } })
                });
            }
        };
        var (vm, ui) = await LoadAsync(api);

        ui.Post(() =>
        {
            vm.SearchText = "match";
            Assert.Equal("MATCH", Assert.Single(vm.MessageSearchResults).Match);
            Assert.True(vm.CanLoadMoreMessages);
            vm.LoadMoreMessagesCommand.Execute(null);
            Assert.True(vm.CanRetryMessageSearch);
            vm.RetryMessageSearchCommand.Execute(null);
            Assert.Equal([3L, 2L], vm.MessageSearchResults.Select(result => result.MessageId));
            Assert.False(vm.CanLoadMoreMessages);
            vm.LoadMoreMessagesCommand.Execute(null);
            Assert.Equal(3, requests.Count);
            Assert.Null(requests[0].Cursor);
            Assert.All(requests.Skip(1), request => Assert.Equal(cursor, request.Cursor));
            vm.Reset();
        });
    }

    [Theory]
    [InlineData(StatusCode.Unimplemented, 1)]
    [InlineData(StatusCode.InvalidArgument, 2)]
    public async Task PermanentErrorsHaveNoRetryAndEmptySearchSendsNoRequest(StatusCode status, int expected)
    {
        var requests = new List<string>();
        var api = new FakeMessengerService
        {
            OnSearchMessages = (request, _) =>
            {
                requests.Add(request.Query);
                return Task.FromResult<(ErrorReturner, SearchMessagesResponse?)>((new(false, errorCode: (int)status), null));
            }
        };
        var (vm, ui) = await LoadAsync(api);

        ui.Post(() =>
        {
            vm.SearchText = "first";
            Assert.False(vm.CanRetryMessageSearch);
            vm.RetryMessageSearchCommand.Execute(null);
            vm.SearchText = "second";
            vm.SearchText = " ";
            Assert.Equal(expected, requests.Count);
            Assert.DoesNotContain(string.Empty, requests);
            Assert.Empty(vm.MessageSearchResults);
            vm.Reset();
        });
    }

    [Fact]
    public async Task FiltersCanSearchWithoutText()
    {
        SearchMessagesRequest? request = null;
        var api = new FakeMessengerService
        {
            OnSearchMessages = (value, _) =>
            {
                request = value;
                return Task.FromResult(Page(1, ""));
            }
        };
        var (vm, ui) = await LoadAsync(api);
        ui.Post(() =>
        {
            var uuid = Guid.NewGuid().ToString();
            var date = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
            vm.ApplyMessageSearchFilters(new() { UserUuid = uuid }, date, date,
                MessageSearchAttachmentPresence.With, [MessageAttachmentType.Sticker]);
            Assert.NotNull(request);
            Assert.Empty(request.Query);
            Assert.Equal(uuid, request.AuthorUserUuid);
            Assert.True(request.SentFrom < request.SentBefore);
            Assert.Equal(MessageSearchAttachmentPresence.With, request.AttachmentPresence);
            Assert.Equal(MessageAttachmentType.Sticker, Assert.Single(request.AttachmentTypes));
            vm.Reset();
        });
    }

    [Fact]
    public async Task OpensUnlistedChatAtTheOldSearchHitAndRequestsHighlight()
    {
        var anchors = new List<long>();
        var tail = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>();
        var api = new FakeMessengerService
        {
            FindChat = id => Task.FromResult<Chat?>(MessengerTestDoubles.CreateChat(id)),
            OnSearchMessages = (_, _) => Task.FromResult(Page(1, "hit")),
            HistoryPage = (id, anchor, _, _) =>
            {
                anchors.Add(anchor);
                return anchor == 0 ? tail.Task : Task.FromResult<(ErrorReturner, List<MessageModel>?)>((new(true),
                    [MessengerTestDoubles.CreateMessage(anchor, id, 2, "hit")]));
            }
        };
        var (vm, ui) = await LoadAsync(api);
        var opening = Task.CompletedTask;
        ui.Post(() =>
        {
            vm.SearchText = "hit";
            opening = vm.OpenMessageSearchResultAsync(Assert.Single(vm.MessageSearchResults));
        });
        await opening.WaitAsync(TimeSpan.FromSeconds(5));
        ui.Post(() =>
        {
            Assert.Equal("unlisted", vm.SelectedChat?.Id);
            Assert.Equal([0L, 1L], anchors);
            Assert.Equal(1, Assert.Single(vm.Messages).Id);
            Assert.Equal(MessageScrollTarget.Message, vm.ScrollRequest?.Target);
            Assert.Equal(1, vm.ScrollRequest?.MessageId);
            Assert.Empty(vm.SearchText);
            vm.Reset();
        });
    }

    [Fact]
    public async Task ReadingUsesOnlyFirstChatPageAndResetCancelsIt()
    {
        var requested = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<(ErrorReturner, List<Chat>?)>();
        var read = new TaskCompletionSource<IReadOnlyCollection<long>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshotCalls = 0;
        var firstPageCalls = 0;
        var definition = MessengerTestDoubles.CreateChat("chat");
        definition.CountUnread = 1;
        var api = new FakeMessengerService
        {
            ChatSnapshot = () =>
            {
                Interlocked.Increment(ref snapshotCalls);
                return Task.FromResult<(ErrorReturner, List<Chat>?)>((new(true), [definition]));
            },
            FirstChatPage = token =>
            {
                Interlocked.Increment(ref firstPageCalls);
                requested.SetResult(token);
                return pending.Task.WaitAsync(token);
            },
            MarkReadResponse = ids =>
            {
                read.SetResult(ids);
                return Task.FromResult(new ErrorReturner(true));
            }
        };
        api.Messages.Add(MessengerTestDoubles.CreateMessage(1, definition.Id, 2));
        var (vm, ui) = await LoadAsync(api);
        ui.Post(() =>
        {
            vm.SelectedChat = Assert.Single(vm.Chats);
            vm.MessageBecameVisibleCommand.Execute(Assert.Single(vm.Messages));
        });

        Assert.Equal([1L], await read.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        var token = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ui.Post(() =>
        {
            Assert.Equal(1, snapshotCalls);
            Assert.Equal(1, firstPageCalls);
            Assert.False(token.IsCancellationRequested);
            vm.Reset();
            Assert.True(token.IsCancellationRequested);
        });
    }

    [Fact]
    public async Task OpeningPersonUsesTargetedLookupAndResetCancelsIt()
    {
        var pending = new TaskCompletionSource<Chat?>();
        CancellationToken lookupToken = default;
        string? requestedId = null;
        var snapshotCalls = 0;
        var api = new FakeMessengerService
        {
            PersonChatId = "target",
            ChatSnapshot = () =>
            {
                snapshotCalls++;
                return Task.FromResult<(ErrorReturner, List<Chat>?)>((new(true), []));
            },
            OnFindChat = (id, token) =>
            {
                requestedId = id;
                lookupToken = token;
                return pending.Task;
            }
        };
        var (vm, ui) = await LoadAsync(api);
        Task opening = Task.CompletedTask;
        ui.Post(() =>
        {
            opening = vm.OpenSearchResultAsync(new UserSearchResultViewModel(new UserData { Id = 7 }, new StubLocalizationService()));
            Assert.Equal("target", requestedId);
            Assert.Equal(1, snapshotCalls);
            Assert.False(lookupToken.IsCancellationRequested);
            vm.Reset();
            Assert.True(lookupToken.IsCancellationRequested);
        });
        await opening.WaitAsync(TimeSpan.FromSeconds(5));
        pending.SetResult(MessengerTestDoubles.CreateChat("target", peerUserId: 7));
        ui.Post(() =>
        {
            Assert.Empty(vm.Chats);
            Assert.Null(vm.SelectedChat);
        });
    }

    [Fact]
    public async Task CurrentUserIsFirstAndAppearsOnlyOnceInAuthors()
    {
        var api = new FakeMessengerService
        {
            CurrentUserId = 2,
            OnSearchMessages = (_, _) => Task.FromResult(Page(1, "hit")),
            OnSearchPeople = (_, _) => Task.FromResult<(ErrorReturner, List<UserData>?)>(
                (new(true), [new UserData { Id = 3 }]))
        };
        var (vm, ui) = await LoadAsync(api);
        ui.Post(() =>
        {
            vm.SearchText = "hit";
            var authors = vm.GetSearchAuthors();
            Assert.Equal([2L, 3L], authors.Select(author => author.UserId));
            Assert.Equal("Messenger_SearchAuthorSelf", authors[0].DisplayName);
            vm.Reset();
        });
    }

    [Fact]
    public async Task ReversedDatesAreSwappedBeforeSearching()
    {
        SearchMessagesRequest? request = null;
        var api = new FakeMessengerService
        {
            OnSearchMessages = (value, _) =>
            {
                request = value;
                return Task.FromResult(Page(1, ""));
            }
        };
        var (vm, ui) = await LoadAsync(api);
        ui.Post(() =>
        {
            var earlier = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
            var later = earlier.AddDays(2);
            vm.ApplyMessageSearchFilters(null, later, earlier, MessageSearchAttachmentPresence.Any, []);
            Assert.Equal(earlier, vm.SearchFrom);
            Assert.Equal(later, vm.SearchThrough);
            Assert.NotNull(request);
            Assert.Equal(earlier.Date, request.SentFrom.ToDateTime().ToLocalTime().Date);
            Assert.Equal(later.Date.AddDays(1), request.SentBefore.ToDateTime().ToLocalTime().Date);
            vm.Reset();
        });
    }

    private static async Task<(MessengerViewModel, InlineUiDispatcher)> LoadAsync(FakeMessengerService api,
        FakeRealtimeMessengerService? realtime = null)
    {
        var ui = new InlineUiDispatcher();
        var vm = MessengerTestDoubles.CreateViewModel(api, realtime, dispatcher: ui);
        vm.SearchDelay = (_, _) => Task.CompletedTask;
        var loading = Task.CompletedTask;
        ui.Post(() => loading = vm.LoadAsync());
        await loading;
        return (vm, ui);
    }

    private static (ErrorReturner, SearchMessagesResponse?) Page(long id, string text) =>
        (new(true), new() { Hits = { Hit(id, text) } });

    private static MessengerService CreateService(WebApiClient api)
    {
        var session = new ClientSession();
        session.SetConnection(new NodeConnection(new NodeProfile("test-node", "", ""), new GlobalParam { UserId = 1 }));
        return new MessengerService(api, session);
    }

    private static ListChatsResponse ChatPage(int offset) => new()
    {
        TotalCount = 101,
        Chats = { Enumerable.Range(offset, Math.Min(50, 101 - offset))
            .Select(id => new Chat { Id = (id == 50 ? 49 : id).ToString() }) }
    };

    private sealed class ChatPagesClient(Func<int, int, CancellationToken, Task<ListChatsResponse>> loadPage)
        : MessagesApi.MessagesApiClient
    {
        public List<(int Offset, int Size)> Requests { get; } = [];

        public override AsyncUnaryCall<ListChatsResponse> ListChatsAsync(ListChatsRequest request,
            Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            var (offset, size) = (request.Pagination.Offset, request.Pagination.Size);
            Requests.Add((offset, size));
            return new AsyncUnaryCall<ListChatsResponse>(loadPage(offset, size, cancellationToken),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }
    }

    private static MessageSearchHit Hit(long id, string text) => new()
    {
        MessageId = id, ChatId = "unlisted", ChatTitle = "Chat", Author = new() { UserId = 2, DisplayName = "Author" },
        SentAt = new() { Seconds = id }, Text = text
    };
}
