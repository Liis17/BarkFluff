const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

var stats = { innerHTMLWrites: 0 };

class FakeElement {
    constructor(tagName, ownerDocument) {
        this.tagName = tagName;
        this.ownerDocument = ownerDocument || null;
        this.parentNode = null;
        this.children = [];
        this.listeners = {};
        this.attributes = {};
        this.dataset = {};
        this.style = {};
        this.tabIndex = -1;
        this.className = '';
        this.textContent = '';
        this.disabled = false;
        this.checked = false;
        var self = this;
        this.classList = {
            names: function () {
                return self.className.split(/\s+/).filter(Boolean);
            },
            contains: function (name) {
                return this.names().indexOf(name) >= 0;
            },
            add: function (name) {
                if (!this.contains(name)) self.className = this.names().concat(name).join(' ');
            },
            remove: function (name) {
                self.className = this.names()
                    .filter(function (n) {
                        return n !== name;
                    })
                    .join(' ');
            },
            toggle: function (name, force) {
                var add = force === undefined ? !this.contains(name) : force;
                if (add) this.add(name);
                else this.remove(name);
                return add;
            }
        };
    }

    set innerHTML(value) {
        stats.innerHTMLWrites++;
        this.children = [];
        this._innerHTML = value;
    }

    get innerHTML() {
        return this._innerHTML || '';
    }

    get firstChild() {
        return this.children[0] || null;
    }

    get nextSibling() {
        if (!this.parentNode) return null;
        var siblings = this.parentNode.children;
        return siblings[siblings.indexOf(this) + 1] || null;
    }

    setAttribute(name, value) {
        this.attributes[name] = String(value);
    }

    getAttribute(name) {
        return Object.prototype.hasOwnProperty.call(this.attributes, name) ? this.attributes[name] : null;
    }

    removeAttribute(name) {
        delete this.attributes[name];
    }

    appendChild(child) {
        return this.insertBefore(child, null);
    }

    insertBefore(child, ref) {
        if (child.parentNode) child.remove();
        child.parentNode = this;
        var index = ref ? this.children.indexOf(ref) : -1;
        if (index < 0) this.children.push(child);
        else this.children.splice(index, 0, child);
        return child;
    }

    remove() {
        if (!this.parentNode) return;
        var doc = this.ownerDocument;
        if (doc && this.contains(doc.activeElement)) doc.activeElement = doc.body;
        this.parentNode.children.splice(this.parentNode.children.indexOf(this), 1);
        this.parentNode = null;
    }

    contains(node) {
        for (var cur = node; cur; cur = cur.parentNode) if (cur === this) return true;
        return false;
    }

    closest(selector) {
        for (var cur = this; cur; cur = cur.parentNode) {
            if (selector === '.chat-item' && cur.classList && cur.classList.contains('chat-item')) return cur;
            if (selector === 'button[data-act]' && cur.tagName === 'button' && cur.dataset.act) return cur;
        }
        return null;
    }

    getBoundingClientRect() {
        return { left: 0, right: 100, top: 0, bottom: 20, width: 100, height: 20 };
    }

    querySelector() {
        return null;
    }

    addEventListener(type, callback) {
        (this.listeners[type] = this.listeners[type] || []).push(callback);
    }

    dispatchEvent(event) {
        if (!event.target) event.target = this;
        (this.listeners[event.type] || []).forEach(function (callback) {
            callback(event);
        });
        if (!event.cancelBubble && this.parentNode) this.parentNode.dispatchEvent(event);
    }

    focus() {
        if (!this.ownerDocument) return;
        this.ownerDocument.activeElement = this;
        this.dispatchEvent(makeEvent('focusin'));
    }
}

function makeEvent(type, extra) {
    var event = {
        type: type,
        cancelBubble: false,
        defaultPrevented: false,
        stopPropagation: function () {
            this.cancelBubble = true;
        },
        preventDefault: function () {
            this.defaultPrevented = true;
        }
    };
    return Object.assign(event, extra || {});
}

