const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class FakeClassList {
    constructor() {
        this.values = new Set();
    }

    add(value) {
        this.values.add(value);
    }

    remove(value) {
        this.values.delete(value);
    }

    contains(value) {
        return this.values.has(value);
    }
}

class FakeElement {
    constructor(tagName) {
        this.tagName = tagName;
        this.children = [];
        this.parentNode = null;
        this.attributes = {};
        this.classList = new FakeClassList();
        this.inert = false;
    }

    appendChild(child) {
        child.parentNode = this;
        this.children.push(child);
        return child;
    }

    contains(target) {
        return this === target || this.children.some((child) => child.contains(target));
    }

    querySelectorAll() {
        return [];
    }

    getClientRects() {
        return [];
    }

    getAttribute(name) {
        return Object.prototype.hasOwnProperty.call(this.attributes, name) ? this.attributes[name] : null;
    }

    setAttribute(name, value) {
        this.attributes[name] = String(value);
    }

    removeAttribute(name) {
        delete this.attributes[name];
    }
}

class FakeDocument extends FakeElement {
    constructor() {
        super('#document');
        this.body = this.appendChild(new FakeElement('body'));
        this.activeElement = this.body;
    }

    addEventListener() {}

    removeEventListener() {}

    createElement(tagName) {
        return new FakeElement(tagName);
    }
}

function loadUtils(document) {
    const BF = {
        i18n: {
            current: function () { return 'ru'; },
            t: function (key) { return key; },
            tp: function (key) { return key; }
        },
        icons: { html: function () { return ''; } }
    };
    const context = { BF: BF, document: document, window: { BF: BF } };
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/utils.js'), 'utf8'),
        context
    );
    return context.window.BF.utils;
}

function createOverlay(document, id) {
    const overlay = new FakeElement('div');
    overlay.id = id;
    document.body.appendChild(overlay);
    return overlay;
}

function testNestedOverlayIsInteractive(parentId, childId) {
    const document = new FakeDocument();
    const utils = loadUtils(document);
    const app = createOverlay(document, 'app');
    const parent = createOverlay(document, parentId);
    const child = createOverlay(document, childId);

    utils.openOverlay(parent);
    assert.equal(child.inert, true, childId + ' should be blocked while its parent overlay is open');

    utils.openOverlay(child);
    assert.equal(child.inert, false, childId + ' should become interactive when opened');
    assert.equal(parent.inert, true, parentId + ' should remain blocked below the child overlay');

    utils.closeOverlay(child);
    assert.equal(parent.inert, false, parentId + ' should become interactive after child closes');
    utils.closeOverlay(parent);
    assert.equal(app.inert, false, 'the application should be restored after all overlays close');
}

function main() {
    testNestedOverlayIsInteractive('settingsOverlay', 'confirmOverlay');
    testNestedOverlayIsInteractive('profileOverlay', 'chatBackgroundSelector');
    testNestedOverlayIsInteractive('profileOverlay', 'imageOverlay');
    console.log('PASS: nested overlays leave the top overlay clickable and restore the background');
}

try {
    main();
} catch (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
}
