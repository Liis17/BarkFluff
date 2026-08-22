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
    }

    createElement(tagName) {
        return new FakeElement(tagName);
    }
}

function createHarness() {
    var document = new FakeDocument();
    var stickerBtn = new FakeElement('button', document);
    var stickerPicker = new FakeElement('div', document);
    var stickerSearch = new FakeElement('input', stickerPicker);
    var stickerPacksBar = new FakeElement('div', stickerPicker);
    var stickerGrid = new FakeElement('div', stickerPicker);
    stickerPicker.appendChild(stickerSearch);
    stickerPicker.appendChild(stickerPacksBar);
    stickerPicker.appendChild(stickerGrid);

    var packs = [
        { id: 'pack-a', name: 'Pack A', coverStickerId: 'a-1' },
        { id: 'pack-b', name: 'Pack B', coverStickerId: 'b-1' }
    ];
    var packStickers = {
        'pack-a': [{ id: 'a-1', fileId: 'file-a-1', emoji: '🐶' }, { id: 'a-2', fileId: 'file-a-2', emoji: '🐱' }],
        'pack-b': [{ id: 'b-1', fileId: 'file-b-1', emoji: '🦊' }]
    };
    var BF = {
        node: { key: function (key) { return key; } },
        api: {
            listStickerPacks: function () { return Promise.resolve({ packs: packs }); },
            getStickerPack: function (packId) { return Promise.resolve({ stickers: packStickers[packId] }); }
        },
        files: {
            getFileUrls: function () { return Promise.resolve(); },
            getCachedFileUrl: function (fileId) { return { url: 'https://files.test/' + fileId }; },
            bindResilientMedia: function () {}
        },
        icons: { element: function () { return new FakeElement('span'); } },
        i18n: { t: function (key) { return key; } }
    };
    var localStorage = { getItem: function () { return null; }, setItem: function () {} };
    var u = { escapeHtml: function (value) { return String(value); } };
    var source = fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/main.js'), 'utf8');
    var start = source.indexOf('    // ========== STICKER PICKER ==========');
    var end = source.indexOf('    // ========== REPLY / FORWARD / CONTEXT MENU ==========');
    assert.notEqual(start, -1, 'sticker picker section should exist');
    assert.notEqual(end, -1, 'sticker picker section end should exist');
    var stickerSection = source.slice(start, end);
    var factory = vm.runInNewContext(
        '(function (BF, document, localStorage, stickerBtn, stickerPicker, stickerSearch, stickerPacksBar, stickerGrid, myUserId, u) {' +
            stickerSection +
            ' return { loadStickerPacks: loadStickerPacks }; })',
        { Set: Set, Map: Map, Promise: Promise, document: document }
    );

    return {
        picker: stickerPicker,
        packsBar: stickerPacksBar,
        grid: stickerGrid,
        loadStickerPacks: function () {
            return factory(BF, document, localStorage, stickerBtn, stickerPicker, stickerSearch, stickerPacksBar, stickerGrid, 1, u)
                .loadStickerPacks();
        }
    };
}

async function settle() {
    for (var i = 0; i < 5; i++) await new Promise((resolve) => setImmediate(resolve));
}

async function main() {
    var harness = createHarness();
    harness.picker.classList.add('visible');
    await harness.loadStickerPacks();
    await settle();

    assert.equal(harness.grid.children.length, 2, 'first pack should render only its own stickers');
    harness.packsBar.children[1].click();
    await settle();

    var failures = [];
    try {
        assert.equal(harness.grid.children.length, 1, 'switching packs should replace the sticker grid');
    } catch (error) {
        failures.push(error.message);
    }
    try {
        assert.equal(harness.picker.classList.contains('visible'), true, 'switching packs should keep the picker open');
    } catch (error) {
        failures.push(error.message);
    }
    if (failures.length > 0) throw new Error(failures.join('; '));
    console.log('PASS: sticker packs switch without mixing stickers or closing the picker');
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
