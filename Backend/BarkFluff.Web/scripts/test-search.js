const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function makeEl() {
    var names = [];
    var el = {
        className: '',
        innerHTML: '',
        value: '',
        children: [],
        listeners: {},
        attributes: {},
        setAttribute: function (name, value) {
            this.attributes[name] = value;
        },
        classList: {
            add: function (name) {
                if (names.indexOf(name) < 0) names.push(name);
            },
            remove: function (name) {
                names = names.filter(function (n) {
                    return n !== name;
                });
            },
            contains: function (name) {
                return names.indexOf(name) >= 0;
            }
        },
        addEventListener: function (type, callback) {
            this.listeners[type] = callback;
        },
        appendChild: function (child) {
            this.children.push(child);
            return child;
        }
    };
    // как настоящий innerHTML: присваивание очищает дочерние элементы
    var html = '';
    Object.defineProperty(el, 'innerHTML', {
        configurable: true,
        get: function () {
            return html;
        },
        set: function (value) {
            html = value;
            el.children = [];
        }
    });
    return el;
}

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function createHarness(options) {
    var input = makeEl();
    var results = makeEl();
    results.scrollTop = 0;
    var filtersEl = makeEl();
    // как настоящий innerHTML: присваивание очищает дочерние элементы
    var html = '';
    Object.defineProperty(results, 'innerHTML', {
        get: function () {
            return html;
        },
        set: function (value) {
            html = value;
            results.children = [];
            results.scrollTop = 0;
        }
    });
    var timers = [];
    var opened = [];
    var toastCalls = [];
    var userSearches = [];
    var pendingSearches = [];
    var messageSearches = [];
    var messageOptions = [];
    var pendingMessageSearches = [];
    var focused = [];
    var settings = options || {};
    var chats = settings.chats || [];
    var BF = {
        api: {
            searchUsers: function (query) {
                userSearches.push(query);
                return new Promise(function (resolve) {
                    pendingSearches.push({ query: query, resolve: resolve });
                });
            },
            searchMessages: function (query, searchOptions) {
                messageSearches.push(query);
                messageOptions.push(searchOptions);
                return new Promise(function (resolve, reject) {
                    pendingMessageSearches.push({ resolve: resolve, reject: reject });
                });
            },
            getPersonChatId: function (userId) {
                return settings.getPersonChatId
                    ? settings.getPersonChatId(userId)
                    : Promise.resolve({ chatId: 'chat-of-' + userId });
            }
        },
        i18n: {
            t: function (key) {
                return key;
            },
            current: function () {
                return 'en';
            }
        },
        utils: {
            escapeHtml: function (value) {
                return String(value);
            },
            isRestrictedUser: function (user) {
                return !!user && Number(user.accountStatus) > 0;
            },
            restrictedUserName: function (user) {
                return BF.i18n.t(Number(user.accountStatus) === 1
                    ? 'accountRestriction.userBlocked'
                    : 'accountRestriction.userDeleted');
            },
            attachmentPreviewHtml: function (type) {
                return '[' + type + ']';
            },
            formatChatListTime: function () {
                return '12:00';
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            querySelector: function (selector) {
                return selector === '#searchInput' ? input : selector === '#searchFilters' ? filtersEl : results;
            },
            createElement: function () {
                return makeEl();
            }
        },
        Promise: Promise,
        setTimeout: function (callback) {
            timers.push({ callback: callback, active: true });
            return timers.length;
        },
        clearTimeout: function (id) {
            if (timers[id - 1]) timers[id - 1].active = false;
        }
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/search.js'), 'utf8'), context);
    BF.search.init({
        getChats: function () {
            return chats;
        },
        getCachedUser: function (userId) {
            return (settings.cachedUsers || {})[userId] || null;
        },
        getMyUserId: function () {
            return 1;
        },
        showToast: function (message, isError) {
            toastCalls.push([message, isError]);
        },
        openChat: function (chatId, focusMessageId) {
            opened.push(chatId);
            if (focusMessageId) focused.push(focusMessageId);
        }
    });
    return {
        input: input,
        results: results,
        filters: filtersEl,
        opened: opened,
        focused: focused,
        toastCalls: toastCalls,
        userSearches: userSearches,
        messageSearches: messageSearches,
        messageOptions: messageOptions,
        answerMessages: function (index, hits, nextCursor) {
            pendingMessageSearches[index].resolve({ hits: hits, nextCursor: nextCursor || null });
        },
        failMessages: function (index) {
            pendingMessageSearches[index].reject(new Error('unavailable'));
        },
        type: function (text) {
            input.value = text;
            input.listeners.input();
        },
        fireDebounce: function () {
            timers.splice(0).forEach(function (timer) {
                if (timer.active) timer.callback();
            });
        },
        answer: function (index, users) {
            pendingSearches[index].resolve({ users: users });
        }
    };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('an empty query hides and clears the results', async function () {
        var h = createHarness();
        h.results.classList.add('visible');
        h.type('   ');
        assert.equal(h.results.classList.contains('visible'), false);
        assert.equal(h.results.children.length, 0);
    });

    await test('loaded chats are filtered instantly (case-insensitive); users arrive after the debounce', async function () {
        var h = createHarness({ chats: [{ id: 1, title: 'Alice Cooper' }, { id: 2, title: 'Bob' }, { id: 3 }] });
        h.type('ALI');
        assert.equal(h.results.classList.contains('visible'), true);
        assert.equal(h.results.children.length, 1, 'only the matching chat, synchronously');
        assert.deepEqual(h.userSearches, [], 'no request before the debounce fires');
        h.fireDebounce();
        assert.deepEqual(h.userSearches, ['ALI']);
        h.answer(0, [{ id: 10, username: 'alina', firstName: 'Alina', lastName: 'K' }]);
        await settle();
        assert.equal(h.results.children.length, 2, 'the chat plus the found user');
        assert.match(h.results.children[1].innerHTML, /@alina/);
    });

    await test('typing again cancels the pending debounce and drops a stale response', async function () {
        var h = createHarness();
        h.type('a');
        h.fireDebounce(); // запрос «a» ушёл
        h.type('ab'); // пользователь продолжил ввод
        h.fireDebounce(); // запрос «ab» ушёл
        assert.deepEqual(h.userSearches, ['a', 'ab']);
        h.answer(1, [{ id: 2, username: 'fresh' }]);
        await settle();
        h.answer(0, [{ id: 1, username: 'stale' }]); // запоздалый ответ на «a»
        await settle();
        assert.equal(h.results.children.length, 1);
        assert.match(h.results.children[0].innerHTML, /@fresh/);
    });

    await test('nothing found shows a message only when chats, users and messages are all empty', async function () {
        var h = createHarness();
        h.type('zzz');
        h.fireDebounce();
        h.answer(0, []);
        await settle();
        assert.doesNotMatch(h.results.innerHTML, /common\.nothingFound/, 'the message search is still running');
        h.answerMessages(0, []);
        await settle();
        assert.match(h.results.innerHTML, /common\.nothingFound/);
    });

    await test('clicking a chat opens it; clicking a user opens the chat with them; both reset the search', async function () {
        var h = createHarness({ chats: [{ id: 5, title: 'Team' }] });
        h.type('team');
        h.results.children[0].listeners.click();
        assert.deepEqual(h.opened, [5]);
        assert.equal(h.input.value, '');
        assert.equal(h.results.classList.contains('visible'), false);

        h.type('tea');
        h.fireDebounce();
        h.answer(0, [{ id: 77, username: 'tea' }]);
        await settle();
        h.results.children[1].listeners.click();
        await settle();
        assert.deepEqual(h.opened, [5, 'chat-of-77']);
    });

    await test('restricted users have a local label and UserUnavailable is shown when opening a chat', async function () {
        var h = createHarness({
            getPersonChatId: function () {
                return Promise.reject({ errorCode: '6BC0E7A1-5117-4910-9717-1784E6921F6F' });
            }
        });
        h.type('blocked');
        h.fireDebounce();
        h.answer(0, [{
            id: 44,
            username: 'private-name',
            firstName: 'Private',
            accountStatus: 1,
            profilePicturePreview: 'https://example.test/avatar.png'
        }]);
        await settle();
        var result = h.results.children[0].innerHTML;
        assert.match(result, /accountRestriction\.userBlocked/);
        assert.doesNotMatch(result, /private-name|avatar\.png/);
        h.results.children[0].listeners.click();
        await settle();
        assert.deepEqual(h.toastCalls, [['accountRestriction.userUnavailable', true]]);
    });

    function hit(overrides) {
        return Object.assign(
            {
                messageId: 900,
                chatId: '7',
                chatTitle: 'Team',
                isGroupChat: true,
                otherUserId: 0,
                authorUserId: 3,
                authorName: 'Bob',
                sentAt: 1,
                text: 'release notes',
                attachmentTypes: []
            },
            overrides
        );
    }

    await test('message hits appear under their own header and open the chat at the message', async function () {
        var h = createHarness({ chats: [{ id: '7', title: 'Team', picture: 'https://example.test/team.png' }] });
        h.type('release');
        h.fireDebounce();
        assert.deepEqual(h.messageSearches, ['release']);
        h.answer(0, []);
        h.answerMessages(0, [hit()]);
        await settle();
        assert.equal(h.results.children.length, 2, 'section header plus the hit');
        assert.equal(h.results.children[0].className, 'search-section-title');
        assert.equal(h.results.children[0].textContent, 'search.messages');
        var item = h.results.children[1].innerHTML;
        assert.match(item, /team\.png/, 'picture comes from the loaded chat, not the file id');
        assert.match(item, /Bob: release notes/);
        h.results.children[1].listeners.click();
        assert.deepEqual(h.opened, ['7']);
        assert.deepEqual(h.focused, [900]);
        assert.equal(h.input.value, '');
    });

    await test('attachment-only hits show the attachment preview; restricted DM peers keep the local label', async function () {
        var h = createHarness({ cachedUsers: { 44: { id: 44, accountStatus: 2 } } });
        h.type('photo');
        h.fireDebounce();
        h.answer(0, []);
        h.answerMessages(0, [hit({ isGroupChat: false, otherUserId: 44, chatTitle: 'Real Name', text: '', attachmentTypes: ['IMAGE'] })]);
        await settle();
        var item = h.results.children[1].innerHTML;
        assert.match(item, /accountRestriction\.userDeleted/);
        assert.doesNotMatch(item, /Real Name/);
        assert.match(item, /\[IMAGE\]/);
        assert.doesNotMatch(item, /Bob:/, 'no author prefix in personal chats');
    });

    await test('one-character queries skip the message search; stale and failed message responses are ignored', async function () {
        var h = createHarness();
        h.type('r');
        h.fireDebounce();
        assert.deepEqual(h.userSearches, ['r']);
        assert.deepEqual(h.messageSearches, [], 'too short for the server-side message search');

        h.type('re');
        h.fireDebounce();
        h.type('rel');
        h.fireDebounce();
        assert.deepEqual(h.messageSearches, ['re', 'rel']);
        h.answer(2, [{ id: 5, username: 'rel' }]);
        h.failMessages(1);
        await settle();
        h.answerMessages(0, [hit()]); // запоздалый ответ на «re»
        await settle();
        assert.equal(h.results.children.length, 1, 'only the fresh user; no stale hit, no error');
        assert.match(h.results.children[0].innerHTML, /@rel/);
    });

    // Элементы фильтров: [0] — строка чипов, [1] — панель.
    function chips(h) {
        return h.filters.children[0].children;
    }

    function panelControls(h) {
        return h.filters.children[1].children;
    }

    function openFilter(h, index) {
        chips(h)[index].listeners.click();
        return panelControls(h);
    }

    function lastLabelInput(label) {
        return label.children[label.children[0].type ? 0 : 1];
    }

    function moreButton(h) {
        return h.results.children[h.results.children.length - 1];
    }

    await test('show more requests the next page by cursor and appends hits without moving the scroll position', async function () {
        var h = createHarness();
        h.type('release');
        h.fireDebounce();
        assert.equal(JSON.stringify(h.messageOptions[0]), '{"pageSize":20}');
        h.answer(0, []);
        var cursor = { opaque: 'page-2' };
        h.answerMessages(0, [hit({ messageId: 1 }), hit({ messageId: 2 })], cursor);
        await settle();
        var more = moreButton(h);
        assert.equal(more.className, 'search-more', 'not a .search-result-item: mobile tap must not switch to the chat');
        assert.equal(more.textContent, 'search.loadMore');

        h.results.scrollTop = 480;
        more.listeners.click();
        assert.equal(moreButton(h).disabled, true, 'no double requests while the page loads');
        assert.equal(h.messageSearches[1], 'release');
        assert.equal(h.messageOptions[1].cursor, cursor, 'the server cursor goes back as is');
        h.answerMessages(1, [hit({ messageId: 2 }), hit({ messageId: 3 })]);
        await settle();
        var ids = h.results.children
            .filter(function (el) {
                return el.className === 'search-result-item search-hit';
            })
            .map(function (el) {
                return el.innerHTML;
            });
        assert.equal(ids.length, 3, 'duplicate hit from the next page is dropped');
        assert.equal(h.results.scrollTop, 480);
        assert.notEqual(moreButton(h).className, 'search-more', 'no button after the last page');

        h.type('releases');
        assert.equal(h.results.scrollTop, 0, 'a new query starts from the top');
    });

    await test('a failed page keeps the hits and offers retry; stale pages after a new query are ignored', async function () {
        var h = createHarness();
        h.type('release');
        h.fireDebounce();
        h.answer(0, []);
        h.answerMessages(0, [hit({ messageId: 1 })], { opaque: 1 });
        await settle();
        moreButton(h).listeners.click();
        h.failMessages(1);
        await settle();
        assert.equal(moreButton(h).textContent, 'search.retry');
        assert.equal(moreButton(h).disabled, false);
        assert.equal(h.results.children.length, 3, 'header, the hit and the retry button');

        moreButton(h).listeners.click();
        h.type('other');
        h.answerMessages(2, [hit({ messageId: 5, text: 'stale' })]);
        await settle();
        assert.doesNotMatch(
            h.results.children
                .map(function (el) {
                    return el.innerHTML || '';
                })
                .join(''),
            /stale/
        );
    });

    await test('only hits appended by show more get the entrance class; the first page is not animated', async function () {
        var h = createHarness();
        h.type('release');
        h.fireDebounce();
        h.answer(0, []);
        h.answerMessages(0, [hit({ messageId: 1 }), hit({ messageId: 2 })], { opaque: 'page-2' });
        await settle();
        function entering() {
            return h.results.children
                .filter(function (el) {
                    return el.className === 'search-result-item search-hit';
                })
                .map(function (el) {
                    return el.classList.contains('search-hit-enter');
                });
        }
        assert.deepEqual(entering(), [false, false], 'typed results are not animated');
        moreButton(h).listeners.click();
        h.answerMessages(1, [hit({ messageId: 2 }), hit({ messageId: 3 })]);
        await settle();
        assert.deepEqual(entering(), [false, false, true], 'only the appended hit fades in');
    });

    await test('filters stay hidden for short queries and appear once the message search is possible', async function () {
        var h = createHarness();
        assert.equal(h.filters.hidden, true);
        h.type('r');
        assert.equal(h.filters.hidden, true);
        h.type('re');
        assert.equal(h.filters.hidden, false);
        assert.equal(chips(h).length, 3, 'author, period and attachments; no reset without filters');
    });

    await test('an author picked from the hits narrows the search, and show more keeps the same filters', async function () {
        var h = createHarness();
        h.type('report');
        h.fireDebounce();
        h.answer(0, []);
        h.answerMessages(0, [
            hit({ messageId: 1, authorUserId: 3, authorName: 'Bob' }),
            hit({ messageId: 2, authorUserId: 0, authorUserUuid: 'remote-uuid', authorName: 'Eve' }),
            hit({ messageId: 3, authorUserId: 1, authorName: 'Me again' })
        ]);
        await settle();
        var panel = openFilter(h, 0);
        var labels = panel
            .filter(function (el) {
                return el.className.indexOf('search-filter-option') === 0;
            })
            .map(function (el) {
                return el.textContent;
            });
        assert.deepEqual(labels, ['search.author.any', 'search.author.me', 'Bob', 'Eve'], 'I appear once, as "me"');

        panel[3].listeners.click(); // Eve — федеративный автор
        assert.equal(h.filters.children[1].hidden, true, 'the panel closes after the pick');
        h.fireDebounce();
        var options = h.messageOptions[1];
        assert.equal(options.authorUserUuid, 'remote-uuid');
        assert.equal(options.authorUserId, undefined, 'only one side of the author oneof');
        assert.equal(chips(h)[0].textContent, 'Eve');
        assert.equal(chips(h)[3].className, 'search-filter-reset');

        h.answer(1, []);
        var cursor = { opaque: 2 };
        h.answerMessages(1, [hit({ messageId: 7 })], cursor);
        await settle();
        moreButton(h).listeners.click();
        assert.equal(h.messageOptions[2].cursor, cursor);
        assert.equal(h.messageOptions[2].authorUserUuid, 'remote-uuid');
    });

    await test('period maps local days to [from, before) and swaps reversed bounds', async function () {
        var h = createHarness();
        h.type('report');
        var panel = openFilter(h, 1);
        var from = lastLabelInput(panel[0]);
        var through = lastLabelInput(panel[1]);
        from.value = '2026-10-31';
        through.value = '2026-10-01';
        through.listeners.change();
        h.fireDebounce();
        var options = h.messageOptions[h.messageOptions.length - 1];
        assert.equal(from.value, '2026-10-01');
        assert.equal(through.value, '2026-10-31');
        assert.equal(options.sentFrom, new Date(2026, 9, 1).getTime());
        assert.equal(options.sentBefore, new Date(2026, 10, 1).getTime(), 'the "to" day is inclusive');
        assert.equal(h.filters.children[1].hidden, false, 'the period panel stays open while editing');
    });

    await test('attachment types imply "with attachments"; "without" clears and disables them', async function () {
        var h = createHarness();
        h.type('report');
        var panel = openFilter(h, 2);
        var radios = panel.slice(0, 3).map(lastLabelInput);
        var docs = lastLabelInput(panel[6]);
        assert.equal(docs.value, 'DOCUMENT');
        docs.checked = true;
        docs.listeners.change();
        h.fireDebounce();
        var options = h.messageOptions[h.messageOptions.length - 1];
        assert.equal(options.attachmentPresence, 'WITH');
        assert.deepEqual(Array.from(options.attachmentTypes), ['DOCUMENT']);
        assert.equal(radios[1].checked, true);

        radios[2].checked = true;
        radios[2].listeners.change();
        h.fireDebounce();
        options = h.messageOptions[h.messageOptions.length - 1];
        assert.equal(options.attachmentPresence, 'WITHOUT');
        assert.equal(options.attachmentTypes, undefined, 'the server rejects types together with "without"');
        assert.equal(docs.checked, false);
        assert.equal(docs.disabled, true);
    });

    await test('filters alone search messages without text; an empty or failed filtered search says so', async function () {
        var h = createHarness();
        h.type('report');
        openFilter(h, 0);
        panelControls(h)[1].listeners.click(); // «Я»
        h.type('');
        assert.equal(h.filters.hidden, false, 'active filters keep the bar visible');
        h.fireDebounce();
        assert.equal(h.userSearches.length, 0, 'no user search without text');
        assert.equal(h.messageSearches[h.messageSearches.length - 1], '');
        assert.equal(h.messageOptions[h.messageOptions.length - 1].authorUserId, 1);
        h.failMessages(h.messageSearches.length - 1);
        await settle();
        assert.equal(h.results.children[0].className, 'search-section-title');
        assert.equal(h.results.children[1].textContent, 'search.messagesFailed');

        h.type('zzz');
        h.fireDebounce();
        h.answer(h.userSearches.length - 1, []);
        h.answerMessages(h.messageSearches.length - 1, []);
        await settle();
        assert.match(h.results.innerHTML, /common\.nothingFound/);

        chips(h)[3].listeners.click(); // сброс
        h.fireDebounce();
        assert.equal(h.messageOptions[h.messageOptions.length - 1].authorUserId, undefined);
    });

    await test('opening a hit resets the filters', async function () {
        var h = createHarness();
        h.type('report');
        openFilter(h, 0);
        panelControls(h)[1].listeners.click();
        h.fireDebounce();
        h.answer(h.userSearches.length - 1, []);
        h.answerMessages(h.messageSearches.length - 1, [hit()]);
        await settle();
        h.results.children[1].listeners.click();
        assert.equal(h.filters.hidden, true);
        h.type('report');
        h.fireDebounce();
        assert.equal(h.messageOptions[h.messageOptions.length - 1].authorUserId, undefined);
    });

    await test('closing the search drops a page still in flight', async function () {
        var h = createHarness();
        h.type('release');
        h.fireDebounce();
        h.answer(0, []);
        h.answerMessages(0, [hit({ messageId: 1 })], { opaque: 1 });
        await settle();
        moreButton(h).listeners.click();
        h.results.children[1].listeners.click();
        h.answerMessages(1, [hit({ messageId: 2 })]);
        await settle();
        assert.equal(h.results.classList.contains('visible'), false);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
