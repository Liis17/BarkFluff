const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class FakeElement {
    constructor(tagName, parentNode) {
        this.tagName = tagName;
        this.parentNode = parentNode || null;
        this.children = [];
        this.listeners = {};
        this.className = '';
        this.title = '';
        this.textContent = '';
        this.value = '';
        this.dataset = {};
        this.style = {};
        this.attributes = {};
        this.hidden = false;
        this.offsetTop = 0;
        this.scrollTop = 0;
        this.classList = {
            values: new Set(),
            add: (value) => this.classList.values.add(value),
            remove: (value) => this.classList.values.delete(value),
            contains: (value) => this.classList.values.has(value),
            toggle: (value, force) => {
                var shouldAdd = force === undefined ? !this.classList.values.has(value) : force;
                if (shouldAdd) this.classList.values.add(value);
                else this.classList.values.delete(value);
                return shouldAdd;
            }
        };
    }

    set innerHTML(value) {
        this.children = [];
        this._innerHTML = value;
    }

    setAttribute(name, value) {
        this.attributes[name] = String(value);
    }

    focus() {}

    appendChild(child) {
        child.parentNode = this;
        this.children.push(child);
        return child;
    }

    replaceChildren(...children) {
        this.children = [];
        children.forEach((child) => this.appendChild(child));
    }

    addEventListener(type, callback) {
        if (!this.listeners[type]) this.listeners[type] = [];
        this.listeners[type].push(callback);
    }

    dispatchEvent(event) {
        if (!event.target) event.target = this;
        event.currentTarget = this;
        (this.listeners[event.type] || []).forEach((callback) => callback(event));
        if (!event.cancelBubble && this.parentNode) this.parentNode.dispatchEvent(event);
    }

    click() {
        var event = {
            type: 'click',
            target: this,
            cancelBubble: false,
            stopPropagation: function () {
                this.cancelBubble = true;
            }
        };
        this.dispatchEvent(event);
    }

    contains(target) {
        return this === target || this.children.some((child) => child.contains(target));
    }
}

class FakeDocument extends FakeElement {
    constructor() {
        super('#document');
        this.byId = {};
    }

