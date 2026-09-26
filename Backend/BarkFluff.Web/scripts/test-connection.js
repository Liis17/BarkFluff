const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 20; i++) await new Promise((resolve) => setImmediate(resolve));
}

function plain(value) {
    return JSON.parse(JSON.stringify(value));
}

function makeEl(id) {
    var names = new Set();
    return {
        id: id,
        textContent: '',
        disabled: false,
        scrollHeight: 0,
        scrollTop: 0,
        clientHeight: 0,
        listeners: {},
        statusEls: {},
        classList: {
            add: function (name) {
                names.add(name);
            },
            remove: function (name) {
                names.delete(name);
            },
            contains: function (name) {
                return names.has(name);
            },
            toggle: function (name, force) {
                if (force) names.add(name);
                else names.delete(name);
            }
        },
        addEventListener: function (type, callback) {
            this.listeners[type] = callback;
        },
        querySelector: function (selector) {
            var match = /\.msg-status\[data-msg-id="([^"]+)"\]/.exec(selector);
            return (match && this.statusEls[match[1]]) || null;
        }
    };
}

function createHarness(options) {
    options = options || {};
    var els = {};
    [
        'messagesArea',
        'loadingMessages',
        'scrollToBottomBtn',
        'connectionBanner',
        'connectionBannerText',
        'connectionRetryButton',
        'connectionSynced'
    ].forEach(function (id) {
        els['#' + id] = makeEl(id);
    });
    var timers = [];
    var nextTimer = 1;
    var handlers = {};
    var calls = [];
    var state = {
        chatId: options.chatId === undefined ? 7 : options.chatId,
        chatType: options.chatType || 0,
        messages: options.messages || [],
        fetched: options.fetched || [],
        info: options.info || { lastMessageId: 3 },
        myUserId: 1,
        loadingOlder: false,
        separatorId: null,
        reconnectResult: true
    };
    var BF = {
        api: {
            getChatInfo: function (chatId) {
                calls.push(['getChatInfo', chatId]);
                return options.chatInfo ? options.chatInfo(chatId) : Promise.resolve(state.info);
            },
            listMessages: function (chatId, fromId, count, around) {
                calls.push(['listMessages', chatId, fromId, count, around]);
                return options.list ? options.list() : Promise.resolve({ messages: state.fetched });
            }
        },
        i18n: {
            t: function (key) {
                return key;
            }
        },
        realtime: {
            on: function (event, callback) {
                handlers[event] = callback;
            },
            reconnect: function () {
                calls.push(['reconnect']);
                return state.reconnectResult;
            }
        },
        feed: {
            isLoadingOlder: function () {
                return state.loadingOlder;
            },
            setResyncSeparatorId: function (id) {
                calls.push(['separator', id]);
                state.separatorId = id;
            },
            getResyncSeparatorId: function () {
                return state.separatorId;
            },
            clearNewerGap: function (resetLoading) {
                calls.push(['clearGap', resetLoading]);
            }
        },
        markRead: {
            markSoon: function (ids) {
                calls.push(['markSoon', Array.from(ids)]);
            }
        },
        messages: {
            updateMessageStatus: function (el, read) {
                calls.push(['status', el.id, read]);
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            querySelector: function (selector) {
                return els[selector] || null;
            }
        },
        setTimeout: function (callback, ms) {
            var id = nextTimer++;
            timers.push({ id: id, callback: callback, ms: ms });
            return id;
        },
        clearTimeout: function (id) {
            timers = timers.filter(function (timer) {
                return timer.id !== id;
            });
        },
        Promise: Promise
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/connection.js'), 'utf8'), context);
    BF.connection.init({
        getCurrentChatId: function () {
            return state.chatId;
        },
        getCurrentChatType: function () {
            return state.chatType;
        },
        getMyUserId: function () {
            return state.myUserId;
        },
        getMessages: function () {
            return state.messages;
        },
        setMessages: function (value) {
            calls.push([
                'setMessages',
                value.map(function (m) {
                    return m.id;
                })
            ]);
            state.messages = value;
        },
        setCurrentChatInfo: function (info) {
            calls.push(['setInfo', info]);
        },
        refreshChatList: function () {
            calls.push(['refresh']);
            return options.refresh ? options.refresh() : Promise.resolve(true);
        },
        reloadPrivateChat: function () {
            calls.push(['reloadPrivate']);
            return Promise.resolve(options.privateResult !== false);
        },
        mergePendingUploads: function (chatId) {
            calls.push(['merge', chatId]);
        },
        reconcilePendingUpload: function (chatId, msg) {
            return !!options.reconciled && options.reconciled.indexOf(msg.id) >= 0;
        },
        renderMessages: function () {
            calls.push(['render']);
            return Promise.resolve();
        },
        appendMessageToView: function (msg, separatorKey) {
            calls.push(['append', msg.id, separatorKey]);
            return Promise.resolve();
        },
        scrollToBottom: function () {
            calls.push(['scrollToBottom']);
        },
        applyMessageDelete: function (chatId, id) {
            calls.push(['delete', chatId, id]);
        },
        applyMessageEdit: function (chatId, msg) {
            calls.push(['edit', chatId, msg.id]);
        }
    });
    var area = els['#messagesArea'];
    area.scrollHeight = 1000;
    area.clientHeight = 100;
    area.scrollTop = 900; // у нижнего края
    return {
        BF: BF,
        els: els,
        state: state,
        area: area,
        calls: function () {
            return plain(calls);
        },
        count: function (name) {
            return calls.filter(function (call) {
                return call[0] === name;
            }).length;
        },
        emit: function (event, data) {
            handlers[event](data);
        },
        status: function (data) {
            handlers.connection_status(data);
        },
        pendingTimers: function (ms) {
            return timers.filter(function (timer) {
                return timer.ms === ms;
            }).length;
        },
        flushTimers: function (ms) {
            var due = timers.filter(function (timer) {
                return timer.ms === ms;
            });
            timers = timers.filter(function (timer) {
                return timer.ms !== ms;
            });
            due.forEach(function (timer) {
                timer.callback();
            });
        },
        visible: function (selector) {
            return els[selector].classList.contains('visible');
        }
    };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('the banner follows the connection state; the retry button reconnects and is locked until the next status', async function () {
        var h = createHarness();
        var banner = h.els['#connectionBanner'];
        var text = h.els['#connectionBannerText'];
        h.status({ state: 'offline' });
        assert.equal(h.visible('#connectionBanner'), true);
        assert.equal(banner.classList.contains('offline'), true);
        assert.equal(text.textContent, 'connection.offline');
        h.status({ state: 'reconnecting' });
        assert.equal(banner.classList.contains('offline'), false);
        assert.equal(text.textContent, 'connection.reconnecting');
        h.status({ state: 'connected' });
        assert.equal(h.visible('#connectionBanner'), false);
        h.status({ connected: true });
        assert.equal(h.visible('#connectionBanner'), false, 'the legacy boolean shape is understood');
        h.status({ connected: false });
        assert.equal(h.visible('#connectionBanner'), true);
        h.status(null);
        assert.equal(text.textContent, 'connection.reconnecting');

        var retry = h.els['#connectionRetryButton'];
        retry.listeners.click();
        assert.equal(h.count('reconnect'), 1);
        assert.equal(retry.disabled, true);
        h.status({ state: 'offline' });
        assert.equal(retry.disabled, false, 'the next status unlocks the button');
        h.state.reconnectResult = false;
        retry.listeners.click();
        assert.equal(retry.disabled, false, 'nothing to wait for when the reconnect did not start');
    });

    await test('a problem after the first status triggers exactly one catch-up on reconnect; the initial status does not', async function () {
        var h = createHarness();
        h.status({ state: 'reconnecting' }); // самый первый статус — ещё не «проблема»
        h.status({ state: 'connected' });
        await settle();
        assert.equal(h.count('refresh'), 0);
        assert.equal(h.visible('#connectionSynced'), false);

        h.status({ state: 'offline' });
        h.status({ state: 'connected' });
        await settle();
        assert.equal(h.count('refresh'), 1);
        assert.equal(h.count('getChatInfo'), 1, 'the tail of the open chat is re-synced too');
        assert.equal(h.visible('#connectionSynced'), true);
        assert.equal(h.pendingTimers(3000), 1);
        h.flushTimers(3000);
        assert.equal(h.visible('#connectionSynced'), false, 'the chip goes away by itself');
        assert.equal(h.count('reconnect'), 0, 'a successful catch-up does not restart the transport');

        h.status({ state: 'connected' });
        await settle();
        assert.equal(h.count('refresh'), 1, 'the problem is cleared: no second catch-up');
    });

    await test('going offline hides the synced chip; without an open chat the chip is not shown but the problem is cleared', async function () {
        var h = createHarness();
        h.status({ state: 'connected' });
        h.status({ state: 'offline' });
        h.status({ state: 'connected' });
        await settle();
        assert.equal(h.visible('#connectionSynced'), true);
        h.status({ state: 'offline' });
        assert.equal(h.visible('#connectionSynced'), false);
        assert.equal(h.pendingTimers(3000), 0, 'the pending hide timer is cancelled');

        var closed = createHarness({ chatId: null });
        closed.status({ state: 'connected' });
        closed.status({ state: 'offline' });
        closed.status({ state: 'connected' });
        await settle();
        assert.equal(closed.count('refresh'), 1);
        assert.equal(closed.count('getChatInfo'), 0);
        assert.equal(closed.visible('#connectionSynced'), false);
        closed.status({ state: 'connected' });
        await settle();
        assert.equal(closed.count('refresh'), 1);
    });

    await test('a failed catch-up shows no chip and asks the transport to reconnect; a failed tail check does too', async function () {
        var failedList = createHarness({
            refresh: function () {
                return Promise.resolve(false);
            }
        });
        failedList.status({ state: 'connected' });
        failedList.status({ state: 'offline' });
        failedList.status({ state: 'connected' });
        await settle();
        assert.equal(failedList.count('reconnect'), 1);
        assert.equal(failedList.visible('#connectionSynced'), false);

        var failedTail = createHarness({
            chatInfo: function () {
                return Promise.reject(new Error('x'));
            }
        });
        failedTail.status({ state: 'connected' });
        failedTail.status({ state: 'offline' });
        failedTail.status({ state: 'connected' });
        await settle();
        assert.equal(failedTail.count('reconnect'), 1);
        assert.equal(failedTail.visible('#connectionSynced'), false);
    });

    await test('a status change during a catch-up discards its result and runs one more pass; overlapping passes are not started', async function () {
        var release;
        var first = true;
        var h = createHarness({
            chatId: null,
            refresh: function () {
                if (!first) return Promise.resolve(true);
                first = false;
                return new Promise(function (resolve) {
                    release = resolve;
                });
            }
        });
        h.status({ state: 'connected' });
        h.status({ state: 'offline' });
        h.status({ state: 'connected' }); // проход №1 завис на обновлении списка
        h.status({ state: 'offline' });
        h.status({ state: 'connected' }); // проход уже идёт — второй не стартует
        assert.equal(h.count('refresh'), 1);
        release(true);
        await settle();
        assert.equal(h.count('refresh'), 2, 'the stale pass is repeated once the connection is back');
        h.status({ state: 'connected' });
        await settle();
        assert.equal(h.count('refresh'), 2, 'and the second pass cleared the problem');
    });

    await test('resync events are debounced into one pass: the sidebar without an open chat, the tail check with one', async function () {
        var closed = createHarness({ chatId: null });
        closed.emit('resync');
        closed.emit('resync');
        closed.emit('resync');
        assert.equal(closed.pendingTimers(1200), 1);
        closed.flushTimers(1200);
        await settle();
        assert.equal(closed.count('refresh'), 1);
        assert.equal(closed.count('getChatInfo'), 0);
        closed.emit('resync');
        assert.equal(closed.pendingTimers(1200), 1, 'a later resync schedules a new pass');

        var open = createHarness();
        open.emit('resync');
        open.emit('resync');
        open.flushTimers(1200);
        await settle();
        assert.equal(open.count('getChatInfo'), 1);
        assert.equal(open.count('refresh'), 0, 'nothing was missed, so the sidebar is left alone');
    });

    await test('returning to the tab refreshes the chat list and the open chat tail', async function () {
        var h = createHarness();
        h.emit('tab_visible');
        await settle();
        assert.equal(h.count('refresh'), 1);
        assert.equal(h.count('getChatInfo'), 1);
    });

    await test('the tail check is skipped without a chat, while older messages load or the chat is loading; private chats reload', async function () {
        var none = createHarness({ chatId: null });
        assert.equal(await none.BF.connection.resyncCurrentChatTail(), true);
        assert.equal(none.count('getChatInfo'), 0);

        var loadingOlder = createHarness();
        loadingOlder.state.loadingOlder = true;
        assert.equal(await loadingOlder.BF.connection.resyncCurrentChatTail(), false);
        assert.equal(loadingOlder.count('getChatInfo'), 0);

        var loading = createHarness();
        loading.els['#loadingMessages'].classList.add('visible');
        assert.equal(await loading.BF.connection.resyncCurrentChatTail(), false);
        assert.equal(loading.count('getChatInfo'), 0);

        var priv = createHarness({ chatType: 1 });
        assert.equal(await priv.BF.connection.resyncCurrentChatTail(), true);
        assert.equal(priv.count('reloadPrivate'), 1);
        assert.equal(priv.count('getChatInfo'), 0);
        var privFail = createHarness({ chatType: 1, privateResult: false });
        assert.equal(await privFail.BF.connection.resyncCurrentChatTail(), false);
    });

    await test('the tail is requested from the first unread message, else from the last one; API failures report false', async function () {
        var unread = createHarness({ info: { firstUnreadMessageId: 2, lastMessageId: 9 } });
        assert.equal(await unread.BF.connection.resyncCurrentChatTail(), true);
        assert.deepEqual(unread.calls().slice(0, 2), [
            ['getChatInfo', 7],
            ['listMessages', 7, 2, 30, 10]
        ]);
        var last = createHarness({ info: { lastMessageId: 9 } });
        await last.BF.connection.resyncCurrentChatTail();
        assert.deepEqual(last.calls()[1], ['listMessages', 7, 9, 30, 10]);

        var errorInfo = createHarness({
            chatInfo: function () {
                return Promise.resolve({ error: 'x' });
            }
        });
        assert.equal(await errorInfo.BF.connection.resyncCurrentChatTail(), false);
        var noMessages = createHarness({
            list: function () {
                return Promise.resolve({});
            }
        });
        assert.equal(await noMessages.BF.connection.resyncCurrentChatTail(), false);
        var rejected = createHarness({
            list: function () {
                return Promise.reject(new Error('x'));
            }
        });
        assert.equal(await rejected.BF.connection.resyncCurrentChatTail(), false);
    });

    await test('switching chats while the tail is being fetched applies nothing', async function () {
        var releaseInfo;
        var early = createHarness({
            fetched: [{ id: 1 }],
            chatInfo: function () {
                return new Promise(function (resolve) {
                    releaseInfo = resolve;
                });
            }
        });
        var pending = early.BF.connection.resyncCurrentChatTail();
        early.state.chatId = 8;
        releaseInfo({ lastMessageId: 1 });
        assert.equal(await pending, true);
        assert.equal(early.count('listMessages'), 0);

        var releaseList;
        var late = createHarness({
            fetched: [{ id: 1 }],
            list: function () {
                return new Promise(function (resolve) {
                    releaseList = resolve;
                });
            }
        });
        var pendingList = late.BF.connection.resyncCurrentChatTail();
        await settle();
        late.state.chatId = 8;
        releaseList({ messages: [{ id: 1 }] });
        assert.equal(await pendingList, true);
        assert.equal(late.count('setInfo'), 0);
        assert.equal(late.count('append'), 0);
        assert.equal(late.count('render'), 0);
    });

    await test('an unchanged tail leaves the view alone', async function () {
        var messages = [
            { id: 1, senderId: 2, content: { text: 'a' }, readBy: [] },
            { id: 2, senderId: 1, content: { text: 'b' }, readBy: [2] }
        ];
        var h = createHarness({ messages: messages, fetched: plain(messages) });
        assert.equal(await h.BF.connection.resyncCurrentChatTail(), true);
        assert.deepEqual(
            h.calls().map(function (call) {
                return call[0];
            }),
            ['getChatInfo', 'listMessages']
        );
    });

    await test('new tail messages at the bottom are appended in order, marked as read when incoming, and the list is refreshed', async function () {
        var h = createHarness({
            messages: [
                { id: 1, senderId: 2 },
                { id: 2, senderId: 1 },
                { id: 3, senderId: 2 }
            ],
            fetched: [
                { id: 5, senderId: 1 },
                { id: 2, senderId: 1 },
                { id: 4, senderId: 2 },
                { id: 3, senderId: 2 }
            ]
        });
        var button = h.els['#scrollToBottomBtn'];
        button.classList.add('visible');
        assert.equal(await h.BF.connection.resyncCurrentChatTail(), true);
        assert.deepEqual(h.calls(), [
            ['getChatInfo', 7],
            ['listMessages', 7, 3, 30, 10],
            ['setInfo', { lastMessageId: 3 }],
            ['separator', null],
            ['append', 4, null],
            ['append', 5, null],
            ['scrollToBottom'],
            ['markSoon', [4]],
            ['refresh']
        ]);
        assert.deepEqual(
            h.state.messages.map(function (m) {
                return m.id;
            }),
            [1, 2, 3, 4, 5],
            'the live array is extended (message 1 is outside the fetched range and is not treated as deleted)'
        );
        assert.equal(button.classList.contains('visible'), false);
    });

    await test('new tail messages while scrolled up get the separator on the first one and the scroll button, no auto-scroll', async function () {
        var h = createHarness({
            messages: [{ id: 1, senderId: 2 }],
            fetched: [
                { id: 1, senderId: 2 },
                { id: 2, senderId: 2 },
                { id: 3, senderId: 2 }
            ]
        });
        h.area.scrollTop = 0;
        assert.equal(await h.BF.connection.resyncCurrentChatTail(), true);
        assert.deepEqual(h.calls().slice(3), [
            ['separator', 2],
            ['append', 2, 'chat.newMessages'],
            ['append', 3, null],
            ['refresh']
        ]);
        assert.equal(h.visible('#scrollToBottomBtn'), true);
    });

    await test('a message that matches a pending upload is not appended twice; the separator goes to the first appended one', async function () {
        var h = createHarness({
            reconciled: [4],
            messages: [{ id: 3, senderId: 2 }],
            fetched: [
                { id: 3, senderId: 2 },
                { id: 4, senderId: 1 },
                { id: 5, senderId: 2 }
            ]
        });
        h.area.scrollTop = 0;
        await h.BF.connection.resyncCurrentChatTail();
        assert.deepEqual(h.calls().slice(3), [['separator', 4], ['append', 5, 'chat.newMessages'], ['refresh']]);
        assert.deepEqual(
            h.state.messages.map(function (m) {
                return m.id;
            }),
            [3, 5]
        );
    });

    await test('new messages that are not at the tail (or an empty window) replace the buffer with a full render', async function () {
        var atBottom = createHarness({
            messages: [{ id: 10 }, { id: 11 }],
            fetched: [{ id: 5 }, { id: 10 }, { id: 11 }]
        });
        assert.equal(await atBottom.BF.connection.resyncCurrentChatTail(), true);
        assert.deepEqual(atBottom.calls().slice(2), [
            ['setInfo', { lastMessageId: 3 }],
            ['separator', null],
            ['setMessages', [5, 10, 11]],
            ['merge', 7],
            ['clearGap', false],
            ['render'],
            ['scrollToBottom'],
            ['refresh']
        ]);

        var scrolledUp = createHarness({
            messages: [{ id: 10 }, { id: 11 }],
            fetched: [{ id: 5 }, { id: 10 }, { id: 11 }]
        });
        scrolledUp.area.scrollTop = 0;
        await scrolledUp.BF.connection.resyncCurrentChatTail();
        assert.deepEqual(scrolledUp.calls().slice(3), [
            ['separator', 5],
            ['setMessages', [5, 10, 11]],
            ['merge', 7],
            ['clearGap', false],
            ['render'],
            ['refresh']
        ]);

        var empty = createHarness({ messages: [], fetched: [{ id: 1 }, { id: 2 }] });
        await empty.BF.connection.resyncCurrentChatTail();
        assert.equal(empty.count('render'), 1);
        assert.equal(empty.count('append'), 0);
    });

    await test('the user counts as being at the bottom within 300px of the end', async function () {
        function separatorFor(scrollTop) {
            var h = createHarness({
                messages: [{ id: 1, senderId: 2 }],
                fetched: [
                    { id: 1, senderId: 2 },
                    { id: 2, senderId: 2 }
                ]
            });
            h.area.scrollTop = scrollTop; // scrollHeight 1000, clientHeight 100
            return h.BF.connection.resyncCurrentChatTail().then(function () {
                return h.state.separatorId;
            });
        }
        assert.equal(await separatorFor(700), null, '200px from the end is still the bottom');
        assert.equal(await separatorFor(500), 2, '400px from the end is not');
    });

    await test('edits, deletions and read marks in the window are applied point-wise', async function () {
        var messages = [
            { id: 1, senderId: 2, content: { text: 'a' }, readBy: [] },
            { id: 2, senderId: 1, readBy: [] },
            { id: 3, senderId: 1, readBy: [] },
            { id: 4, senderId: 1, readBy: [] },
            { id: 9, senderId: 1, readBy: [] } // выше диапазона выборки — не удалено, просто не попало в неё
        ];
        var h = createHarness({
            messages: messages,
            fetched: [
                { id: 1, senderId: 2, content: { text: 'b' }, editedAt: 5, readBy: [] },
                { id: 3, senderId: 1, readBy: [2] },
                { id: 4, senderId: 1, readBy: [1] }
            ]
        });
        h.els['#messagesArea'].statusEls['3'] = { id: 'el3' };
        h.els['#messagesArea'].statusEls['4'] = { id: 'el4' };
        assert.equal(await h.BF.connection.resyncCurrentChatTail(), true);
        assert.deepEqual(h.calls().slice(3), [
            ['separator', null],
            ['delete', 7, 2],
            ['edit', 7, 1],
            ['status', 'el3', true],
            ['status', 'el4', false],
            ['refresh']
        ]);
        assert.deepEqual(plain(messages[2].readBy), [2]);
        assert.equal(h.count('scrollToBottom'), 0, 'no scrolling when nothing new arrived');
    });

    await test('diffFetchedTail: identical windows give null; ids compare as strings', function () {
        var h = createHarness({ messages: [{ id: 5, content: { text: 'x' }, readBy: [1] }] });
        var diff = h.BF.connection.diffFetchedTail;
        assert.equal(diff([{ id: '5', content: { text: 'x' }, readBy: [1] }]), null);
        assert.equal(diff([]), null);
    });

    await test('diffFetchedTail: edits win over read marks, news are sorted, deletions only count inside the fetched range', function () {
        var h = createHarness({
            messages: [
                { id: 1, content: { text: 'a' } },
                { id: 3, content: { text: 'a' }, editedAt: 1 },
                { id: 4, content: { text: 'a' } },
                { id: 6, content: { text: 'a' } }
            ]
        });
        var result = plain(
            h.BF.connection.diffFetchedTail([
                { id: 9, content: { text: 'n' } },
                { id: 3, content: { text: 'a' }, editedAt: 2 },
                { id: 4, content: { text: 'b' }, readBy: [2] },
                { id: 8, content: { text: 'n' } }
            ])
        );
        assert.deepEqual(
            result.edits.map(function (m) {
                return m.id;
            }),
            [3, 4],
            'an edit by editedAt alone and by text alone; message 4 changed text and readBy but counts once'
        );
        assert.deepEqual(result.readUpdates, []);
        assert.deepEqual(
            result.news.map(function (m) {
                return m.id;
            }),
            [8, 9]
        );
        assert.deepEqual(result.deletes, [6], 'message 6 is inside 3..9 and missing; message 1 is outside the range');
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
