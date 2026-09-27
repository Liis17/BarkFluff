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
        disabled: false,
        onclick: null,
        scrollHeight: 0,
        scrollTop: 0,
        clientHeight: 0,
        dataset: {},
        listeners: {},
        statusEls: {},
        groupEls: {},
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
        addEventListener: function (type, callback) {
            this.listeners[type] = callback;
        },
        querySelector: function (selector) {
            var status = /\.msg-status\[data-msg-id="([^"]+)"\]/.exec(selector);
            if (status) return this.statusEls[status[1]] || null;
            var group = /\.msg-group\[data-msg-id="([^"]+)"\]/.exec(selector);
            return (group && this.groupEls[group[1]]) || null;
        }
    };
}

function createHarness(options) {
    options = options || {};
    var els = {};
    [
        'messagesArea',
        'messagesInner',
        'scrollToBottomBtn',
        'deleteMsgConfirmOverlay',
        'deleteMsgOk',
        'deleteMsgCancel'
    ].forEach(function (id) {
        if ((options.missing || []).indexOf(id) < 0) els['#' + id] = makeEl(id);
    });
    var handlers = {};
    var calls = [];
    var state = {
        chatId: options.chatId === undefined ? 'chat-a' : options.chatId,
        myUserId: 1,
        chats: options.chats || [],
        messages: options.messages || [],
        reconciled: options.reconciled || [],
        deletePromise: Promise.resolve()
    };
    var BF = {
        api: {
            deleteMessage: function (id) {
                calls.push(['api.delete', id]);
                return state.deletePromise;
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
            }
        },
        utils: {
            openOverlay: function (el) {
                calls.push(['openOverlay', el.id]);
            },
            closeOverlay: function (el) {
                calls.push(['closeOverlay', el.id]);
            }
        },
        sound: {
            play: function (name) {
                calls.push(['sound', name]);
            }
        },
        feed: {
            announceIncoming: function (msg) {
                calls.push(['announce', msg.id]);
            },
            incrementNewBelow: function () {
                calls.push(['newBelow']);
            },
            replaceElement: function (oldEl, newEl) {
                state.replacedWith = newEl;
                calls.push(['replaceElement', oldEl.id, newEl.built]);
            }
        },
        markRead: {
            markSoon: function (id) {
                calls.push(['markSoon', id]);
            }
        },
        messages: {
            updateMessageStatus: function (el, read) {
                calls.push(['status', el.id, read]);
            }
        },
        composer: {
            onMessageDeleted: function (id) {
                calls.push(['composerDeleted', id]);
            }
        }
    };
    if (options.pinned !== false) {
        BF.pinned = {
            applyPinnedEvent: function (data) {
                calls.push(['pinned', data.tag]);
            },
            applyUnpinnedEvent: function (data) {
                calls.push(['unpinned', data.tag]);
            },
            applyAllUnpinnedEvent: function (data) {
                calls.push(['allUnpinned', data.tag]);
            },
            applyMessageDeleted: function (id) {
                calls.push(['pinnedDeleted', id]);
            }
        };
    }
    var context = vm.createContext({
        BF: BF,
        console: { log: function () {} },
        document: {
            querySelector: function (selector) {
                return els[selector] || null;
            }
        },
        Promise: Promise,
        Number: Number
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/message-events.js'), 'utf8'), context);
    BF.messageEvents.init({
        getChats: function () {
            return state.chats;
        },
        getCurrentChatId: function () {
            return state.chatId;
        },
        getMyUserId: function () {
            return state.myUserId;
        },
        getMessages: function () {
            return state.messages;
        },
        reconcilePendingUpload: function (chatId, msg) {
            return state.reconciled.indexOf(msg.id) >= 0;
        },
        renderChatList: function () {
            calls.push(['renderChatList']);
        },
        loadChats: function (reset) {
            calls.push(['loadChats', reset]);
        },
        showNewMessageNotification: function (title, msg) {
            calls.push(['notify', title, msg.id]);
        },
        updateTitleBadge: function () {
            calls.push(['badge']);
        },
        appendMessageToView: function (msg) {
            calls.push(['append', msg.id]);
            return Promise.resolve();
        },
        scrollToBottom: function () {
            calls.push(['scrollToBottom']);
        },
        renderMessages: function () {
            calls.push(['renderMessages']);
        },
        buildMessageViewElement: function (msg) {
            calls.push(['build', msg.id]);
            return Promise.resolve({ built: msg.id, dataset: {} });
        },
        showToast: function (text, isError) {
            calls.push(['toast', text, !!isError]);
        }
    });
    var area = els['#messagesArea'];
    area.scrollHeight = 1000;
    area.clientHeight = 500;
    area.scrollTop = 500; // у нижнего края
    return {
        BF: BF,
        els: els,
        state: state,
        area: area,
        calls: function () {
            return plain(calls);
        },
        names: function () {
            return calls.map(function (call) {
                return call[0];
            });
        },
        has: function (name) {
            return calls.some(function (call) {
                return call[0] === name;
            });
        },
        clear: function () {
            calls.length = 0;
        },
        emit: function (event, data) {
            handlers[event](data);
        },
        scrolledUp: function (px) {
            area.scrollTop = 500 - px;
        },
        visible: function () {
            return els['#scrollToBottomBtn'].classList.contains('visible');
        }
    };
}

function chat(id, extra) {
    return Object.assign({ id: id, title: 'Title ' + id, countUnread: 0 }, extra || {});
}

function msg(id, senderId, extra) {
    return Object.assign({ id: id, senderId: senderId, content: { text: 't' + id } }, extra || {});
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('a message in another chat lifts the chat, counts it as unread, plays the chime and notifies', async function () {
        var h = createHarness({ chats: [chat('chat-a'), chat('chat-b', { countUnread: 2 }), chat('chat-c')] });
        h.emit('new_message', { chatId: 'chat-b', message: msg(10, 2) });
        await settle();
        assert.deepEqual(
            h.state.chats.map(function (c) {
                return c.id;
            }),
            ['chat-b', 'chat-a', 'chat-c']
        );
        assert.equal(h.state.chats[0].countUnread, 3);
        assert.equal(h.state.chats[0].lastMessage.id, 10);
        assert.deepEqual(h.calls(), [
            ['renderChatList'],
            ['sound', 'chime'],
            ['notify', 'Title chat-b', 10],
            ['badge']
        ]);
    });

    await test('own messages from another device do not add unread, sound or notifications', async function () {
        var h = createHarness({ chats: [chat('chat-a'), chat('chat-b')] });
        h.emit('new_message', { chatId: 'chat-b', message: msg(10, 1) });
        await settle();
        assert.equal(h.state.chats[0].id, 'chat-b');
        assert.equal(h.state.chats[0].countUnread, 0);
        assert.deepEqual(h.calls(), [['renderChatList'], ['badge']]);
    });

    await test('a message from an unknown chat reloads the chat list and notifies with an empty title', async function () {
        var h = createHarness({ chats: [chat('chat-a')] });
        h.emit('new_message', { chatId: 'chat-x', message: msg(10, 2) });
        await settle();
        assert.deepEqual(h.calls(), [['loadChats', true], ['sound', 'chime'], ['notify', '', 10], ['badge']]);
        assert.equal(h.state.chats.length, 1, 'the list is reloaded, nothing is invented locally');
    });

    await test('an incoming message at the bottom of the open chat is announced, appended, scrolled to and marked read', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(1, 2)] });
        h.emit('new_message', { chatId: 'chat-a', message: msg(10, 2) });
        await settle();
        assert.deepEqual(
            h.state.messages.map(function (m) {
                return m.id;
            }),
            [1, 10]
        );
        assert.equal(h.state.chats[0].countUnread, 0, 'the open chat does not accumulate unread');
        assert.deepEqual(h.calls(), [
            ['renderChatList'],
            ['sound', 'chime'],
            ['notify', 'Title chat-a', 10],
            ['badge'],
            ['announce', 10],
            ['append', 10],
            ['scrollToBottom'],
            ['markSoon', 10]
        ]);
        assert.equal(h.visible(), false);
    });

    await test('an own message at the bottom is appended and scrolled to without announce, chime or mark-read', async function () {
        var h = createHarness({ chats: [chat('chat-a')] });
        h.emit('new_message', { chatId: 'chat-a', message: msg(10, 1) });
        await settle();
        assert.deepEqual(h.calls(), [['renderChatList'], ['badge'], ['append', 10], ['scrollToBottom']]);
    });

    await test('scrolled up by 300px or more: the button shows, the counter grows, no scroll and no mark-read', async function () {
        var h = createHarness({ chats: [chat('chat-a')] });
        h.scrolledUp(300);
        h.emit('new_message', { chatId: 'chat-a', message: msg(10, 2) });
        await settle();
        assert.equal(h.visible(), true);
        assert.equal(h.has('newBelow'), true);
        assert.equal(h.has('scrollToBottom'), false);
        assert.equal(h.has('markSoon'), false);
        assert.equal(h.has('announce'), true, 'a screen reader still hears the message');
    });

    await test('299px from the bottom still counts as at the bottom', async function () {
        var h = createHarness({ chats: [chat('chat-a')] });
        h.scrolledUp(299);
        h.emit('new_message', { chatId: 'chat-a', message: msg(10, 2) });
        await settle();
        assert.equal(h.has('scrollToBottom'), true);
        assert.equal(h.has('markSoon'), true);
        assert.equal(h.has('newBelow'), false);
    });

    await test('the button is hidden again when the message arrives while the user is at the bottom', async function () {
        var h = createHarness({ chats: [chat('chat-a')] });
        h.els['#scrollToBottomBtn'].classList.add('visible');
        h.emit('new_message', { chatId: 'chat-a', message: msg(10, 2) });
        await settle();
        assert.equal(h.visible(), false);
    });

    await test('a message already present in the open chat is ignored entirely', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(10, 2)] });
        h.emit('new_message', { chatId: 'chat-a', message: msg(10, 2) });
        await settle();
        assert.deepEqual(h.calls(), []);
        assert.equal(h.state.messages.length, 1);
    });

    await test('the same id in another chat is not a duplicate of the open chat', async function () {
        var h = createHarness({ chats: [chat('chat-a'), chat('chat-b')], messages: [msg(10, 2)] });
        h.emit('new_message', { chatId: 'chat-b', message: msg(10, 2) });
        await settle();
        assert.equal(h.has('renderChatList'), true);
        assert.equal(h.state.chats[0].countUnread, 1);
    });

    await test('a message that reconciles a pending upload updates the list but is not appended a second time', async function () {
        var h = createHarness({ chats: [chat('chat-a'), chat('chat-b')], messages: [msg(10, 1)], reconciled: [10] });
        h.emit('new_message', { chatId: 'chat-a', message: msg(10, 1) });
        await settle();
        assert.equal(h.state.messages.length, 1);
        assert.deepEqual(h.calls(), [['renderChatList'], ['badge']]);
        assert.equal(h.state.chats[0].lastMessage.id, 10);
    });

    await test('read events update the ticks and readBy of the open chat, only counting other readers as read', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(5, 1), msg(6, 1)] });
        var el = makeEl('status-5');
        h.area.statusEls['5'] = el;
        h.emit('message_read', { chatId: 'chat-a', messageId: 5, readBy: [1, 2] });
        assert.deepEqual(h.state.messages[0].readBy, [1, 2]);
        assert.deepEqual(h.calls().slice(0, 1), [['status', 'status-5', true]]);
        h.clear();
        h.emit('message_read', { chatId: 'chat-a', messageId: 5, readBy: [1] });
        assert.deepEqual(h.calls().slice(0, 1), [['status', 'status-5', false]], 'only me: still a single tick');
    });

    await test('read events without a tick element or a loaded message do not fail', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(5, 1)] });
        h.emit('message_read', { chatId: 'chat-a', messageId: 5, readBy: [2] });
        assert.deepEqual(h.state.messages[0].readBy, [2]);
        assert.equal(h.has('status'), false);
        h.emit('message_read', { chatId: 'chat-a', messageId: 99, readBy: [2] });
        assert.equal(h.has('status'), false);
    });

    await test('a read event of another chat never touches the messages of the open chat', async function () {
        var h = createHarness({ chats: [chat('chat-a'), chat('chat-b')], messages: [msg(5, 1)] });
        h.emit('message_read', { chatId: 'chat-b', messageId: 5, readBy: [2] });
        assert.equal(h.state.messages[0].readBy, undefined);
    });

    await test('reading in the open chat zeroes its unread counter; in another chat it decrements down to zero', async function () {
        var h = createHarness({ chats: [chat('chat-a', { countUnread: 4 }), chat('chat-b', { countUnread: 2 })] });
        h.emit('message_read', { chatId: 'chat-a', messageId: 1, readBy: [1] });
        assert.equal(h.state.chats[0].countUnread, 0);
        h.emit('message_read', { chatId: 'chat-b', messageId: 1, readBy: [1] });
        assert.equal(h.state.chats[1].countUnread, 1);
        h.emit('message_read', { chatId: 'chat-b', messageId: 2, readBy: [1] });
        h.emit('message_read', { chatId: 'chat-b', messageId: 3, readBy: [1] });
        assert.equal(h.state.chats[1].countUnread, 0, 'never below zero');
    });

    await test('somebody else reading keeps my counter but still redraws the list and the title badge', async function () {
        var h = createHarness({ chats: [chat('chat-b', { countUnread: 2 })] });
        h.emit('message_read', { chatId: 'chat-b', messageId: 1, readBy: [2] });
        assert.equal(h.state.chats[0].countUnread, 2);
        assert.deepEqual(h.calls(), [['renderChatList'], ['badge']]);
    });

    await test('a read event of an unknown chat redraws nothing', async function () {
        var h = createHarness({ chats: [chat('chat-a')] });
        h.emit('message_read', { chatId: 'chat-x', messageId: 1, readBy: [1] });
        assert.deepEqual(h.calls(), []);
    });

    await test('an edit replaces the message and its element, keeping the date attribute', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(5, 2), msg(6, 2)] });
        var oldEl = makeEl('old-6');
        oldEl.dataset.date = '2026-09-27';
        h.els['#messagesInner'].groupEls['6'] = oldEl;
        var edited = msg(6, 2, { editedAt: 1 });
        h.emit('message_edited', { chatId: 'chat-a', message: edited });
        await settle();
        assert.equal(h.state.messages[1], edited);
        assert.deepEqual(h.calls(), [
            ['build', 6],
            ['replaceElement', 'old-6', 6]
        ]);
        assert.equal(h.state.replacedWith.dataset.date, '2026-09-27', 'the day separator logic still finds the group');
    });

    await test('an edit event of another chat does not touch a message with the same id in the open chat', async function () {
        var h = createHarness({ chats: [chat('chat-a'), chat('chat-b')], messages: [msg(6, 2)] });
        h.els['#messagesInner'].groupEls['6'] = makeEl('old-6');
        var original = h.state.messages[0];
        h.emit('message_edited', { chatId: 'chat-b', message: msg(6, 2, { editedAt: 1 }) });
        await settle();
        assert.equal(h.state.messages[0], original);
        assert.deepEqual(h.calls(), []);
    });

    await test('an edit of the last message of a chat updates the chat list, also when the chat is not open', async function () {
        var h = createHarness({ chats: [chat('chat-b', { lastMessage: msg(6, 2) }), chat('chat-c', { lastMessage: msg(7, 2) })] });
        var edited = msg(6, 2, { editedAt: 1 });
        h.emit('message_edited', { chatId: 'chat-b', message: edited });
        await settle();
        assert.equal(h.state.chats[0].lastMessage, edited);
        assert.deepEqual(h.calls(), [['renderChatList']], 'no view work for a chat that is not open');
    });

    await test('an edit that is not the last message leaves the chat list alone', async function () {
        var h = createHarness({ chats: [chat('chat-b', { lastMessage: msg(7, 2) })] });
        h.emit('message_edited', { chatId: 'chat-b', message: msg(6, 2) });
        assert.deepEqual(h.calls(), []);
    });

    await test('an edit of a message that is not loaded, or has no element, or is empty, does nothing', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(5, 2)] });
        h.emit('message_edited', { chatId: 'chat-a', message: msg(99, 2) });
        h.emit('message_edited', { chatId: 'chat-a', message: msg(5, 2, { editedAt: 1 }) });
        h.emit('message_edited', { chatId: 'chat-a', message: null });
        await settle();
        assert.deepEqual(h.calls(), [], 'no element in the DOM for message 5, so nothing is rebuilt');
        assert.equal(h.state.messages[0].editedAt, 1, 'but the loaded message is updated');
    });

    await test('a delete removes the message, redraws the feed, tells the composer and the pinned panel', async function () {
        var h = createHarness({ chats: [chat('chat-a', { lastMessage: msg(9, 2) })], messages: [msg(5, 2), msg(6, 2)] });
        h.emit('message_deleted', { chatId: 'chat-a', messageId: '5' });
        assert.deepEqual(
            h.state.messages.map(function (m) {
                return m.id;
            }),
            [6],
            'the id is compared as a number, whatever its type'
        );
        assert.deepEqual(h.calls(), [['composerDeleted', 5], ['renderMessages'], ['pinnedDeleted', 5]]);
    });

    await test('a delete of a message that is not loaded skips the redraw but still reaches the composer and pinned', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(5, 2)] });
        h.emit('message_deleted', { chatId: 'chat-a', messageId: 99 });
        assert.equal(h.state.messages.length, 1);
        assert.deepEqual(h.calls(), [['composerDeleted', 99], ['pinnedDeleted', 99]]);
    });

    await test('a delete found in any chat is applied regardless of the chat id of the event', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(5, 2)] });
        h.emit('message_deleted', { chatId: 'CHAT-A', messageId: 5 });
        assert.equal(h.state.messages.length, 0);
        assert.equal(h.has('renderMessages'), true);
    });

    await test('deleting the last message of a chat reloads the chat list once', async function () {
        var h = createHarness({
            chats: [chat('chat-a', { lastMessage: msg(5, 2) }), chat('chat-b', { lastMessage: msg(5, 2) }), chat('chat-c')],
            messages: [msg(5, 2)]
        });
        h.emit('message_deleted', { chatId: 'chat-a', messageId: '5' });
        assert.equal(
            h.calls().filter(function (call) {
                return call[0] === 'loadChats';
            }).length,
            1
        );
        assert.deepEqual(h.calls().find((call) => call[0] === 'loadChats'), ['loadChats', true]);
    });

    await test('a delete event without an id does nothing', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(5, 2)] });
        h.emit('message_deleted', { chatId: 'chat-a', messageId: null });
        h.emit('message_deleted', { chatId: 'chat-a' });
        assert.deepEqual(h.calls(), []);
        assert.equal(h.state.messages.length, 1);
    });

    await test('applyEdit and applyDelete are exported for the send pipeline and the resync', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(5, 2)] });
        h.BF.messageEvents.applyDelete('chat-a', 5);
        assert.equal(h.state.messages.length, 0);
        var h2 = createHarness({ chats: [chat('chat-a', { lastMessage: msg(5, 2) })] });
        h2.BF.messageEvents.applyEdit('chat-a', msg(5, 2, { editedAt: 1 }));
        assert.equal(h2.state.chats[0].lastMessage.editedAt, 1);
    });

    await test('pin events are forwarded to the pinned panel, and are safe when it is absent', async function () {
        var h = createHarness();
        h.emit('message_pinned', { tag: 'p' });
        h.emit('message_unpinned', { tag: 'u' });
        h.emit('all_messages_unpinned', { tag: 'a' });
        assert.deepEqual(h.calls(), [['pinned', 'p'], ['unpinned', 'u'], ['allUnpinned', 'a']]);
        var h2 = createHarness({ pinned: false });
        h2.emit('message_pinned', { tag: 'p' });
        h2.emit('message_deleted', { chatId: 'chat-a', messageId: 1 });
        assert.deepEqual(h2.calls(), [['composerDeleted', 1]]);
    });

    await test('the delete dialog: confirm deletes through the API, applies it to the open chat and closes the dialog', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(5, 1), msg(6, 1)] });
        var release;
        h.state.deletePromise = new Promise(function (resolve) {
            release = resolve;
        });
        h.BF.messageEvents.requestDelete(5);
        assert.deepEqual(h.calls(), [['openOverlay', 'deleteMsgConfirmOverlay']]);
        var ok = h.els['#deleteMsgOk'];
        assert.equal(typeof ok.onclick, 'function');
        ok.onclick();
        assert.equal(ok.disabled, true, 'the button is locked while the request is running');
        assert.equal(h.state.messages.length, 2);
        release();
        await settle();
        assert.equal(h.state.messages.length, 1);
        assert.equal(ok.disabled, false);
        assert.equal(ok.onclick, null);
        assert.deepEqual(h.calls().slice(-1), [['closeOverlay', 'deleteMsgConfirmOverlay']]);
        assert.equal(h.has('toast'), false);
    });

    await test('the delete dialog: a failed request shows an error toast, keeps the message and closes the dialog', async function () {
        var h = createHarness({ chats: [chat('chat-a')], messages: [msg(5, 1)] });
        h.state.deletePromise = Promise.reject(new Error('nope'));
        h.BF.messageEvents.requestDelete(5);
        var ok = h.els['#deleteMsgOk'];
        ok.onclick();
        await settle();
        assert.equal(h.state.messages.length, 1);
        assert.deepEqual(
            h.calls().filter(function (call) {
                return call[0] === 'toast';
            }),
            [['toast', 'error.deleteMessage', true]]
        );
        assert.equal(ok.disabled, false);
        assert.equal(ok.onclick, null);
        assert.equal(h.has('closeOverlay'), true);
    });

    await test('the delete dialog: switching chats while the request runs still applies the delete by its global id', async function () {
        var h = createHarness({ chats: [chat('chat-a', { lastMessage: msg(5, 1) })], messages: [msg(5, 1)] });
        var release;
        h.state.deletePromise = new Promise(function (resolve) {
            release = resolve;
        });
        h.BF.messageEvents.requestDelete(5);
        h.els['#deleteMsgOk'].onclick();
        h.state.chatId = 'chat-b';
        h.state.messages = [];
        release();
        await settle();
        assert.equal(h.has('composerDeleted'), true, 'the event is still applied: ids are global');
        assert.equal(h.has('renderMessages'), false, 'nothing to remove from the switched chat');
        assert.equal(h.has('loadChats'), true, 'the last message of a chat was deleted');
    });

    await test('the delete dialog: without the overlay or without an id nothing opens', async function () {
        var h = createHarness({ missing: ['deleteMsgConfirmOverlay'] });
        h.BF.messageEvents.requestDelete(5);
        assert.deepEqual(h.calls(), []);
        var h2 = createHarness();
        h2.BF.messageEvents.requestDelete(0);
        h2.BF.messageEvents.requestDelete(undefined);
        assert.deepEqual(h2.calls(), []);
    });

    await test('the delete dialog: cancel and a click on the backdrop close it and drop the pending confirm handler', async function () {
        var h = createHarness();
        h.BF.messageEvents.requestDelete(5);
        h.clear();
        h.els['#deleteMsgCancel'].listeners.click();
        assert.deepEqual(h.calls(), [['closeOverlay', 'deleteMsgConfirmOverlay']]);
        assert.equal(h.els['#deleteMsgOk'].onclick, null);

        h.BF.messageEvents.requestDelete(5);
        h.clear();
        var overlay = h.els['#deleteMsgConfirmOverlay'];
        overlay.listeners.click({ target: {} });
        assert.deepEqual(h.calls(), [], 'a click inside the dialog does not close it');
        assert.equal(typeof h.els['#deleteMsgOk'].onclick, 'function');
        overlay.listeners.click({ target: overlay });
        assert.deepEqual(h.calls(), [['closeOverlay', 'deleteMsgConfirmOverlay']]);
        assert.equal(h.els['#deleteMsgOk'].onclick, null);
    });

    await test('init tolerates a page without the delete dialog', async function () {
        var h = createHarness({ missing: ['deleteMsgConfirmOverlay', 'deleteMsgOk', 'deleteMsgCancel'] });
        h.emit('message_deleted', { chatId: 'chat-a', messageId: 1 });
        assert.equal(h.has('composerDeleted'), true);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