    querySelector(selector) {
        return this.byId[selector.replace(/^#/, '')] || null;
    }

    createElement(tagName) {
        return new FakeElement(tagName);
    }
}

function createHarness(options) {
    options = options || {};
    var document = new FakeDocument();
    var stickerBtn = new FakeElement('button', document);
    var stickerPicker = new FakeElement('div', document);
    var stickerSearch = new FakeElement('input', stickerPicker);
    var linkForm = new FakeElement('form', stickerPicker);
    var linkInput = new FakeElement('input', linkForm);
    var stickerPacksBar = new FakeElement('div', stickerPicker);
    var stickerFeed = new FakeElement('div', stickerPicker);
    linkForm.hidden = true;
    linkForm.appendChild(linkInput);
    [stickerSearch, linkForm, stickerPacksBar, stickerFeed].forEach((el) => stickerPicker.appendChild(el));

    var myPacks = options.packs || [
        {
            pack: { id: 'pack-a', name: 'Pack A', coverStickerId: 'a-1' },
            stickers: [
                { id: 'a-1', fileId: 'file-a-1', emoji: '🐶' },
                { id: 'a-2', fileId: 'file-a-2', emoji: '🐱' }
            ]
        },
        {
            pack: { id: 'pack-b', name: 'Pack B', coverStickerId: 'b-1' },
            stickers: [{ id: 'b-1', fileId: 'file-b-1', emoji: '🦊' }]
        }
    ];
    var calls = { list: 0, openedByName: [] };
    var BF = {
        node: {
            key: function (key) {
                return key;
            }
        },
        api: {
            listMyStickerPacks: function () {
                calls.list++;
                return Promise.resolve(myPacks);
            }
        },
        files: {
            getFileUrls: function () {
                return Promise.resolve();
            },
            getCachedFileUrl: function (fileId) {
                return { url: 'https://files.test/' + fileId };
            },
            bindResilientMedia: function () {}
        },
        icons: {
            element: function () {
                return new FakeElement('span');
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
        },
        stickerPack: {
            openById: function () {},
            openByName: function (name) {
                calls.openedByName.push(name);
            }
        }
    };
    var stored = options.recent ? JSON.stringify(options.recent) : null;
    var localStorage = {
        getItem: function () {
            return stored;
        },
        setItem: function () {}
    };
    document.byId = {
        stickerBtn: stickerBtn,
        stickerPicker: stickerPicker,
        stickerSearch: stickerSearch,
        stickerPacksBar: stickerPacksBar,
        stickerFeed: stickerFeed,
        stickerLinkForm: linkForm,
        stickerLinkInput: linkInput
    };

    var context = vm.createContext({ BF: BF, document: document, localStorage: localStorage, Promise: Promise });
    context.window = context;
    var source = fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/sticker-picker.js'), 'utf8');
    vm.runInContext(source, context);
    var toasts = [];
    BF.stickerPicker.init({
        getMyUserId: function () {
            return 1;
        },
        getCurrentChatId: function () {
            return null;
        },
        getCurrentChatType: function () {
            return 0;
        },
        onSent: function () {},
        showToast: function (text) {
            toasts.push(text);
        }
    });

    return {
        BF: BF,
        calls: calls,
        toasts: toasts,
        picker: stickerPicker,
        packsBar: stickerPacksBar,
        feed: stickerFeed,
        search: stickerSearch,
        linkForm: linkForm,
        linkInput: linkInput,
        open: function () {
            stickerBtn.click();
        }
    };
}

function sectionIds(feed) {
    return feed.children.map((child) => child.dataset.sectionId);
}

function stickerCount(section) {
    return section.children[1].children.length;
}

async function settle() {
    for (var i = 0; i < 5; i++) await new Promise((resolve) => setImmediate(resolve));
}

var tests = [];
function test(name, fn) {
    tests.push({ name: name, fn: fn });
}

test('feed shows recent first and every added pack as its own section', async () => {
    var h = createHarness({ recent: ['b-1', 'gone'] });
    h.open();
    await settle();
    assert.deepEqual(sectionIds(h.feed), ['__recent__', 'pack-a', 'pack-b']);
    assert.equal(stickerCount(h.feed.children[0]), 1, 'stickers missing from added packs drop out of recent');
    assert.equal(stickerCount(h.feed.children[1]), 2);
    var tabs = h.packsBar.children;
    assert.equal(tabs.length, 4, 'recent + two packs + add-by-link');
    assert.equal(tabs[3].className.includes('add'), true);
});

test('tab click scrolls to its section and keeps the picker open', async () => {
    var h = createHarness();
    h.open();
    await settle();
    h.feed.children[1].offsetTop = 180;
    h.packsBar.children[1].click();
    assert.equal(h.feed.scrollTop, 180);
    assert.equal(h.packsBar.children[1].classList.contains('active'), true);
    assert.equal(h.picker.classList.contains('visible'), true);
});

test('emoji search filters across packs and hides empty sections', async () => {
    var h = createHarness();
    h.open();
    await settle();
    h.search.value = '🦊';
    h.search.dispatchEvent({ type: 'input' });
    await settle();
    assert.deepEqual(sectionIds(h.feed), ['pack-b']);
});

test('no added packs shows the empty state instead of server packs', async () => {
    var h = createHarness({ packs: [] });
    h.open();
    await settle();
    assert.equal(h.feed.children.length, 1);
    assert.equal(h.feed.children[0].className, 'sticker-empty-state');
    assert.equal(h.packsBar.children.length, 1, 'only the add-by-link tab');
});

test('pack list is cached until invalidated', async () => {
    var h = createHarness();
    h.open();
    await settle();
    h.open();
    h.open();
    await settle();
    assert.equal(h.calls.list, 1);
    h.BF.stickerPicker.invalidate();
    await settle();
    assert.equal(h.calls.list, 2, 'invalidate reloads while the picker is open');
});

test('sticker link parsing accepts links and bare short names', () => {
    var h = createHarness();
    var parse = h.BF.stickerPicker.parseStickerLink;
    assert.equal(parse('https://barkfluff.example/addstickers/Shark_Liis'), 'Shark_Liis');
    assert.equal(parse('  cats_pack '), 'cats_pack');
    assert.equal(parse('https://example.com/other/path'), null);
    assert.equal(parse('ab'), null);
});

test('submitting a link opens the pack window by short name', async () => {
    var h = createHarness();
    h.open();
    await settle();
    h.packsBar.children[h.packsBar.children.length - 1].click();
    assert.equal(h.linkForm.hidden, false);
    h.linkInput.value = 'https://node.test/addstickers/cats';
    h.linkForm.dispatchEvent({ type: 'submit', preventDefault: function () {} });
    assert.deepEqual(h.calls.openedByName, ['cats']);
    assert.equal(h.picker.classList.contains('visible'), false);
});

async function main() {
    var failed = 0;
    for (var t of tests) {
        try {
            await t.fn();
            console.log('PASS: ' + t.name);
        } catch (error) {
            failed++;
            console.error('FAIL: ' + t.name + ' — ' + error.message);
        }
    }
    if (failed > 0) process.exitCode = 1;
}

main();
