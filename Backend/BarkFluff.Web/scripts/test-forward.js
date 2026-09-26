const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 15; i++) await new Promise((resolve) => setImmediate(resolve));
}

function makeEl(id) {
    var names = new Set();
    var html = '';
    var el = {
        id: id,
        textContent: '',
        value: '',
        disabled: false,
        dataset: {},
        children: [],
        listeners: {},
        onclick: null,
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
        appendChild: function (child) {
            this.children.push(child);
            return child;
        }
    };
    Object.defineProperty(el, 'innerHTML', {
        get: function () {
            return html;
        },
        set: function (value) {
            html = value;
            el.children = [];
        }
    });
    return el;
}

function createHarness(options) {
    var els = {};
    ['forwardOverlay', 'forwardClose', 'forwardChatList', 'forwardComment', 'forwardSendBtn', 'forwardCounter'].forEach(
        function (id) {
            els['#' + id] = makeEl(id);
        }
    );
    els['#forwardSendBtn'].textContent = 'Send';
    var documentListeners = {};
    var log = { opened: [], closed: [], sent: [], toasts: [] };
    var failFor = (options && options.failFor) || [];
    var BF = {
        api: {
            sendMessage: function (request) {
                log.sent.push(request);
                return failFor.indexOf(request.chatId) >= 0
                    ? Promise.reject(new Error('x'))
                    : Promise.resolve({ message: {} });
            }
        },
        i18n: {
            t: function (key, params) {
                return params ? key + ' ' + JSON.stringify(params) : key;
            },
            tp: function (key, count) {
                return key + ':' + count;
            }
        },
        utils: {
            escapeHtml: String,
            openOverlay: function (overlay) {
                overlay.classList.add('visible');
                log.opened.push(overlay.id);
            },
            closeOverlay: function (overlay) {
                overlay.classList.remove('visible');
                log.closed.push(overlay.id);
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            querySelector: function (selector) {
                return els[selector] || null;
            },
            createElement: function () {
                return makeEl();
            },
            addEventListener: function (type, callback) {
                documentListeners[type] = callback;
            }
        },
        Set: Set,
        Promise: Promise
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/forward.js'), 'utf8'), context);
    var chats = [
        { id: 1, title: 'Ann' },
        { id: 2, title: 'Team', picture: 'https://p/2.png' },
        { id: 3 }
    ];
    BF.forward.init({
        getChats: function () {
            return chats;
        },
        showToast: function (text, isError) {
            log.toasts.push([text, !!isError]);
        }
    });
    return {
        BF: BF,
        els: els,
        log: log,
        list: els['#forwardChatList'],
        send: els['#forwardSendBtn'],
        counter: els['#forwardCounter'],
        pressKey: function (key) {
            documentListeners.keydown({ key: key });
        }
    };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('a plain message is forwarded as itself; forwarded blocks resolve to their originals in order', async function () {
        var h = createHarness();
        var resolve = h.BF.forward.resolveSourceIds;
        assert.deepEqual(Array.from(resolve({ content: { text: 'x' } }, 10)), [10]);
        assert.deepEqual(Array.from(resolve(null, 11)), [11]);
        var forwarded = {
            content: {
                attachments: [
                    { type: 'IMAGE' },
                    { type: 'FORWARDED_MESSAGE', forwardedMessage: { originalMessageId: 300, order: 2 } },
                    { type: 8, forwardedMessage: { originalMessageId: 100, order: 0 } },
                    { type: '8', forwardedMessage: { originalMessageId: 200, order: 1 } },
                    { type: 'FORWARDED_MESSAGE', forwardedMessage: { order: 3 } } // без оригинала — пропускается
                ]
            }
        };
        assert.deepEqual(Array.from(resolve(forwarded, 12)), [100, 200, 300]);
        assert.deepEqual(
            Array.from(resolve({ content: { attachments: [{ type: 'FORWARDED_MESSAGE', forwardedMessage: {} }] } }, 13)),
            [13],
            'falls back to the message itself when no original is known'
        );
    });

    await test('opening lists every chat, resets the previous selection and comment and disables sending', async function () {
        var h = createHarness();
        h.els['#forwardComment'].value = 'old comment';
        h.BF.forward.open({ content: {} }, 5);
        assert.equal(h.log.opened.length, 1);
        assert.equal(h.list.children.length, 3);
        assert.match(h.list.children[1].innerHTML, /<img src="https:\/\/p\/2\.png"/);
        assert.match(h.list.children[0].innerHTML, /fwd-avatar">A</);
        assert.match(h.list.children[2].innerHTML, /common\.chat/, 'a chat without a title gets a generic name');
        assert.equal(h.els['#forwardComment'].value, '');
        assert.equal(h.counter.textContent, 'forward.noChatsSelected');
        assert.equal(h.send.disabled, true);
    });

    await test('clicking chats toggles the selection, the counter and the send button', async function () {
        var h = createHarness();
        h.BF.forward.open({ content: {} }, 5);
        h.list.children[0].listeners.click();
        h.list.children[1].listeners.click();
        assert.equal(h.list.children[0].classList.contains('selected'), true);
        assert.equal(h.counter.textContent, 'forward.selected {"count":2}');
        assert.equal(h.send.disabled, false);
        h.list.children[0].listeners.click();
        assert.equal(h.list.children[0].classList.contains('selected'), false);
        assert.equal(h.counter.textContent, 'forward.selected {"count":1}');
        h.list.children[1].listeners.click();
        assert.equal(h.send.disabled, true);
    });

    await test('submitting forwards to every selected chat in order with the comment; a failure does not stop the rest', async function () {
        var h = createHarness({ failFor: [1] });
        h.BF.forward.open({ content: { attachments: [{ type: 8, forwardedMessage: { originalMessageId: 77 } }] } }, 5);
        h.els['#forwardComment'].value = '  see this  ';
        h.list.children[0].listeners.click();
        h.list.children[1].listeners.click();
        h.send.onclick();
        assert.equal(h.send.disabled, true, 'locked while sending');
        assert.equal(h.send.textContent, 'forward.sending');
        await settle();
        assert.deepEqual(JSON.parse(JSON.stringify(h.log.sent)), [
            { chatId: 1, text: 'see this', forwardedMessageIds: [77] },
            { chatId: 2, text: 'see this', forwardedMessageIds: [77] }
        ]);
        assert.equal(h.send.textContent, 'Send', 'the label is restored');
        assert.equal(h.log.closed.length, 1, 'the dialog closes when done');
        assert.deepEqual(JSON.parse(JSON.stringify(h.log.toasts)), [['forward.done:2', false]]);
    });

    await test('an empty comment is sent as null; nothing is sent without a selection', async function () {
        var h = createHarness();
        h.BF.forward.open({ content: {} }, 9);
        h.send.onclick(); // ничего не выбрано
        await settle();
        assert.equal(h.log.sent.length, 0);
        h.list.children[2].listeners.click();
        h.send.onclick();
        await settle();
        assert.deepEqual(JSON.parse(JSON.stringify(h.log.sent)), [{ chatId: 3, text: null, forwardedMessageIds: [9] }]);
    });

    await test('close button, backdrop and Escape close the dialog and drop the selection; a click inside does not', async function () {
        var h = createHarness();
        h.BF.forward.open({ content: {} }, 5);
        h.list.children[0].listeners.click();
        h.els['#forwardClose'].listeners.click();
        assert.equal(h.log.closed.length, 1);
        assert.equal(h.send.onclick, null);

        h.BF.forward.open({ content: {} }, 5);
        h.els['#forwardOverlay'].listeners.click({ target: {} });
        assert.equal(h.log.closed.length, 1, 'a click inside the dialog does not close it');
        h.els['#forwardOverlay'].listeners.click({ target: h.els['#forwardOverlay'] });
        assert.equal(h.log.closed.length, 2);

        h.pressKey('Escape'); // диалог уже закрыт — повторного закрытия нет
        assert.equal(h.log.closed.length, 2);
        h.BF.forward.open({ content: {} }, 5);
        h.pressKey('Enter');
        assert.equal(h.log.closed.length, 2);
        h.pressKey('Escape');
        assert.equal(h.log.closed.length, 3);
        // выбор сброшен: после повторного открытия счётчик пуст
        h.BF.forward.open({ content: {} }, 5);
        assert.equal(h.counter.textContent, 'forward.noChatsSelected');
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
