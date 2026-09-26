const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Минимальный DOM: только то, что использует feed.js.
class FakeElement {
    constructor(tagName, doc) {
        this.tagName = tagName;
        this.ownerDocument = doc;
        this.parentNode = null;
        this.children = [];
        this.attributes = {};
        this.dataset = {};
        this.style = {};
        this.listeners = {};
        this.className = '';
        this.textContent = '';
        this.offsetTop = 0;
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
                if (force === undefined ? !this.contains(name) : force) this.add(name);
                else this.remove(name);
            }
        };
    }

    set innerHTML(value) {
        this.children.forEach(function (child) {
            child.parentNode = null;
        });
        this.children = [];
        this._html = value;
        this.resetFocusIfDetached();
    }

    get tabIndex() {
        return this.attributes.tabindex === undefined ? -1 : Number(this.attributes.tabindex);
    }

    set tabIndex(value) {
        this.attributes.tabindex = String(value);
    }

    get isConnected() {
        for (var cur = this; cur; cur = cur.parentNode) if (cur === this.ownerDocument) return true;
        return false;
    }

    get firstElementChild() {
        return this.children[0] || null;
    }

    get lastElementChild() {
        return this.children[this.children.length - 1] || null;
    }

    get firstChild() {
        return this.firstElementChild;
    }

    sibling(offset) {
        if (!this.parentNode) return null;
        var list = this.parentNode.children;
        return list[list.indexOf(this) + offset] || null;
    }

    get nextElementSibling() {
        return this.sibling(1);
    }

    get previousElementSibling() {
        return this.sibling(-1);
    }

    setAttribute(name, value) {
        this.attributes[name] = String(value);
        if (name === 'id') this.id = String(value);
    }

    getAttribute(name) {
        return this.attributes[name] === undefined ? null : this.attributes[name];
    }

    hasAttribute(name) {
        return this.attributes[name] !== undefined;
    }

    removeAttribute(name) {
        delete this.attributes[name];
    }

    appendChild(child) {
        return this.insertBefore(child, null);
    }

    insertBefore(child, ref) {
        var nodes = child.isFragment ? child.children.slice() : [child];
        var self = this;
        nodes.forEach(function (node) {
            if (node.parentNode) node.parentNode.children.splice(node.parentNode.children.indexOf(node), 1);
            node.parentNode = self;
            var index = ref ? self.children.indexOf(ref) : -1;
            if (index < 0) self.children.push(node);
            else self.children.splice(index, 0, node);
        });
        if (child.isFragment) child.children = [];
        return child;
    }

    remove() {
        if (!this.parentNode) return;
        this.parentNode.children.splice(this.parentNode.children.indexOf(this), 1);
        this.parentNode = null;
        this.resetFocusIfDetached();
    }

    replaceWith(node) {
        var parent = this.parentNode;
        parent.insertBefore(node, this);
        this.remove();
    }

    resetFocusIfDetached() {
        var doc = this.ownerDocument;
        if (doc && doc.activeElement && !doc.activeElement.isConnected) doc.activeElement = doc.body;
    }

    contains(node) {
        for (var cur = node; cur; cur = cur.parentNode) if (cur === this) return true;
        return false;
    }

    matches(selector) {
        var self = this;
        return selector.split(',').some(function (part) {
            var m = part.trim().match(/^([a-z]*)((?:\.[\w-]+)*)(?:\[([\w-]+)="([^"]*)"\])?$/);
            if (!m) throw new Error('unsupported selector ' + part);
            if (m[1] && self.tagName !== m[1]) return false;
            var classes = m[2].split('.').filter(Boolean);
            if (
                !classes.every(function (c) {
                    return self.classList.contains(c);
                })
            )
                return false;
            if (m[3]) {
                var key = m[3].replace(/^data-/, '').replace(/-(\w)/g, function (_, c) {
                    return c.toUpperCase();
                });
                var value = m[3].indexOf('data-') === 0 ? self.dataset[key] : self.attributes[m[3]];
                if (String(value) !== m[4]) return false;
            }
            return true;
        });
    }

    querySelectorAll(selector) {
        var out = [];
        (function walk(node) {
            node.children.forEach(function (child) {
                if (child.matches(selector)) out.push(child);
                walk(child);
            });
        })(this);
        return out;
    }

    querySelector(selector) {
        return this.querySelectorAll(selector)[0] || null;
    }

    closest(selector) {
        for (var cur = this; cur && cur.matches; cur = cur.parentNode) if (cur.matches(selector)) return cur;
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
        if (this.parentNode && this.parentNode.dispatchEvent) this.parentNode.dispatchEvent(event);
    }

    focus() {
        this.ownerDocument.activeElement = this;
    }

    scrollIntoView() {}

    getBoundingClientRect() {
        return { top: 0, bottom: 0, left: 0, right: 0 };
    }
}

