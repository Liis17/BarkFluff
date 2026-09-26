const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function createHarness(options) {
    var favicon = {
        attrs: { href: '/favicon.ico', type: 'image/x-icon' },
        getAttribute: function (name) {
            return this.attrs[name];
        },
        setAttribute: function (name, value) {
            this.attrs[name] = value;
        },
        removeAttribute: function (name) {
            delete this.attrs[name];
        }
    };
    var document = {
        title: '',
        visibilityState: 'visible',
        getElementById: function (id) {
            return id === 'favicon' ? favicon : null;
        }
    };
    var chats = [];
    var currentChatId = null;
    var notifications = [];
    var BF = {
        i18n: {
            t: function (key, params) {
                if (key === 'app.title') return 'BarkFluff';
                if (key === 'tab.chatWith') return 'Chat • ' + params.name;
                return key;
            }
        },
        utils: {
            truncate: function (value, len) {
                return value.length > len ? value.slice(0, len) + '...' : value;
            },
            attachmentEmoji: function (type) {
                return '[' + type + ']';
            }
        }
    };
    class FakeNotification {
        constructor(title, opts) {
            this.title = title;
            this.opts = opts;
            notifications.push(this);
        }
        close() {}
    }
    FakeNotification.permission = (options && options.permission) || 'granted';
    var context = vm.createContext({
        BF: BF,
        document: document,
        Notification: FakeNotification,
        Image: function () {},
        setTimeout: function () {}
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/attention.js'), 'utf8'), context);
    BF.attention.init({
        getChats: function () {
            return chats;
        },
        getCurrentChatId: function () {
            return currentChatId;
        }
    });
    return {
        BF: BF,
        document: document,
        favicon: favicon,
        notifications: notifications,
        setChats: function (value) {
            chats = value;
        },
        setCurrentChat: function (id) {
            currentChatId = id;
        }
    };
}

function test(name, fn) {
    fn();
    console.log('PASS: ' + name);
}

test('the tab title shows the total unread counter, capped at 99+', function () {
    var h = createHarness();
    h.BF.attention.updateTitleBadge();
    assert.equal(h.document.title, 'BarkFluff');
    h.setChats([{ countUnread: 2 }, { countUnread: 0 }, { countUnread: 3 }, {}]);
    h.BF.attention.updateTitleBadge();
    assert.equal(h.document.title, '(5) BarkFluff');
    h.setChats([{ countUnread: 60 }, { countUnread: 60 }]);
    h.BF.attention.updateTitleBadge();
    assert.equal(h.document.title, '(99+) BarkFluff');
});

test('a chat context replaces the base title and reset brings back the app title and favicon', function () {
    var h = createHarness();
    h.setChats([{ countUnread: 1 }]);
    h.BF.attention.setChatTabContext(h.BF.attention.chatTabTitle({ firstName: 'Ann', lastName: 'Lee' }), null);
    assert.equal(h.document.title, '(1) Chat • Ann Lee');
    assert.equal(h.favicon.getAttribute('href'), '/favicon.ico');
    assert.equal(h.BF.attention.chatTabTitle({}), 'Chat • common.user', 'nameless user falls back to a generic label');
    h.BF.attention.resetChatTabContext();
    assert.equal(h.document.title, '(1) BarkFluff');
    assert.equal(h.favicon.getAttribute('type'), 'image/x-icon');
});

test('a notification is shown for a message in another chat, with a shortened body', function () {
    var h = createHarness();
    h.setCurrentChat(1);
    h.BF.attention.showNewMessageNotification('Ann', { id: 10, chatId: 2, content: { text: 'x'.repeat(100) } });
    assert.equal(h.notifications.length, 1);
    assert.equal(h.notifications[0].title, 'Ann');
    assert.equal(h.notifications[0].opts.body, 'x'.repeat(80) + '...');
    assert.equal(h.notifications[0].opts.tag, 'bf-msg-10');

    h.BF.attention.showNewMessageNotification('Ann', { id: 11, chatId: 2, content: { attachments: [{ type: 'IMAGE' }] } });
    assert.equal(h.notifications[1].opts.body, '[IMAGE]');
});

test('no notification for the open chat, a hidden page or without permission', function () {
    var h = createHarness();
    h.setCurrentChat(2);
    h.BF.attention.showNewMessageNotification('Ann', { id: 1, chatId: 2, content: { text: 'hi' } });
    assert.equal(h.notifications.length, 0, 'the chat is open');

    h.setCurrentChat(1);
    h.document.visibilityState = 'hidden';
    h.BF.attention.showNewMessageNotification('Ann', { id: 2, chatId: 2, content: { text: 'hi' } });
    assert.equal(h.notifications.length, 0, 'the page is hidden (service worker pushes handle it)');

    var denied = createHarness({ permission: 'denied' });
    denied.BF.attention.showNewMessageNotification('Ann', { id: 3, chatId: 2, content: { text: 'hi' } });
    assert.equal(denied.notifications.length, 0);
});
