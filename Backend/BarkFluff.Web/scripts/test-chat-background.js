const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function makeEl() {
    var html = '';
    var el = {
        className: '',
        textContent: '',
        disabled: false,
        children: [],
        listeners: {},
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
    var els = {
        '#chatBackgroundSelector': makeEl(),
        '#chatBackgroundSelectorTitle': makeEl(),
        '#chatBackgroundSelectorGrid': makeEl(),
        '#chatBackgroundSelectorClose': makeEl()
    };
    var log = { opened: [], closed: [], saved: [] };
    var personalization = (options && options.personalization) || {
        personalization: { chatBackgroundFileIds: ['bg1', 'bg2'] }
    };
    var BF = {
        api: {
            getPersonalization: function () {
                return options && options.failLoad ? Promise.reject(new Error('x')) : Promise.resolve(personalization);
            }
        },
        personalization: {
            getChatBackgroundFileId: function () {
                return (options && options.current) !== undefined ? options.current : '';
            },
            setChatBackgroundFileId: function (chatId, fileId) {
                log.saved.push([chatId, fileId]);
                return options && options.failSave ? Promise.reject(new Error('x')) : Promise.resolve();
            }
        },
        files: {
            bindResilientMedia: function () {},
            getFileUrls: function (ids) {
                return Promise.resolve([{ url: 'https://f/' + ids[0], previewUrl: 'https://p/' + ids[0] }]);
            }
        },
        i18n: {
            t: function (key, params) {
                return params ? key + ' ' + JSON.stringify(params) : key;
            }
        },
        utils: {
            escapeHtml: String,
            openOverlay: function (overlay) {
                log.opened.push(overlay);
            },
            closeOverlay: function (overlay) {
                log.closed.push(overlay);
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
            }
        },
        Promise: Promise
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/chat-background.js'), 'utf8'), context);
    BF.chatBackground.init();
    return { BF: BF, els: els, log: log, grid: els['#chatBackgroundSelectorGrid'] };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('opening shows a loading hint, opens the overlay and builds a global card plus one card per background', async function () {
        var h = createHarness();
        h.BF.chatBackground.open(7, 'Team');
        assert.equal(h.log.opened.length, 1);
        assert.match(h.grid.innerHTML, /common\.loadingShort/);
        assert.match(h.els['#chatBackgroundSelectorTitle'].textContent, /chat\.background\.for.*Team/);
        await settle();
        assert.equal(h.grid.children.length, 3);
        assert.match(h.grid.children[0].className, /none-card/);
        assert.equal(h.grid.children[0].textContent, 'chat.background.useGlobal');
        assert.equal(h.grid.children[1].children[0].src, 'https://p/bg1', 'the preview url is loaded into the image');
    });

    await test('the chat title falls back to a generic word; nothing happens without a chat id', async function () {
        var h = createHarness();
        h.BF.chatBackground.open(7, '');
        assert.match(h.els['#chatBackgroundSelectorTitle'].textContent, /common\.chat/);
        var none = createHarness();
        none.BF.chatBackground.open(null, 'x');
        assert.equal(none.log.opened.length, 0);
    });

    await test('the card of the current background is marked active', async function () {
        var h = createHarness({ current: 'bg2' });
        h.BF.chatBackground.open(7, 'Team');
        await settle();
        var active = h.grid.children.filter(function (card) {
            return / active/.test(card.className);
        });
        assert.equal(active.length, 1);
        assert.equal(active[0], h.grid.children[2]);

        var global = createHarness({ current: '' });
        global.BF.chatBackground.open(7, 'Team');
        await settle();
        assert.match(global.grid.children[0].className, / active/);
    });

    await test('picking a card saves it for this chat and closes the dialog', async function () {
        var h = createHarness();
        h.BF.chatBackground.open(7, 'Team');
        await settle();
        h.grid.children[1].listeners.click();
        assert.equal(h.grid.children[1].disabled, true, 'the card is locked while saving');
        await settle();
        assert.deepEqual(JSON.parse(JSON.stringify(h.log.saved)), [[7, 'bg1']]);
        assert.equal(h.log.closed.length, 1);

        h.grid.children[0].listeners.click(); // «как в общих настройках» — пустой fileId
        await settle();
        assert.deepEqual(JSON.parse(JSON.stringify(h.log.saved[1])), [7, '']);
    });

    await test('a failed save keeps the dialog open and unlocks the card', async function () {
        var h = createHarness({ failSave: true });
        h.BF.chatBackground.open(7, 'Team');
        await settle();
        h.grid.children[1].listeners.click();
        await settle();
        assert.equal(h.log.closed.length, 0);
        assert.equal(h.grid.children[1].disabled, false);
    });

    await test('a failed load shows an error hint', async function () {
        var h = createHarness({ failLoad: true });
        h.BF.chatBackground.open(7, 'Team');
        await settle();
        assert.match(h.grid.innerHTML, /sd-hint error.*chat\.background\.error/);
    });

    await test('the close button and a click on the backdrop close the dialog; a click inside does not', async function () {
        var h = createHarness();
        h.els['#chatBackgroundSelectorClose'].listeners.click();
        assert.equal(h.log.closed.length, 1);
        var overlay = h.els['#chatBackgroundSelector'];
        overlay.listeners.click({ target: overlay });
        assert.equal(h.log.closed.length, 2);
        overlay.listeners.click({ target: {} });
        assert.equal(h.log.closed.length, 2);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
