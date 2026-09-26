const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Разметка как в messages.js: data-msg-id стоит на .msg-group, у .msg-bubble его нет.
function makeGroup(id, top, className) {
    var group = {
        classes: ['msg-group'].concat(className ? [className] : []),
        dataset: { msgId: String(id) },
        classList: {
            contains: function (name) {
                return group.classes.indexOf(name) >= 0;
            }
        },
        getBoundingClientRect: function () {
            return { top: top, bottom: top + 40 };
        }
    };
    group.bubble = {
        classes: ['msg-bubble'],
        dataset: {},
        classList: {
            contains: function (name) {
                return name === 'msg-bubble';
            }
        },
        getBoundingClientRect: group.getBoundingClientRect
    };
    return group;
}

function createHarness(groups, messages) {
    var listeners = {};
    var timers = [];
    var marked = [];
    var area = {
        addEventListener: function (type, callback) {
            listeners[type] = callback;
        },
        getBoundingClientRect: function () {
            return { top: 0, bottom: 500 };
        },
        // Тот же селектор, что использует код: сопоставляем по классу элемента.
        querySelectorAll: function (selector) {
            var cls = selector.replace(/^\./, '');
            var found = [];
            groups.forEach(function (group) {
                if (group.classes.indexOf(cls) >= 0) found.push(group);
                if (group.bubble.classes.indexOf(cls) >= 0) found.push(group.bubble);
            });
            return found;
        }
    };
    var BF = {
        api: {
            markAsRead: function (ids) {
                marked.push(Array.from(ids)); // массив из vm-контекста иначе не равен внешнему по прототипу
                return Promise.resolve();
            }
        },
        i18n: {
            t: function (key) {
                return key;
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            querySelector: function () {
                return area;
            }
        },
        Set: Set,
        Promise: Promise,
        setTimeout: function (callback, ms) {
            timers.push({ callback: callback, ms: ms, active: true });
            return timers.length;
        },
        clearTimeout: function (id) {
            if (timers[id - 1]) timers[id - 1].active = false;
        }
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/mark-read.js'), 'utf8'), context);
    var state = { chatId: 7, chatType: 0 };
    BF.markRead.init({
        getCurrentChatId: function () {
            return state.chatId;
        },
        getCurrentChatType: function () {
            return state.chatType;
        },
        getMessages: function () {
            return messages;
        },
        getMyUserId: function () {
            return 1;
        },
        showToast: function () {}
    });
    return {
        BF: BF,
        state: state,
        marked: marked,
        scroll: function () {
            listeners.scroll();
        },
        // Прокручиваем все ожидающие таймеры (порядок: троттлинг скролла → отправка пачки).
        flush: function () {
            for (var guard = 0; guard < 10; guard++) {
                var pending = timers.filter(function (t) {
                    return t.active;
                });
                if (pending.length === 0) return;
                pending.forEach(function (t) {
                    t.active = false;
                    t.callback();
                });
            }
        }
    };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('scrolling marks unread incoming messages that are in view (data-msg-id is on .msg-group)', async function () {
        var messages = [
            { id: 10, senderId: 2, readBy: [] },
            { id: 11, senderId: 2, readBy: [] },
            { id: 12, senderId: 2, readBy: [] }
        ];
        var groups = [makeGroup(10, 100), makeGroup(11, 200), makeGroup(12, 900)]; // 12 — за пределами экрана
        var h = createHarness(groups, messages);
        h.scroll();
        h.flush();
        await Promise.resolve();
        assert.deepEqual(h.marked, [[10, 11]]);
    });

    await test('own, already read, system, pending and private-chat messages are not marked', async function () {
        var messages = [
            { id: 20, senderId: 1, readBy: [] }, // своё
            { id: 21, senderId: 2, readBy: [1] }, // уже прочитано
            { id: 22, senderId: 0, readBy: [] } // системная плашка
        ];
        var groups = [
            makeGroup(20, 10),
            makeGroup(21, 60),
            makeGroup(22, 110, 'msg-system'),
            makeGroup('pending-send-abc', 160)
        ];
        var h = createHarness(groups, messages);
        h.scroll();
        h.flush();
        await Promise.resolve();
        assert.deepEqual(h.marked, []);

        var unread = createHarness([makeGroup(30, 10)], [{ id: 30, senderId: 2, readBy: [] }]);
        unread.state.chatType = 1; // приватный чат: отметка чтения идёт своим путём
        unread.scroll();
        unread.flush();
        await Promise.resolve();
        assert.deepEqual(unread.marked, []);
    });

    await test('schedule() marks all unread incoming messages of the open chat after a second', async function () {
        var messages = [
            { id: 40, senderId: 2, readBy: [] },
            { id: 41, senderId: 1, readBy: [] },
            { id: 42, senderId: 2, readBy: [1] },
            { id: 43, senderId: 3, readBy: [2] }
        ];
        var h = createHarness([], messages);
        h.BF.markRead.schedule();
        h.flush();
        await Promise.resolve();
        assert.deepEqual(h.marked, [[40, 43]]);
    });

    await test('markSoon() batches ids and a repeated call restarts the timer', async function () {
        var h = createHarness([], []);
        h.BF.markRead.markSoon(50);
        h.BF.markRead.markSoon([51, 52]);
        h.flush();
        await Promise.resolve();
        assert.deepEqual(h.marked, [[50, 51, 52]]);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
