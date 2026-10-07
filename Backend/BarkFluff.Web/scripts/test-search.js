const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function makeEl() {
    var names = [];
    return {
        className: '',
        innerHTML: '',
        value: '',
        children: [],
        listeners: {},
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
}

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function createHarness(options) {
    var input = makeEl();
    var results = makeEl();
    // как настоящий innerHTML: присваивание очищает дочерние элементы
    var html = '';
    Object.defineProperty(results, 'innerHTML', {
        get: function () {
            return html;
        },
        set: function (value) {
            html = value;
            results.children = [];
        }
    });
    var timers = [];
    var opened = [];
    var userSearches = [];
    var pendingSearches = [];
    var chats = (options && options.chats) || [];
    var BF = {
        api: {
            searchUsers: function (query) {
                userSearches.push(query);
                return new Promise(function (resolve) {
                    pendingSearches.push({ query: query, resolve: resolve });
                });
            },
            getPersonChatId: function (userId) {
                return Promise.resolve({ chatId: 'chat-of-' + userId });
            }
        },
        i18n: {
            t: function (key) {
                return key;
            }
        },
        utils: {
            escapeHtml: function (value) {
                return String(value);
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            querySelector: function (selector) {
                return selector === '#searchInput' ? input : results;
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
        openChat: function (chatId) {
            opened.push(chatId);
        }
    });
    return {
        input: input,
        results: results,
        opened: opened,
        userSearches: userSearches,
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

    await test('nothing found shows a message only when both chats and users are empty', async function () {
        var h = createHarness();
        h.type('zzz');
        h.fireDebounce();
        h.answer(0, []);
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
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
