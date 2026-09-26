const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function createHarness(options) {
    var chats = (options && options.chats) || [];
    var opened = [];
    var loadCalls = 0;
    var replaced = [];
    var document = { cookie: (options && options.cookie) || '' };
    var users = (options && options.users) || [];
    var BF = {
        api: {
            searchUsers: function () {
                return Promise.resolve({ users: users });
            },
            getPersonChatId: function (userId) {
                return Promise.resolve({ chatId: 'chat-of-' + userId });
            }
        }
    };
    var location = { href: (options && options.href) || 'https://web.test/messenger', hostname: 'web.test' };
    var context = vm.createContext({
        BF: BF,
        document: document,
        location: location,
        URL: URL,
        Promise: Promise,
        console: { error: function () {} }
    });
    context.window = {
        BF: BF,
        location: location,
        history: {
            replaceState: function (state, title, url) {
                replaced.push(url);
            }
        }
    };
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/deep-link.js'), 'utf8'), context);
    BF.deepLink.init({
        getChats: function () {
            return chats;
        },
        loadChats: function () {
            loadCalls++;
            // Защита от зацикливания: без неё регрессия «перезагружать список при каждом промахе» вешала бы тест.
            if (loadCalls > 3) return Promise.reject(new Error('loadChats is called in a loop'));
            return Promise.resolve().then(function () {
                if (options && options.afterLoad) chats = chats.concat(options.afterLoad);
            });
        },
        openChat: function (chatId) {
            opened.push(chatId);
        }
    });
    return {
        BF: BF,
        document: document,
        opened: opened,
        replaced: replaced,
        loadCalls: function () {
            return loadCalls;
        },
        addChat: function (chat) {
            chats = chats.concat(chat);
        }
    };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('a push for a known chat opens it right away', async function () {
        var h = createHarness({ chats: [{ id: 5 }] });
        h.BF.deepLink.openFromPush('5'); // id из push приходит строкой
        assert.deepEqual(h.opened, ['5']);
        assert.equal(h.loadCalls(), 0);
    });

    await test('a push for a chat outside the loaded list reloads the list once, then opens it', async function () {
        var h = createHarness({ chats: [{ id: 1 }], afterLoad: [{ id: 9 }] });
        h.BF.deepLink.openFromPush('9');
        assert.deepEqual(h.opened, [], 'not opened until the list is loaded');
        await settle();
        assert.equal(h.loadCalls(), 1);
        assert.deepEqual(h.opened, ['9']);
    });

    await test('an unknown chat stays pending and is not reloaded twice; openPending retries later', async function () {
        var h = createHarness({ chats: [{ id: 1 }] });
        h.BF.deepLink.openFromPush('42');
        await settle();
        h.BF.deepLink.openFromPush('42'); // тот же чат: повторной загрузки списка нет
        await settle();
        assert.equal(h.loadCalls(), 1);
        assert.deepEqual(h.opened, []);
        h.addChat({ id: 42 });
        h.BF.deepLink.openPending();
        assert.deepEqual(h.opened, ['42']);
        h.BF.deepLink.openPending();
        assert.deepEqual(h.opened, ['42'], 'the pending link is consumed');
    });

    await test('?chat= is removed from the address and the chat is opened', async function () {
        var h = createHarness({ chats: [{ id: 7 }], href: 'https://web.test/messenger?chat=7&call=1&x=y#top' });
        h.BF.deepLink.openFromUrl();
        assert.deepEqual(h.replaced, ['/messenger?x=y#top']);
        assert.deepEqual(h.opened, ['7']);

        var none = createHarness({ chats: [], href: 'https://web.test/messenger' });
        none.BF.deepLink.openFromUrl();
        assert.deepEqual(none.replaced, []);
        assert.deepEqual(none.opened, []);
    });

    await test('the bf_open_chat cookie opens the chat of the exactly matching username and is consumed', async function () {
        var h = createHarness({
            cookie: 'a=1; bf_open_chat=Alice; b=2',
            users: [
                { id: 1, username: 'alice2' },
                { id: 2, username: 'ALICE' }
            ]
        });
        h.BF.deepLink.openFromCookie();
        await settle();
        assert.deepEqual(h.opened, ['chat-of-2']);
        assert.match(h.document.cookie, /bf_open_chat=; path=\/; expires=Thu, 01 Jan 1970/, 'cookie is deleted');

        var noMatch = createHarness({ cookie: 'bf_open_chat=bob', users: [{ id: 3, username: 'bobby' }] });
        noMatch.BF.deepLink.openFromCookie();
        await settle();
        assert.deepEqual(noMatch.opened, [], 'no exact match — nothing is opened');
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
