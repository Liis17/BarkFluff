const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 20; i++) await new Promise((resolve) => setImmediate(resolve));
}

function makeEl(id) {
    var names = new Set();
    return {
        id: id,
        style: {},
        dataset: {},
        innerHTML: '',
        textContent: '',
        parentElement: { classList: { add: function () {}, remove: function () {} } },
        classList: {
            add: function (name) {
                names.add(name);
            },
            remove: function (name) {
                names.delete(name);
            },
            contains: function (name) {
                return names.has(name);
            }
        },
        addEventListener: function () {},
        appendChild: function () {}
    };
}

// --- private-chat-ui.js: обработчики событий приватных чатов ---

function createUiHarness(options) {
    options = options || {};
    var handlers = {};
    var calls = [];
    var state = {
        chatId: options.chatId === undefined ? 'p1' : options.chatId,
        myUserId: 1,
        chats: options.chats || [],
        messages: options.messages || []
    };
    var BF = {
        i18n: {
            t: function (key) {
                return key;
            }
        },
        realtime: {
            on: function (event, callback) {
                handlers[event] = callback;
            },
            unsubscribeTyping: function () {}
        },
        privateChat: {
            hasKey: function () {
                return true;
            },
            decryptMessage: function (chatId, message) {
                return Promise.resolve('plain-' + message.id);
            }
        },
        api: {
            listPrivateMessages: function (chatId) {
                calls.push(['listPrivateMessages', chatId]);
                return Promise.resolve({ messages: [] });
            },
            markPrivateMessagesAsRead: function () {
                return Promise.resolve();
            }
        },
        utils: { openOverlay: function () {}, closeOverlay: function () {} },
        sound: { play: function () {} }
    };
    var els = {};
    var context = vm.createContext({
        BF: BF,
        console: console,
        document: {
            querySelector: function (selector) {
                if (!els[selector]) els[selector] = makeEl(selector);
                return els[selector];
            },
            createElement: function (tag) {
                return makeEl(tag);
            }
        },
        Promise: Promise
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/private-chat-ui.js'), 'utf8'), context);
    var overrides = {
        getChats: function () {
            return state.chats;
        },
        getCurrentChatId: function () {
            return state.chatId;
        },
        setCurrentChatId: function (id) {
            state.chatId = id;
        },
        getMyUserId: function () {
            return state.myUserId;
        },
        getMessages: function () {
            return state.messages;
        },
        setMessages: function (messages) {
            state.messages = messages;
        },
        getUser: function () {
            return Promise.resolve(null);
        },
        renderMessages: function () {
            calls.push(['renderMessages']);
            return Promise.resolve();
        }
    };
    // Остальные зависимости только записывают вызов.
    var deps = new Proxy(overrides, {
        get: function (target, name) {
            if (name in target) return target[name];
            return function (arg) {
                calls.push([name, arg]);
            };
        }
    });
    BF.privateChatUI.init(deps);
    return {
        state: state,
        emit: function (event, data) {
            handlers[event](data);
        },
        names: function () {
            return calls.map(function (call) {
                return call[0];
            });
        },
        calls: function () {
            return calls.slice();
        },
        clear: function () {
            calls.length = 0;
        }
    };
}

function uiMessage(id) {
    return { id: id, senderId: 2, content: { text: 'old-' + id } };
}

function encrypted(id, extra) {
    return Object.assign(
        { id: id, senderId: 2, sentAt: 1, isEdited: true, editedAt: 2, isDeleted: false },
        extra || {}
    );
}

// --- realtime.js: подписки на приватные потоки ---

function createRealtimeHarness() {
    var opened = [];
    var emitted = [];
    function fakeStream(method) {
        var listeners = {};
        var stream = {
            method: method,
            cancelled: false,
            on: function (event, cb) {
                listeners[event] = cb;
                return stream;
            },
            cancel: function () {
                stream.cancelled = true;
            },
            fire: function (event, payload) {
                listeners[event](payload);
            }
        };
        return stream;
    }
    var updates = new Proxy(
        {},
        {
            get: function (target, method) {
                return function (req) {
                    var stream = fakeStream(method);
                    stream.request = req.kind;
                    opened.push(stream);
                    return stream;
                };
            }
        }
    );
    var protoUpdates = new Proxy(
        {},
        {
            get: function (target, kind) {
                return function () {
                    this.kind = kind;
                };
            }
        }
    );
    var BF = {
        clients: {
            updates: updates,
            onliner: updates,
            getValidToken: function () {
                return Promise.resolve('token');
            },
            refreshToken: function () {
                return Promise.resolve('token');
            }
        },
        metadata: {
            build: function () {
                return {};
            }
        },
        api: {
            setOnlineStatus: function () {
                return Promise.resolve();
            },
            _mapEncryptedMessage: function (m) {
                return { id: m.id };
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        console: console,
        document: { addEventListener: function () {}, visibilityState: 'visible' },
        navigator: { onLine: true },
        proto: { barkfluff: { updates: protoUpdates } },
        setTimeout: setTimeout,
        clearTimeout: clearTimeout,
        setInterval: setInterval,
        clearInterval: clearInterval,
        Promise: Promise,
        Date: Date,
        Math: Math,
        Object: Object
    });
    context.window = context;
    context.addEventListener = function () {};
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/realtime.js'), 'utf8'), context);
    [
        'private_message_edited',
        'private_message_deleted',
        'private_messages_read',
        'private_chat_invite',
        'private_chat_invite_resolution',
        'resync'
    ].forEach(function (event) {
        BF.realtime.on(event, function (data) {
            emitted.push([event, data]);
        });
    });
    return {
        BF: BF,
        opened: opened,
        emitted: emitted,
        stream: function (method) {
            return opened.filter(function (s) {
                return s.method === method;
            });
        }
    };
}

function event(fields) {
    var evt = {};
    Object.keys(fields).forEach(function (key) {
        evt['get' + key] = function () {
            return fields[key];
        };
    });
    return evt;
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('an edit replaces the decrypted message in the open private chat and re-renders', async function () {
        var h = createUiHarness({ chats: [{ id: 'p1' }], messages: [uiMessage(5), uiMessage(6)] });
        h.emit('private_message_edited', { chatId: 'p1', message: encrypted(5) });
        await settle();
        assert.equal(h.state.messages[0].content.text, 'plain-5');
        assert.equal(h.state.messages[0].isEdited, true);
        assert.equal(h.state.messages[1].content.text, 'old-6');
        assert.deepEqual(h.names(), ['renderMessages']);
    });

    await test('an edit for a closed chat or an unloaded message changes nothing', async function () {
        var h = createUiHarness({ chats: [{ id: 'p1' }], messages: [uiMessage(5)] });
        h.emit('private_message_edited', { chatId: 'p2', message: encrypted(5) });
        h.emit('private_message_edited', { chatId: 'p1', message: encrypted(99) });
        await settle();
        assert.equal(h.state.messages[0].content.text, 'old-5');
        assert.deepEqual(h.names(), []);
    });

    await test('an edit is dropped when the chat is switched while decrypting', async function () {
        var h = createUiHarness({ chats: [{ id: 'p1' }], messages: [uiMessage(5)] });
        h.emit('private_message_edited', { chatId: 'p1', message: encrypted(5) });
        h.state.chatId = 'other';
        await settle();
        assert.equal(h.state.messages[0].content.text, 'old-5');
        assert.deepEqual(h.names(), []);
    });

    await test('a delete removes the message only from the open private chat; a repeat is a no-op', async function () {
        var h = createUiHarness({ chats: [{ id: 'p1' }], messages: [uiMessage(5), uiMessage(6)] });
        h.emit('private_message_deleted', { chatId: 'p1', messageId: 5 });
        h.emit('private_message_deleted', { chatId: 'p1', messageId: 5 });
        h.emit('private_message_deleted', { chatId: 'p2', messageId: 6 });
        assert.deepEqual(
            h.state.messages.map(function (m) {
                return m.id;
            }),
            [6]
        );
        assert.deepEqual(h.names(), ['renderMessages']);
    });

    await test('an edit event carrying isDeleted removes the message', async function () {
        var h = createUiHarness({ chats: [{ id: 'p1' }], messages: [uiMessage(5)] });
        h.emit('private_message_edited', { chatId: 'p1', message: encrypted(5, { isDeleted: true }) });
        await settle();
        assert.deepEqual(h.state.messages, []);
    });

    await test('my read on another device clears the unread counter; a peer read is ignored', async function () {
        var chats = [
            { id: 'p1', countUnread: 0 },
            { id: 'p2', countUnread: 3 }
        ];
        var h = createUiHarness({ chats: chats });
        h.emit('private_messages_read', { chatId: 'p2', userId: 2, lastReadMessageId: 9 });
        assert.equal(chats[1].countUnread, 3);
        assert.deepEqual(h.names(), []);
        h.emit('private_messages_read', { chatId: 'p2', userId: 1, lastReadMessageId: 9 });
        assert.equal(chats[1].countUnread, 0);
        assert.deepEqual(h.names(), ['renderChatList', 'updateTitleBadge']);
        h.clear();
        h.emit('private_messages_read', { chatId: 'p2', userId: 1, lastReadMessageId: 9 });
        assert.deepEqual(h.names(), [], 'a repeated read does not re-render');
    });

    await test('an invite reloads the chat list', async function () {
        var h = createUiHarness();
        h.emit('private_chat_invite', { chatId: 'p9', inviterUserId: 2 });
        assert.deepEqual(h.calls(), [['loadChats', true]]);
    });

    await test('a resolution for a background chat updates its state once', async function () {
        var chats = [{ id: 'p2', privateInviteState: 0 }];
        var h = createUiHarness({ chatId: 'p1', chats: chats });
        h.emit('private_chat_invite_resolution', { chatId: 'p2', accepted: false });
        h.emit('private_chat_invite_resolution', { chatId: 'p2', accepted: false });
        assert.equal(chats[0].privateInviteState, 2);
        assert.deepEqual(h.names(), ['renderChatList']);
    });

    await test('an accepted resolution for the open chat re-opens it and loads messages', async function () {
        var chats = [{ id: 'p1', privateInviteState: 0, privateInviterUserId: 1, members: [] }];
        var h = createUiHarness({ chatId: 'p1', chats: chats });
        h.emit('private_chat_invite_resolution', { chatId: 'p1', accepted: true });
        await settle();
        assert.equal(chats[0].privateInviteState, 1);
        assert.ok(h.names().indexOf('listPrivateMessages') >= 0);
    });

    await test('a resolution for an unknown chat reloads the chat list', async function () {
        var h = createUiHarness({ chats: [] });
        h.emit('private_chat_invite_resolution', { chatId: 'p7', accepted: true });
        assert.deepEqual(h.calls(), [['loadChats', true]]);
    });

    await test('realtime opens the five private streams, maps their events and cancels them on stop', async function () {
        var h = createRealtimeHarness();
        h.BF.realtime.startAll();
        await settle();
        var expected = {
            subscribePrivateMessageEdits: 'SubscribePrivateMessageEditsRequest',
            subscribePrivateMessageDeletes: 'SubscribePrivateMessageDeletesRequest',
            subscribePrivateMessagesRead: 'SubscribePrivateMessagesReadRequest',
            subscribePrivateChatInvites: 'SubscribePrivateChatInvitesRequest',
            subscribePrivateChatInviteResolutions: 'SubscribePrivateChatInviteResolutionsRequest'
        };
        Object.keys(expected).forEach(function (method) {
            var streams = h.stream(method);
            assert.equal(streams.length, 1, method);
            assert.equal(streams[0].request, expected[method]);
        });

        h.stream('subscribePrivateMessageEdits')[0].fire('data', event({ ChatId: 'p1', Message: { id: 5 } }));
        h.stream('subscribePrivateMessageDeletes')[0].fire('data', event({ ChatId: 'p1', MessageId: 6 }));
        h.stream('subscribePrivateMessagesRead')[0].fire(
            'data',
            event({ ChatId: 'p1', UserId: 1, LastReadMessageId: 7 })
        );
        h.stream('subscribePrivateChatInvites')[0].fire('data', event({ ChatId: 'p2', InviterUserId: 3 }));
        h.stream('subscribePrivateChatInviteResolutions')[0].fire(
            'data',
            event({ ChatId: 'p3', InviteeUserId: 4, Accepted: true })
        );
        assert.deepEqual(JSON.parse(JSON.stringify(h.emitted)), [
            ['private_message_edited', { chatId: 'p1', message: { id: 5 } }],
            ['private_message_deleted', { chatId: 'p1', messageId: 6 }],
            ['private_messages_read', { chatId: 'p1', userId: 1, lastReadMessageId: 7 }],
            ['private_chat_invite', { chatId: 'p2', inviterUserId: 3 }],
            ['private_chat_invite_resolution', { chatId: 'p3', inviteeUserId: 4, accepted: true }]
        ]);

        h.BF.realtime.stopAll();
        Object.keys(expected).forEach(function (method) {
            assert.equal(h.stream(method)[0].cancelled, true, method);
        });
    });

    await test('a reopened private stream asks the UI to resync', async function () {
        var h = createRealtimeHarness();
        h.BF.realtime.startAll();
        await settle();
        h.stream('subscribePrivateChatInvites')[0].fire('end');
        await new Promise((resolve) => setTimeout(resolve, 2100));
        await settle();
        assert.equal(h.stream('subscribePrivateChatInvites').length, 2);
        assert.ok(
            h.emitted.some(function (e) {
                return e[0] === 'resync' && e[1].source === 'privateInvites';
            })
        );
        h.BF.realtime.stopAll();
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
