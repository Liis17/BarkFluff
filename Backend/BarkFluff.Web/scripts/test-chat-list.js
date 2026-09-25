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
        var className = selector.replace(/^\./, '');
        for (var cur = this; cur; cur = cur.parentNode) {
            if (cur.classList && cur.classList.contains(className)) return cur;
        }
        return null;
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
    document.body.appendChild(chatListEl);
    document.querySelector = function (selector) {
        return { '#chatList': chatListEl, '#chatContextMenu': chatContextMenu }[selector] || null;
    };

    var chats = [];
    for (var i = 0; i < chatCount; i++) {
        chats.push({ id: 1000 + i, title: 'Chat ' + i, chatType: 0, lastMessage: null, countUnread: 0 });
    }
    var opened = [];
    var mobileShown = 0;
    var frames = [];
    var BF = {
        api: {
            listChats: function () {
                return Promise.resolve({ chats: [], totalCount: 0 });
            }
        },
        folders: {
            renderTabs: function () {},
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
        }
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
