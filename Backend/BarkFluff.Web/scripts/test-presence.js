const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function makeEl(userId) {
    var names = new Set();
    return {
        userId: userId,
        textContent: '',
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
        }
    };
}

function createHarness(options) {
    options = options || {};
    var header = makeEl();
    var dots = (options.dots || []).map(makeEl);
    var timers = [];
    var nextTimer = 1;
    var handlers = {};
    var subscriptions = [];
    var getUserCalls = [];
    var cache = new Map(options.cache ? Object.entries(options.cache).map((e) => [Number(e[0]), e[1]]) : []);
    var state = {
        chatId: options.chatId === undefined ? 7 : options.chatId,
        chatInfo: options.chatInfo === undefined ? { isGroupChat: false, membersId: [1, 5] } : options.chatInfo,
        peerIsBot: !!options.peerIsBot,
        chats: options.chats || []
    };
    var BF = {
        i18n: {
            t: function (key, params) {
                return params ? key + ' ' + JSON.stringify(params) : key;
            },
            tp: function (key, count) {
                return key + ':' + count;
            }
        },
        utils: {
            isStatusOnline: function (status) {
                return status === 'online';
            },
            formatLastSeen: function (lastSeen) {
                return 'seen:' + lastSeen;
            }
        },
        realtime: {
            on: function (event, callback) {
                handlers[event] = callback;
            },
            changeOnlineSubscription: function (ids) {
                subscriptions.push(Array.from(ids).sort());
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            querySelector: function (selector) {
                return selector === '#chatHeaderStatus' ? header : null;
            },
            querySelectorAll: function (selector) {
                var match = /\.online-dot\[data-online-user="([^"]+)"\]/.exec(selector);
                return dots.filter(function (dot) {
                    return match && String(dot.userId) === match[1];
                });
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
        Promise: Promise,
        Map: Map,
        Set: Set
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/presence.js'), 'utf8'), context);
    BF.presence.init({
        getChats: function () {
            return state.chats;
        },
        getCurrentChatId: function () {
            return state.chatId;
        },
        getCurrentChatInfo: function () {
            return state.chatInfo;
        },
        getPeerIsBot: function () {
            return state.peerIsBot;
        },
        getMyUserId: function () {
            return 1;
        },
        getCachedUser: function (userId) {
            return cache.get(userId);
        },
        getUser: function (userId) {
            getUserCalls.push(userId);
            var fetched = options.fetch && options.fetch[userId];
            return Promise.resolve(fetched).then(function (user) {
                if (user) cache.set(userId, user);
                return user;
            });
        }
    });
    return {
        BF: BF,
        state: state,
        header: header,
        dots: dots,
        subscriptions: subscriptions,
        getUserCalls: getUserCalls,
        status: function (userId, status, lastSeen) {
            handlers.online_status({ userId: userId, status: status, lastSeen: lastSeen });
        },
        typing: function (userId, action, chatId) {
            handlers.typing({ chatId: chatId === undefined ? 7 : chatId, userId: userId, action: action });
        },
        pendingTimers: function () {
            return timers.length;
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
        }
    };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('statuses are remembered and reported by isOnline / getEntry', function () {
        var h = createHarness();
        assert.equal(h.BF.presence.isOnline(5), false);
        assert.equal(h.BF.presence.getEntry(5), undefined);
        h.status(5, 'online', 10);
        assert.equal(h.BF.presence.isOnline(5), true);
        assert.deepEqual(JSON.parse(JSON.stringify(h.BF.presence.getEntry(5))), { status: 'online', lastSeen: 10 });
        h.status(5, 'offline', 20);
        assert.equal(h.BF.presence.isOnline(5), false);
        h.BF.presence.handleStatus(6, 'online', 30); // тот же путь, что и у события
        assert.equal(h.BF.presence.isOnline(6), true);
    });

    await test('a status event toggles only the online dots of that user', function () {
        var h = createHarness({ chatInfo: null, dots: [5, 5, 6] });
        h.status(5, 'online', 1);
        assert.deepEqual(
            h.dots.map(function (dot) {
                return dot.classList.contains('visible');
            }),
            [true, true, false]
        );
        h.status(5, 'offline', 2);
        assert.equal(h.dots[0].classList.contains('visible'), false);
        assert.equal(h.dots[1].classList.contains('visible'), false);
    });

    await test('the header shows online / last seen only for the peer of an open private chat', function () {
        var h = createHarness();
        h.status(5, 'online', 1);
        assert.equal(h.header.textContent, 'status.online');
        assert.equal(h.header.classList.contains('online'), true);
        h.status(5, 'offline', 99);
        assert.equal(h.header.textContent, 'seen:99');
        assert.equal(h.header.classList.contains('online'), false);

        h.status(6, 'online', 1); // не собеседник
        assert.equal(h.header.textContent, 'seen:99');

        var group = createHarness({ chatInfo: { isGroupChat: true, membersId: [1, 5] } });
        group.status(5, 'online', 1);
        assert.equal(group.header.textContent, '');

        var bot = createHarness({ peerIsBot: true });
        bot.status(5, 'online', 1);
        assert.equal(bot.header.textContent, '');

        var loading = createHarness({ chatInfo: null });
        loading.status(5, 'online', 1);
        assert.equal(loading.header.textContent, '');
    });

    await test('typing in a private chat replaces the status, expires after 6s and restores it', function () {
        var h = createHarness();
        h.status(5, 'online', 1);
        h.typing(5, 1);
        assert.equal(h.header.textContent, 'status.typing');
        h.typing(5, 1);
        assert.equal(h.pendingTimers(), 1, 'a repeated event restarts the timer instead of adding one');
        h.flushTimers(6000);
        assert.equal(h.header.textContent, 'status.online');
        assert.equal(h.header.classList.contains('online'), true);

        h.typing(5, 1);
        h.typing(5, 2); // «перестал печатать»
        assert.equal(h.pendingTimers(), 0);
        assert.equal(h.header.textContent, 'status.online');
    });

    await test('a status update does not overwrite the typing indicator', function () {
        var h = createHarness();
        h.typing(5, 1);
        h.status(5, 'online', 1);
        assert.equal(h.header.textContent, 'status.typing');
        h.typing(5, 2);
        assert.equal(h.header.textContent, 'status.online', 'the fresh status shows once typing stops');
    });

    await test('typing events of other chats, of myself or without an open chat are ignored; chat ids compare case-insensitively', function () {
        var h = createHarness();
        h.typing(5, 1, 8);
        h.typing(1, 1);
        assert.equal(h.header.textContent, '');
        assert.equal(h.pendingTimers(), 0);

        var noChat = createHarness({ chatId: null });
        noChat.typing(5, 1);
        assert.equal(noChat.pendingTimers(), 0);

        var uuid = createHarness({ chatId: 'abc-DEF' });
        uuid.typing(5, 1, 'ABC-def');
        assert.equal(uuid.header.textContent, 'status.typing');
    });

    await test('typing while the chat info is still loading writes nothing to the header', function () {
        var h = createHarness({ chatInfo: null });
        h.typing(5, 1);
        assert.equal(h.header.textContent, '');
        assert.equal(h.getUserCalls.length, 0);
    });

    await test('group typing names the typers (first name, username, or "someone"), capped at three', async function () {
        var h = createHarness({
            chatInfo: { isGroupChat: true, membersId: [1, 2, 3, 4, 5] },
            cache: { 2: { firstName: 'Ann Lee' }, 3: { username: 'bob' } }
        });
        h.typing(2, 1);
        assert.equal(h.header.textContent, 'status.typing.named {"names":"Ann"}');
        h.typing(3, 1);
        assert.equal(h.header.textContent, 'status.typing.many {"names":"Ann, bob"}');
        h.typing(4, 1);
        h.typing(5, 1);
        assert.equal(h.header.textContent, 'status.typing.many {"names":"Ann, bob, common.someone"}');

        h.typing(2, 2);
        h.typing(3, 2);
        h.typing(4, 2);
        h.typing(5, 2);
        assert.equal(h.header.textContent, 'group.memberCount:5');
        assert.equal(h.header.classList.contains('online'), false);
    });

    await test('group typing loads an unknown user and re-renders with the real name; private typing does not', async function () {
        var h = createHarness({
            chatInfo: { isGroupChat: true, membersId: [1, 2, 3] },
            fetch: { 2: { firstName: 'Ann' } }
        });
        h.typing(2, 1);
        assert.equal(h.header.textContent, 'status.typing.named {"names":"common.someone"}');
        await settle();
        assert.equal(h.header.textContent, 'status.typing.named {"names":"Ann"}');
        assert.deepEqual(h.getUserCalls, [2]);

        var priv = createHarness();
        priv.typing(5, 1);
        await settle();
        assert.equal(priv.getUserCalls.length, 0);
    });

    await test('resetTyping cancels the pending timers so the typing state does not outlive a chat switch', function () {
        var h = createHarness();
        h.typing(5, 1);
        h.BF.presence.resetTyping();
        assert.equal(h.pendingTimers(), 0);
        h.status(5, 'online', 1);
        assert.equal(h.header.textContent, 'status.online', 'no typing left to protect the header');
    });

    await test('collectUserIds subscribes to peers of private chats (not me, not bots, not groups) only when the set changes', function () {
        var h = createHarness({
            cache: { 7: { isBot: true } },
            chats: [
                { id: 1, members: [{ userId: 1 }, { userId: 5 }] },
                { id: 2, isGroupChat: true, members: [{ userId: 9 }] },
                { id: 3, members: [{ userId: 6 }, { userId: 1 }] },
                { id: 4 },
                { id: 5, members: [{ userId: 7 }] }
            ]
        });
        h.BF.presence.collectUserIds();
        assert.deepEqual(h.subscriptions, [[5, 6]]);
        h.BF.presence.collectUserIds();
        assert.equal(h.subscriptions.length, 1, 'unchanged set: no new subscription');

        h.state.chats.push({ id: 6, members: [{ userId: 8 }] });
        h.BF.presence.collectUserIds();
        assert.deepEqual(h.subscriptions[1], [5, 6, 8]);
        h.state.chats.pop();
        h.BF.presence.collectUserIds();
        assert.deepEqual(h.subscriptions[2], [5, 6], 'a peer that left the list is dropped');
        h.state.chats[2].members = [{ userId: 9 }, { userId: 1 }];
        h.BF.presence.collectUserIds();
        assert.deepEqual(
            h.subscriptions[3],
            [5, 9],
            'a replaced peer changes the subscription even when the size is the same'
        );
    });

    await test('subscribeFor adds only new non-bot users to the subscription', function () {
        var h = createHarness({ cache: { 7: { isBot: true } } });
        h.BF.presence.subscribeFor([5, 7]);
        assert.deepEqual(h.subscriptions, [[5]]);
        h.BF.presence.subscribeFor([5]);
        assert.equal(h.subscriptions.length, 1, 'already subscribed');
        h.BF.presence.subscribeFor([6]);
        assert.deepEqual(h.subscriptions[1], [5, 6]);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