function keydown(key) {
    return {
        type: 'keydown',
        key: key,
        defaultPrevented: false,
        preventDefault: function () {
            this.defaultPrevented = true;
        }
    };
}

async function settle() {
    for (var i = 0; i < 20; i++) await new Promise((resolve) => setImmediate(resolve));
}

function createHarness(count) {
    var doc = new FakeElement('#document');
    doc.ownerDocument = doc;
    doc.body = new FakeElement('body', doc);
    doc.appendChild(doc.body);
    doc.activeElement = doc.body;
    doc.createElement = function (tag) {
        return new FakeElement(tag, doc);
    };
    doc.createDocumentFragment = function () {
        var frag = new FakeElement('#fragment', doc);
        frag.isFragment = true;
        return frag;
    };
    var ids = {};
    ['messagesArea', 'messagesInner', 'loadingMessages', 'feedLiveRegion'].forEach(function (id) {
        ids[id] = new FakeElement('div', doc);
    });
    doc.body.appendChild(ids.messagesArea);
    ids.messagesArea.appendChild(ids.messagesInner);
    doc.body.appendChild(ids.loadingMessages);
    doc.body.appendChild(ids.feedLiveRegion);
    ids.messagesArea.clientHeight = 500;
    ids.messagesArea.scrollHeight = 10000;
    ids.messagesArea.scrollTop = 5000;
    doc.querySelector = function (selector) {
        return ids[selector.replace('#', '')] || null;
    };

    var nextId = 1000;
    function makeMessages(n, senderFor) {
        var list = [];
        for (var i = 0; i < n; i++) {
            var id = nextId++;
            list.push({ id: id, senderId: senderFor(id), sentAt: id * 1000, type: 1, content: { text: 'm' + id } });
        }
        return list;
    }
    var messages = makeMessages(count, function (id) {
        return id % 2 ? 1 : 2;
    });
    var olderPage = null;
    var timers = [];
    var BF = {
        messages: {
            buildMessageElement: function (msg) {
                var group = doc.createElement('div');
                group.className = 'msg-group ' + (msg.senderId === 1 ? 'outgoing' : 'incoming');
                group.dataset.msgId = String(msg.id); // настоящий dataset хранит строки
                var bubble = doc.createElement('div');
                bubble.className = 'msg-bubble';
                group.appendChild(bubble);
                group.offsetTop = Number(msg.id) * 40;
                return Promise.resolve(group);
            }
        },
        files: {
            getCachedFileUrl: function () {
                return null;
            },
            getFileUrls: function () {
                return Promise.resolve();
            }
        },
        api: {
            listMessages: function () {
                return Promise.resolve({ messages: olderPage || [] });
            }
        },
        i18n: {
            t: function (key, params) {
                return params ? key + ' ' + JSON.stringify(params) : key;
            },
            tp: function (key, n) {
                return key + ':' + n;
            }
        },
        utils: {
            formatDate: function () {
                return 'today';
            },
            formatTime: function (ts) {
                return 't' + ts;
            },
            escapeHtml: String,
            truncate: function (value) {
                return value;
            },
            markdownToPlainText: function (value) {
                return value;
            },
            attachmentEmoji: function (type) {
                return '[' + type + ']';
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: doc,
        Promise: Promise,
        setTimeout: function (callback) {
            timers.push(callback);
            return timers.length;
        }
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/feed.js'), 'utf8'), context);
    BF.feed.init({
        getCurrentChatId: function () {
            return 7;
        },
        getCurrentChatType: function () {
            return 0;
        },
        getCurrentChatInfo: function () {
            return { isGroupChat: true };
        },
        getMyUserId: function () {
            return 1;
        },
        getMessages: function () {
            return messages;
        },
        setMessages: function (value) {
            messages = value;
        },
        getUser: function (userId) {
            return Promise.resolve({ firstName: 'User', lastName: String(userId) });
        },
        showMediaOverlay: function () {},
        mergePendingUploads: function () {},
        onPendingCancel: function () {},
        onPendingRetry: function () {},
        decryptPrivateBatch: function () {},
        showToast: function () {}
    });

    var inner = ids.messagesInner;
    return {
        BF: BF,
        doc: doc,
        inner: inner,
        area: ids.messagesArea,
        liveRegion: ids.feedLiveRegion,
        articles: function () {
            return inner.querySelectorAll('.msg-group');
        },
        article: function (id) {
            return inner.querySelector('[data-msg-id="' + id + '"]');
        },
        rovers: function () {
            return inner.querySelectorAll('.msg-group').filter(function (el) {
                return el.hasAttribute('tabindex');
            });
        },
        messages: function () {
            return messages;
        },
        setOlderPage: function (n) {
            var oldest = messages[0].id;
            olderPage = [];
            for (var i = n; i > 0; i--) {
                olderPage.push({ id: oldest - i, senderId: 2, sentAt: (oldest - i) * 1000, type: 1, content: {} });
            }
        },
        runTimers: function () {
            timers.splice(0).forEach(function (callback) {
                callback();
            });
        }
    };
}

// Сравниваем id, а не узлы: диф упавшего assert по фейковым узлам с циклическими ссылками съедает память.
function focusedId(h) {
    var el = h.doc.activeElement;
    return el && el.dataset ? el.dataset.msgId : undefined;
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('rendered messages are ARIA articles with author/time labels and a single rover', async function () {
        var h = createHarness(4);
        await h.BF.feed.render();
        await settle();
        var list = h.articles();
        assert.equal(list.length, 4);
        list.forEach(function (el) {
            assert.equal(el.getAttribute('role'), 'article');
            var bubble = el.querySelector('.msg-bubble');
            assert.equal(el.getAttribute('aria-describedby'), bubble.id);
            assert.equal(bubble.id, 'feed-msg-' + el.dataset.msgId);
        });
        assert.equal(h.article(1001).getAttribute('aria-label'), 'call.you, t1001000');
        assert.equal(h.article(1000).getAttribute('aria-label'), 'User 2, t1000000');
        assert.deepEqual(
            h.rovers().map(function (el) {
                return el.dataset.msgId;
            }),
            ['1003'],
            'only the newest message is tabbable'
        );
        assert.equal(h.inner.getAttribute('aria-busy'), null, 'aria-busy is cleared after rendering');
    });

    await test('arrow keys, Home and End move focus and the roving tabindex', async function () {
        var h = createHarness(5);
        await h.BF.feed.render();
        h.article(1004).focus();
        h.article(1004).dispatchEvent(keydown('ArrowUp'));
        assert.equal(focusedId(h), '1003');
        assert.equal(h.rovers().length, 1);
        assert.equal(h.article(1003).getAttribute('tabindex'), '0');
        h.article(1003).dispatchEvent(keydown('Home'));
        assert.equal(focusedId(h), '1000');
        h.article(1000).dispatchEvent(keydown('End'));
        await settle();
        assert.equal(focusedId(h), '1004');
        assert.equal(h.rovers().length, 1);
    });

    await test('a full re-render keeps focus on the same message, or its neighbour if it was deleted', async function () {
        var h = createHarness(5);
        await h.BF.feed.render();
        h.article(1002).focus();
        await h.BF.feed.render();
        assert.equal(focusedId(h), '1002');
        h.messages().splice(2, 1); // удалено сообщение с фокусом
        await h.BF.feed.render();
        assert.equal(focusedId(h), '1003', 'focus moves to the following message');
    });

    await test('trimming the sliding window moves focus off a dropped message', async function () {
        var h = createHarness(200);
        await h.BF.feed.render();
        h.article(1199).focus();
        h.setOlderPage(30);
        h.area.scrollTop = 0;
        h.area.dispatchEvent({ type: 'scroll' });
        await settle();
        assert.equal(h.messages().length, 200);
        assert.ok(!h.article(1199), 'the newest messages were trimmed');
        assert.equal(focusedId(h), '1169', 'focus lands on the newest surviving message');
        assert.equal(h.rovers().length, 1);
    });

    await test('replaceElement and removeElement preserve focus and the rover', async function () {
        var h = createHarness(3);
        await h.BF.feed.render();
        var old = h.article(1001);
        old.focus();
        var replacement = await h.BF.feed.buildElement({ id: 5000, senderId: 1, sentAt: 1, type: 1, content: {} });
        h.BF.feed.replaceElement(old, replacement);
        assert.equal(focusedId(h), '5000');
        assert.equal(replacement.getAttribute('tabindex'), '0');
        h.BF.feed.removeElement(replacement);
        assert.equal(focusedId(h), '1002');
        assert.equal(h.rovers().length, 1);
    });

    await test('incoming messages are announced in the live region, a burst as one phrase', async function () {
        var h = createHarness(1);
        h.BF.feed.announceIncoming({ id: 1, senderId: 2, content: { text: 'hello' } });
        h.runTimers();
        await settle();
        h.runTimers();
        assert.equal(h.liveRegion.textContent, 'a11y.newMessage {"name":"User 2","text":"hello"}');
        h.BF.feed.announceIncoming({ id: 2, senderId: 2, content: { attachments: [{ type: 'IMAGE' }] } });
        h.BF.feed.announceIncoming({ id: 3, senderId: 2, content: { text: 'x' } });
        h.runTimers();
        await settle();
        h.runTimers();
        assert.equal(h.liveRegion.textContent, 'a11y.newMessages:2');
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