function createHarness(chatCount) {
    var document = new FakeElement('#document');
    document.ownerDocument = document;
    document.body = new FakeElement('body', document);
    document.activeElement = document.body;
    document.createElement = function (tagName) {
        return new FakeElement(tagName, document);
    };
    var chatListEl = new FakeElement('div', document);
    chatListEl.clientHeight = 300;
    chatListEl.scrollTop = 0;
    chatListEl.scrollHeight = 1e9; // пагинация в этих сценариях не нужна
    var chatContextMenu = new FakeElement('div', document);
    var deleteChatOverlay = new FakeElement('div', document);
    var deleteChatTitle = new FakeElement('h3', document);
    var deleteChatText = new FakeElement('p', document);
    var deleteChatForEveryoneRow = new FakeElement('label', document);
    var deleteChatForEveryoneLabel = new FakeElement('span', document);
    var deleteChatForEveryoneCheckbox = new FakeElement('input', document);
    var deleteChatOk = new FakeElement('button', document);
    var deleteChatCancel = new FakeElement('button', document);
    document.body.appendChild(chatListEl);
    document.querySelector = function (selector) {
        return {
            '#chatList': chatListEl,
            '#chatContextMenu': chatContextMenu,
            '#deleteChatConfirmOverlay': deleteChatOverlay,
            '#deleteChatTitle': deleteChatTitle,
            '#deleteChatText': deleteChatText,
            '#deleteChatForEveryoneRow': deleteChatForEveryoneRow,
            '#deleteChatForEveryoneLabel': deleteChatForEveryoneLabel,
            '#deleteChatForEveryone': deleteChatForEveryoneCheckbox,
            '#deleteChatOk': deleteChatOk,
            '#deleteChatCancel': deleteChatCancel
        }[selector] || null;
    };

    var chats = [];
    for (var i = 0; i < chatCount; i++) {
        chats.push({ id: 1000 + i, title: 'Chat ' + i, chatType: 0, lastMessage: null, countUnread: 0 });
    }
    var opened = [];
    var mobileShown = 0;
    var closedChatCount = 0;
    var listChatsCalls = 0;
    var listChatsHandler = function () {
        return Promise.resolve({ chats: [], totalCount: 0 });
    };
    var realtimeListeners = {};
    var resolveDelete;
    var rejectDelete;
    var toastCalls = [];
    var frames = [];
    var BF = {
        api: {
            listChats: function () {
                listChatsCalls++;
                return listChatsHandler.apply(null, arguments);
            },
            deleteChat: function () {
                return new Promise(function (resolve, reject) {
                    resolveDelete = resolve;
                    rejectDelete = reject;
                });
            },
            leaveChat: function () {
                return Promise.resolve();
            }
        },
        folders: {
            renderTabs: function () {},
            getFoldersWithoutChat: function () { return []; },
            getFoldersForChat: function () { return []; },
            filterChats: function (list) {
                return list;
            }
        },
        drafts: {
            has: function () {
                return false;
            }
        },
        i18n: {
            t: function (key) {
                return key;
            },
            tp: function (key, count) {
                return key + ':' + count;
            }
        },
        icons: {
            html: function () {
                return '';
            }
        },
        utils: {
            openOverlay: function (overlay) { overlay.classList.add('visible'); },
            closeOverlay: function (overlay) { overlay.classList.remove('visible'); },
            escapeHtml: function (value) {
                return String(value);
            },
            markdownToPlainText: function (value) {
                return value;
            },
            truncate: function (value) {
                return value;
            },
            formatChatListTime: function () {
                return '12:00';
            }
        },
        realtime: {
            on: function (name, callback) { realtimeListeners[name] = callback; }
        }
    };
    var state = { currentChatId: null };
    var context = vm.createContext({
        BF: BF,
        document: document,
        getComputedStyle: function () {
            return {
                paddingBottom: '0px',
                getPropertyValue: function () {
                    return ' 75px';
                }
            };
        },
        requestAnimationFrame: function (callback) {
            frames.push(callback);
        },
        addEventListener: function () {},
        __mobileShowChat: function () {
            mobileShown++;
        },
        innerWidth: 1024,
        innerHeight: 768
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/chat-list.js'), 'utf8'), context);
    BF.chatList.init({
        getChats: function () {
            return chats;
        },
        setChats: function (value) {
            chats = value;
        },
        getCurrentChatId: function () {
            return state.currentChatId;
        },
        getMyUserId: function () {
            return 1;
        },
        getCachedUser: function () {
            return null;
        },
        getUser: function () {
            return Promise.resolve(null);
        },
        isUserOnline: function () {
            return false;
        },
        collectOnlineUserIds: function () {},
        updateTitleBadge: function () {},
        showToast: function (message, isError) { toastCalls.push([message, isError]); },
        closeCurrentChat: function () {
            closedChatCount++;
            state.currentChatId = null;
        },
        openChat: function (chatId) {
            opened.push(chatId);
        },
        botBadgeMarkup: function () {
            return '';
        }
    });

    var spacer = chatListEl.children[0];
    return {
        BF: BF,
        state: state,
        document: document,
        list: chatListEl,
        spacer: spacer,
        opened: opened,
        mobileShown: function () {
            return mobileShown;
        },
        closedChatCount: function () { return closedChatCount; },
        listChatsCalls: function () { return listChatsCalls; },
        setListChats: function (handler) {
            listChatsHandler = handler;
        },
        emitRealtime: function (name, data) { realtimeListeners[name](data); },
        contextMenu: chatContextMenu,
        deleteChatOk: deleteChatOk,
        deleteChatCancel: deleteChatCancel,
        deleteChatOverlay: deleteChatOverlay,
        resolveDelete: function () { return resolveDelete; },
        rejectDelete: function () { return rejectDelete; },
        toastCalls: toastCalls,
        chats: function () {
            return chats;
        },
        setChats: function (value) {
            chats = value;
        },
        rowIds: function () {
            return spacer.children.map(function (row) {
                return Number(row.dataset.chatId);
            });
        },
        row: function (chatId) {
            return spacer.children.find(function (row) {
                return Number(row.dataset.chatId) === chatId;
            });
        },
        scrollTo: function (top) {
            chatListEl.scrollTop = top;
            chatListEl.dispatchEvent(makeEvent('scroll'));
            var pending = frames.splice(0);
            pending.forEach(function (callback) {
                callback();
            });
        }
    };
}

function test(name, fn) {
    fn();
    console.log('PASS: ' + name);
}

test('only the visible window (+overscan) of 200 chats is rendered', function () {
    var h = createHarness(200);
    h.BF.chatList.render();
    // окно: 0..ceil(300/75)+8 = 13 строк
    assert.equal(h.spacer.children.length, 13);
    assert.equal(h.spacer.style.height, 200 * 75 + 'px');
    assert.equal(h.row(1000).getAttribute('aria-setsize'), '200');
    assert.equal(h.row(1000).getAttribute('aria-posinset'), '1');
    assert.equal(h.row(1000).getAttribute('role'), 'option');
});

test('scrolling moves the window and keeps the rover row in the DOM', function () {
    var h = createHarness(200);
    h.BF.chatList.render();
    h.scrollTo(100 * 75);
    var ids = h.rowIds();
    assert.ok(ids.indexOf(1100) >= 0, 'row 100 should be rendered');
    assert.ok(ids.indexOf(1050) < 0, 'row 50 should be recycled');
    assert.ok(ids.indexOf(1000) >= 0, 'rover (first row) stays in the DOM');
    assert.equal(h.row(1000).tabIndex, 0);
    assert.equal(h.row(1100).style.transform, 'translateY(' + 100 * 75 + 'px)');
});

test('repeated render without data changes does not touch row markup', function () {
    var h = createHarness(50);
    h.BF.chatList.render();
    stats.innerHTMLWrites = 0;
    h.BF.chatList.render();
    h.BF.chatList.render();
    assert.equal(stats.innerHTMLWrites, 0);
});

test('a chat bumped to the top rewrites only its own row and reuses the node', function () {
    var h = createHarness(50);
    h.BF.chatList.render();
    var movedEl = h.row(1005);
    var chats = h.chats();
    var moved = chats.splice(5, 1)[0];
    moved.lastMessage = { id: 1, sentAt: 1, senderId: 2, type: 1, content: { text: 'hello' } };
    chats.unshift(moved);
    stats.innerHTMLWrites = 0;
    h.BF.chatList.render();
    assert.equal(stats.innerHTMLWrites, 1);
    assert.equal(h.row(1005), movedEl);
    assert.equal(h.spacer.children[0], movedEl);
    assert.equal(movedEl.style.transform, 'translateY(0px)');
    assert.equal(h.row(1000).getAttribute('aria-posinset'), '2');
});

test('duplicate chat ids from offset paging render a single row', function () {
    var h = createHarness(3);
    h.setChats(h.chats().concat([h.chats()[1]]));
    h.BF.chatList.render();
    assert.deepEqual(h.rowIds(), [1000, 1001, 1002]);
    assert.equal(h.row(1001).getAttribute('aria-setsize'), '3');
});

test('click and Enter open the original chat id; Enter also shows the chat on mobile', function () {
    var h = createHarness(5);
    h.BF.chatList.render();
    var inner = new FakeElement('span', h.document);
    h.row(1002).appendChild(inner);
    inner.dispatchEvent(makeEvent('click'));
    assert.deepEqual(h.opened, [1002]);
    assert.equal(typeof h.opened[0], 'number');
    var enter = makeEvent('keydown', { key: 'Enter' });
    h.row(1003).dispatchEvent(enter);
    assert.deepEqual(h.opened, [1002, 1003]);
    assert.equal(enter.defaultPrevented, true);
    assert.equal(h.mobileShown(), 1);
});

test('arrow keys move focus and the roving tabindex; the open chat is aria-selected', function () {
    var h = createHarness(20);
    h.state.currentChatId = 1004;
    h.BF.chatList.render();
    assert.equal(h.row(1004).getAttribute('aria-selected'), 'true');
    assert.equal(h.row(1004).tabIndex, 0, 'rover follows the open chat');
    h.row(1004).focus();
    h.row(1004).dispatchEvent(makeEvent('keydown', { key: 'ArrowDown' }));
    assert.equal(h.document.activeElement, h.row(1005));
    assert.equal(h.row(1005).tabIndex, 0);
    assert.equal(h.row(1004).tabIndex, -1);
    h.row(1005).dispatchEvent(makeEvent('keydown', { key: 'End' }));
    assert.equal(h.document.activeElement, h.row(1019));
    assert.ok(h.list.scrollTop > 0, 'End scrolls the last row into view');
});

test('the focused row stays in the DOM when scrolled far away', function () {
    var h = createHarness(200);
    h.BF.chatList.render();
    var focused = h.row(1003);
    focused.focus();
    h.scrollTo(150 * 75);
    assert.equal(h.row(1003), focused);
    assert.equal(h.document.activeElement, focused);
});

test('the focused row keeps focus when a new message bumps it to the top', function () {
    var h = createHarness(20);
    h.BF.chatList.render();
    var focused = h.row(1005);
    focused.focus();
    var chats = h.chats();
    chats.unshift(chats.splice(5, 1)[0]);
    h.BF.chatList.render();
    assert.equal(h.document.activeElement, focused, 'moving the focused node would blur it');
    assert.equal(focused.style.transform, 'translateY(0px)');
    assert.equal(focused.getAttribute('aria-posinset'), '1');
});

test('when the focused chat disappears, focus moves to its neighbour', function () {
    var h = createHarness(10);
    h.BF.chatList.render();
    h.row(1004).focus();
    h.setChats(
        h.chats().filter(function (chat) {
            return chat.id !== 1004;
        })
    );
    h.BF.chatList.render();
    assert.equal(h.row(1004), undefined);
    assert.equal(h.document.activeElement, h.row(1005));
});

function openChatAction(h, chatId, action) {
    h.BF.chatList.render();
    h.row(chatId).dispatchEvent(makeEvent('contextmenu', { clientX: 20, clientY: 20 }));
    var button = h.contextMenu.children.find(function (child) {
        return child.dataset.act === action;
    });
    assert.ok(button, 'context menu action should exist');
    button.dispatchEvent(makeEvent('click'));
}

async function runAsyncTest(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function runAsyncTests() {
    await runAsyncTest('delete modal shows pending state and removes the chat after success', async function () {
        var h = createHarness(3);
        h.state.currentChatId = 1001;
        openChatAction(h, 1001, 'delete-chat');

        h.deleteChatOk.onclick();
        assert.equal(h.deleteChatOk.disabled, true);
        assert.equal(h.deleteChatOk.textContent, 'dialog.deleteChat.pending');
        assert.equal(h.deleteChatOk.classList.contains('is-loading'), true);
        assert.equal(h.deleteChatOk.getAttribute('aria-busy'), 'true');
        assert.equal(h.deleteChatCancel.disabled, true);
        assert.equal(h.deleteChatCancel.disabled, true);

        await Promise.resolve();
        h.resolveDelete()();
        await new Promise(function (resolve) { setImmediate(resolve); });

        assert.deepEqual(h.chats().map(function (chat) { return chat.id; }), [1000, 1002]);
        assert.equal(h.closedChatCount(), 1);
        assert.equal(h.deleteChatOverlay.classList.contains('visible'), false);
        assert.equal(h.deleteChatOk.disabled, false);
        assert.equal(h.deleteChatOk.classList.contains('is-loading'), false);
        assert.equal(h.listChatsCalls(), 0, 'success does not reload the chat list');
    });

    await runAsyncTest('group leave uses its pending label and removes locally', async function () {
        var h = createHarness(2);
        h.chats()[0].isGroupChat = true;
        openChatAction(h, 1000, 'leave-chat');

        h.deleteChatOk.onclick();
        assert.equal(h.deleteChatOk.disabled, true);
        assert.equal(h.deleteChatOk.textContent, 'dialog.leaveChat.pending');
        await new Promise(function (resolve) { setImmediate(resolve); });

        assert.deepEqual(h.chats().map(function (chat) { return chat.id; }), [1001]);
        assert.equal(h.listChatsCalls(), 0, 'success does not reload the chat list');
    });

    await runAsyncTest('chat_hidden removes and closes once without loading the list', async function () {
        var h = createHarness(3);
        h.state.currentChatId = 1001;
        h.BF.chatList.render();

        h.emitRealtime('chat_hidden', { chatId: 1001 });
        h.emitRealtime('chat_hidden', { chatId: 1001 });

        assert.deepEqual(h.chats().map(function (chat) { return chat.id; }), [1000, 1002]);
        assert.equal(h.closedChatCount(), 1);
        assert.equal(h.listChatsCalls(), 0, 'realtime removal does not reload the chat list');
    });

    await runAsyncTest('chat_hidden before the delete response does not remove twice', async function () {
        var h = createHarness(3);
        h.state.currentChatId = 1001;
        openChatAction(h, 1001, 'delete-chat');
        h.deleteChatOk.onclick();
        await Promise.resolve();

        h.emitRealtime('chat_hidden', { chatId: 1001 });
        h.resolveDelete()();
        await new Promise(function (resolve) { setImmediate(resolve); });

        assert.deepEqual(h.chats().map(function (chat) { return chat.id; }), [1000, 1002]);
        assert.equal(h.closedChatCount(), 1);
        assert.equal(h.listChatsCalls(), 0, 'RPC success does not reload the chat list');
        assert.equal(h.deleteChatOverlay.classList.contains('visible'), false);
    });

    await runAsyncTest('chat_hidden after a list snapshot reconciles the already-updated total', async function () {
        var h = createHarness(0);
        h.setListChats(function () {
            return Promise.resolve({
                chats: [
                    { id: 1000, title: 'Chat 0', chatType: 0 },
                    { id: 1001, title: 'Chat 1', chatType: 0 },
                    { id: 1002, title: 'Chat 2', chatType: 0 }
                ],
                totalCount: 3
            });
        });
        await h.BF.chatList.load(true);

        h.setListChats(function () {
            return Promise.resolve({
                chats: [
                    { id: 1001, title: 'Chat 1', chatType: 0 },
                    { id: 1002, title: 'Chat 2', chatType: 0 }
                ],
                totalCount: 2
            });
        });
        await h.BF.chatList.refreshQuiet();

        h.emitRealtime('chat_hidden', { chatId: 1000 });
        await new Promise(function (resolve) {
            setImmediate(resolve);
        });
        await h.BF.chatList.load();

        assert.deepEqual(
            h.chats().map(function (chat) {
                return chat.id;
            }),
            [1001, 1002]
        );
        assert.equal(h.listChatsCalls(), 3, 'an absent chat triggers quiet total reconciliation');
    });

    await runAsyncTest('removing a locally-created chat preserves server pagination counts', async function () {
        var h = createHarness(0);
        var offsets = [];
        h.setListChats(function (offset) {
            offsets.push(offset);
            if (offset === 2) {
                return Promise.resolve({ chats: [{ id: 1003, title: 'Chat 3', chatType: 0 }], totalCount: 3 });
            }
            return Promise.resolve({
                chats: [
                    { id: 1000, title: 'Chat 0', chatType: 0 },
                    { id: 1001, title: 'Chat 1', chatType: 0 }
                ],
                totalCount: 3
            });
        });
        await h.BF.chatList.load(true);
        h.setChats(h.chats().concat([{ id: 1002, title: 'Locally-created chat', chatType: 0 }]));

        h.emitRealtime('chat_hidden', { chatId: 1002 });
        await new Promise(function (resolve) {
            setImmediate(resolve);
        });
        await h.BF.chatList.load();

        assert.deepEqual(
            h.chats().map(function (chat) {
                return chat.id;
            }),
            [1000, 1001, 1003]
        );
        assert.deepEqual(offsets, [0, 0, 2], 'the new chat is not counted in the server page offset');
    });

    await runAsyncTest('pagination offset ignores a locally-created chat that remains in the list', async function () {
        var h = createHarness(0);
        var offsets = [];
        h.setListChats(function (offset) {
            offsets.push(offset);
            if (offset === 2) {
                return Promise.resolve({ chats: [{ id: 1003, title: 'Chat 3', chatType: 0 }], totalCount: 4 });
            }
            if (offset === 3) {
                return Promise.resolve({ chats: [{ id: 1004, title: 'Chat 4', chatType: 0 }], totalCount: 4 });
            }
            return Promise.resolve({
                chats: [
                    { id: 1000, title: 'Chat 0', chatType: 0 },
                    { id: 1001, title: 'Chat 1', chatType: 0 }
                ],
                totalCount: 4
            });
        });
        await h.BF.chatList.load(true);
        h.setChats(h.chats().concat([{ id: 1002, title: 'Locally-created chat', chatType: 0 }]));

        await h.BF.chatList.load();
        await h.BF.chatList.load();

        assert.deepEqual(offsets, [0, 2, 3]);
        assert.deepEqual(
            h.chats().map(function (chat) {
                return chat.id;
            }),
            [1000, 1001, 1002, 1003, 1004]
        );
    });

    await runAsyncTest('chat_hidden decrements pagination once for a chat outside the loaded page', async function () {
        var h = createHarness(0);
        var listPage = 0;
        h.setListChats(function () {
            listPage++;
            if (listPage === 2) {
                return Promise.resolve({ chats: [{ id: 1002, title: 'Chat 2', chatType: 0 }], totalCount: 4 });
            }
            if (listPage === 3) {
                return Promise.resolve({
                    chats: [
                        { id: 1000, title: 'Chat 0', chatType: 0 },
                        { id: 1001, title: 'Chat 1', chatType: 0 }
                    ],
                    totalCount: 3
                });
            }
            return Promise.resolve({
                chats: [
                    { id: 1000, title: 'Chat 0', chatType: 0 },
                    { id: 1001, title: 'Chat 1', chatType: 0 }
                ],
                totalCount: 4
            });
        });
        await h.BF.chatList.load(true);
        await h.BF.chatList.load();

        h.emitRealtime('chat_hidden', { chatId: 1003 });
        h.emitRealtime('chat_hidden', { chatId: 1003 });
        await new Promise(function (resolve) {
            setImmediate(resolve);
        });
        await h.BF.chatList.load();

        assert.deepEqual(
            h.chats().map(function (chat) {
                return chat.id;
            }),
            [1000, 1001, 1002]
        );
        assert.equal(h.listChatsCalls(), 3, 'quiet total reconciliation keeps both loaded pages');
    });

    await runAsyncTest(
        'removing a loaded chat invalidates an in-flight next page and retries at the shifted offset',
        async function () {
            var h = createHarness(0);
            var offsets = [];
            var resolvePage;
            var requests = 0;
            h.setListChats(function (offset) {
                offsets.push(offset);
                requests++;
                if (requests === 1) {
                    return Promise.resolve({
                        chats: [
                            { id: 1000, title: 'Chat 0', chatType: 0 },
                            { id: 1001, title: 'Chat 1', chatType: 0 }
                        ],
                        totalCount: 3
                    });
                }
                if (requests === 2)
                    return new Promise(function (resolve) {
                        resolvePage = resolve;
                    });
                if (requests === 3) {
                    return Promise.resolve({
                        chats: [
                            { id: 1001, title: 'Chat 1', chatType: 0 },
                            { id: 1002, title: 'Chat 2', chatType: 0 }
                        ],
                        totalCount: 2
                    });
                }
                assert.equal(offset, 1, 'the retry starts at the adjusted server offset');
                return Promise.resolve({ chats: [{ id: 1002, title: 'Chat 2', chatType: 0 }], totalCount: 2 });
            });
            await h.BF.chatList.load(true);
            var nextPage = h.BF.chatList.load();
            assert.deepEqual(offsets, [0, 2]);

            h.emitRealtime('chat_hidden', { chatId: 1000 });
            resolvePage({ chats: [{ id: 1002, title: 'Chat 2', chatType: 0 }], totalCount: 3 });
            await nextPage;

            assert.deepEqual(
                h.chats().map(function (chat) {
                    return chat.id;
                }),
                [1001, 1002]
            );
            assert.deepEqual(offsets, [0, 2, 0, 1]);
        }
    );

    await runAsyncTest('a tombstone in a stale page response does not advance the server offset', async function () {
        var h = createHarness(0);
        var offsets = [];
        var resolvePage;
        var requests = 0;
        h.setListChats(function (offset) {
            offsets.push(offset);
            requests++;
            if (requests === 1) {
                return Promise.resolve({
                    chats: [
                        { id: 1000, title: 'Chat 0', chatType: 0 },
                        { id: 1001, title: 'Chat 1', chatType: 0 }
                    ],
                    totalCount: 5
                });
            }
            if (requests === 2)
                return new Promise(function (resolve) {
                    resolvePage = resolve;
                });
            if (requests === 3) return Promise.resolve({ chats: [], totalCount: 4 });
            assert.equal(offset, 3, 'offset counts current server rows, excluding the hidden row');
            return Promise.resolve({ chats: [{ id: 1004, title: 'Chat 4', chatType: 0 }], totalCount: 4 });
        });
        await h.BF.chatList.load(true);
        var nextPage = h.BF.chatList.load();
        h.emitRealtime('chat_hidden', { chatId: 1002 });
        resolvePage({
            chats: [
                { id: 1002, title: 'Deleted chat', chatType: 0 },
                { id: 1003, title: 'Chat 3', chatType: 0 }
            ],
            totalCount: 5
        });
        await nextPage;
        await h.BF.chatList.load();

        assert.deepEqual(
            h.chats().map(function (chat) {
                return chat.id;
            }),
            [1000, 1001, 1003, 1004]
        );
        assert.deepEqual(offsets, [0, 2, 0, 3]);
    });

    await runAsyncTest('pagination requested during total reconciliation continues afterward', async function () {
        var h = createHarness(0);
        var resolveTotal;
        var requests = 0;
        h.setListChats(function (offset) {
            requests++;
            if (requests === 1) {
                return Promise.resolve({
                    chats: [
                        { id: 1000, title: 'Chat 0', chatType: 0 },
                        { id: 1001, title: 'Chat 1', chatType: 0 }
                    ],
                    totalCount: 4
                });
            }
            if (requests === 2) {
                return new Promise(function (resolve) {
                    resolveTotal = resolve;
                });
            }
            assert.equal(offset, 2, 'pagination keeps the loaded server offset');
            return Promise.resolve({ chats: [{ id: 1002, title: 'Chat 2', chatType: 0 }], totalCount: 3 });
        });
        await h.BF.chatList.load(true);
        h.emitRealtime('chat_hidden', { chatId: 1003 });
        var queuedLoad = h.BF.chatList.load();
        resolveTotal({ chats: [], totalCount: 3 });
        await queuedLoad;

        assert.deepEqual(
            h.chats().map(function (chat) {
                return chat.id;
            }),
            [1000, 1001, 1002]
        );
        assert.equal(h.listChatsCalls(), 3);
    });

    await runAsyncTest('reset requested during total reconciliation reloads the chat list', async function () {
        var h = createHarness(0);
        var resolveTotal;
        var requests = 0;
        h.setListChats(function (offset) {
            requests++;
            if (requests === 1) {
                return Promise.resolve({
                    chats: [
                        { id: 1000, title: 'Chat 0', chatType: 0 },
                        { id: 1001, title: 'Chat 1', chatType: 0 }
                    ],
                    totalCount: 2
                });
            }
            if (requests === 2) return new Promise(function (resolve) { resolveTotal = resolve; });
            assert.equal(offset, 0, 'a reset starts from the first server page');
            return Promise.resolve({
                chats: [
                    { id: 1000, title: 'Chat 0', chatType: 0 },
                    { id: 1001, title: 'Chat 1', chatType: 0 },
                    { id: 1002, title: 'New chat', chatType: 0 }
                ],
                totalCount: 3
            });
        });
        await h.BF.chatList.load(true);
        h.emitRealtime('chat_hidden', { chatId: 1999 });
        var queuedReset = h.BF.chatList.load(true);
        resolveTotal({ chats: [], totalCount: 2 });
        await queuedReset;

        assert.deepEqual(h.chats().map(function (chat) { return chat.id; }), [1000, 1001, 1002]);
        assert.equal(h.listChatsCalls(), 3);
    });

    await runAsyncTest('an in-flight page response cannot restore a hidden chat or stale total', async function () {
        var h = createHarness(0);
        var resolveList;
        var requests = 0;
        h.setListChats(function () {
            requests++;
            if (requests > 1) {
                return Promise.resolve({ chats: [{ id: 1001, title: 'Remaining chat', chatType: 0 }], totalCount: 1 });
            }
            return new Promise(function (resolve) {
                resolveList = resolve;
            });
        });
        var request = h.BF.chatList.load(true);
        h.emitRealtime('chat_hidden', { chatId: 1000 });
        resolveList({
            chats: [
                { id: 1000, title: 'Deleted chat', chatType: 0 },
                { id: 1001, title: 'Remaining chat', chatType: 0 }
            ],
            totalCount: 2
        });
        await request;
        await new Promise(function (resolve) {
            setImmediate(resolve);
        });
        await h.BF.chatList.load();

        assert.deepEqual(
            h.chats().map(function (chat) {
                return chat.id;
            }),
            [1001]
        );
        assert.equal(h.listChatsCalls(), 2, 'a quiet follow-up reconciles the in-flight response');
    });

    await runAsyncTest('an in-flight quiet refresh cannot restore a hidden chat', async function () {
        var h = createHarness(0);
        h.setListChats(function () {
            return Promise.resolve({
                chats: [
                    { id: 1000, title: 'Chat 0', chatType: 0 },
                    { id: 1001, title: 'Chat 1', chatType: 0 }
                ],
                totalCount: 2
            });
        });
        await h.BF.chatList.load(true);

        var resolveRefresh;
        h.setListChats(function () {
            return new Promise(function (resolve) {
                resolveRefresh = resolve;
            });
        });
        var refresh = h.BF.chatList.refreshQuiet();
        h.emitRealtime('chat_hidden', { chatId: 1000 });
        h.setListChats(function () {
            return Promise.resolve({
                chats: [
                    { id: 1002, title: 'New chat', chatType: 0 },
                    { id: 1001, title: 'Chat 1', chatType: 0 }
                ],
                totalCount: 2
            });
        });
        resolveRefresh({
            chats: [
                { id: 1000, title: 'Chat 0', chatType: 0 },
                { id: 1001, title: 'Chat 1', chatType: 0 }
            ],
            totalCount: 2
        });
        await refresh;
        await new Promise(function (resolve) {
            setImmediate(resolve);
        });
        assert.deepEqual(
            h.chats().map(function (chat) {
                return chat.id;
            }),
            [1001]
        );
        h.setListChats(function () {
            return Promise.resolve({ chats: [{ id: 1002, title: 'New chat', chatType: 0 }], totalCount: 2 });
        });
        await h.BF.chatList.load();
        await h.BF.chatList.load();

        assert.deepEqual(
            h.chats().map(function (chat) {
                return chat.id;
            }),
            [1001, 1002]
        );
        assert.equal(h.listChatsCalls(), 4, 'total reconciliation preserves pagination for the new chat');
    });

    await runAsyncTest(
        'an in-flight response that already includes the removal does not decrement twice',
        async function () {
            var h = createHarness(0);
            h.setListChats(function () {
                return Promise.resolve({
                    chats: [
                        { id: 1000, title: 'Chat 0', chatType: 0 },
                        { id: 1001, title: 'Chat 1', chatType: 0 }
                    ],
                    totalCount: 2
                });
            });
            await h.BF.chatList.load(true);

            var resolveRefresh;
            h.setListChats(function () {
                return new Promise(function (resolve) {
                    resolveRefresh = resolve;
                });
            });
            var refresh = h.BF.chatList.refreshQuiet();
            h.emitRealtime('chat_hidden', { chatId: 1000 });
            h.setListChats(function () {
                return Promise.resolve({ chats: [{ id: 1001, title: 'Chat 1', chatType: 0 }], totalCount: 1 });
            });
            resolveRefresh({ chats: [{ id: 1001, title: 'Chat 1', chatType: 0 }], totalCount: 1 });
            await refresh;
            await new Promise(function (resolve) {
                setImmediate(resolve);
            });
            h.setListChats(function () {
                return Promise.resolve({ chats: [], totalCount: 1 });
            });
            await h.BF.chatList.load();

            assert.deepEqual(
                h.chats().map(function (chat) {
                    return chat.id;
                }),
                [1001]
            );
            assert.equal(h.listChatsCalls(), 3, 'the response and follow-up each account for the removal once');
        }
    );

    await runAsyncTest('failed delete restores controls and keeps the chat', async function () {
        var h = createHarness(2);
        openChatAction(h, 1000, 'delete-chat');
        h.deleteChatOk.onclick();
        await Promise.resolve();
        h.rejectDelete()(new Error('delete failed'));
        await new Promise(function (resolve) { setImmediate(resolve); });

        assert.deepEqual(h.chats().map(function (chat) { return chat.id; }), [1000, 1001]);
        assert.equal(h.deleteChatOk.disabled, false);
        assert.equal(h.deleteChatOk.textContent, 'common.delete');
        assert.equal(h.deleteChatOk.classList.contains('is-loading'), false);
        assert.deepEqual(h.toastCalls, [['error.deleteChat', true]]);
    });
}

runAsyncTests().catch(function (error) {
    console.error(error);
    process.exitCode = 1;
});
