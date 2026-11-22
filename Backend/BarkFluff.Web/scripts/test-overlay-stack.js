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
        this.listeners = {};
    }

    appendChild(child) {
        child.parentNode = this;
        this.children.push(child);
        return child;
    }

    addEventListener(type, listener) {
        this.listeners[type] = (this.listeners[type] || []).concat([listener]);
    }

    removeEventListener(type, listener) {
        this.listeners[type] = (this.listeners[type] || []).filter((item) => item !== listener);
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

// Таймеры управляются тестом: сработавший таймер запускается явно, без реального ожидания.
class FakeTimers {
    constructor() {
        this.pending = [];
    }

    setTimeout(fn) {
        const timer = { fn: fn, cleared: false };
        this.pending.push(timer);
        return timer;
    }

    clearTimeout(timer) {
        if (timer) timer.cleared = true;
    }

    runPending() {
        const due = this.pending.filter((timer) => !timer.cleared);
        this.pending = [];
        due.forEach((timer) => timer.fn());
    }
}

// Имитирует transitionend, всплывающий от source к target.
function fireTransitionEnd(target, propertyName, source) {
    (target.listeners.transitionend || []).slice().forEach((listener) => {
        listener({ target: source || target, propertyName: propertyName });
    });
}

function loadUtils(document, timers) {
    const BF = {
        i18n: {
            current: function () { return 'ru'; },
            t: function (key) { return key; },
            tp: function (key) { return key; }
        },
        icons: { html: function () { return ''; } }
    };
    const fakeTimers = timers || new FakeTimers();
    const context = {
        BF: BF,
        document: document,
        window: { BF: BF },
        setTimeout: fakeTimers.setTimeout.bind(fakeTimers),
        clearTimeout: fakeTimers.clearTimeout.bind(fakeTimers)
    };
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

function testClosingReleasesStackSynchronously() {
    const document = new FakeDocument();
    const utils = loadUtils(document, new FakeTimers());
    const app = createOverlay(document, 'app');
    const overlay = createOverlay(document, 'confirmOverlay');

    utils.openOverlay(overlay);
    utils.closeOverlay(overlay);

    assert.equal(overlay.classList.contains('visible'), false, 'closing hides the overlay for visibility checks at once');
    assert.equal(overlay.classList.contains('is-closing'), true, 'a closing overlay stays displayed while it animates out');
    assert.equal(overlay.inert, true, 'a closing overlay cannot take focus or clicks');
    assert.equal(app.inert, false, 'the background is restored synchronously');

    // Запись в стеке уже снята: повторное открытие не блокируется устаревшей записью.
    utils.openOverlay(overlay);
    assert.equal(overlay.classList.contains('visible'), true, 'reopening must not be blocked by a stale stack entry');
}

function testExitEndsOnOwnOpacityTransitionEnd() {
    const document = new FakeDocument();
    const utils = loadUtils(document, new FakeTimers());
    const overlay = createOverlay(document, 'profileOverlay');
    const panel = overlay.appendChild(new FakeElement('div'));

    utils.openOverlay(overlay);
    utils.closeOverlay(overlay);

    fireTransitionEnd(overlay, 'opacity', panel);
    fireTransitionEnd(overlay, 'transform', overlay);
    assert.equal(overlay.classList.contains('is-closing'), true, 'a child or non-opacity transition must not end the exit');

    fireTransitionEnd(overlay, 'opacity', overlay);
    assert.equal(overlay.classList.contains('is-closing'), false, 'the opacity transitionend of the overlay ends the exit');
    assert.equal(overlay.inert, false, 'a finished overlay is not inert');
}

function testExitEndsByFallbackTimerWithoutTransitionEnd() {
    const document = new FakeDocument();
    const timers = new FakeTimers();
    const utils = loadUtils(document, timers);
    const overlay = createOverlay(document, 'attachOverlay');

    utils.openOverlay(overlay);
    utils.closeOverlay(overlay);
    timers.runPending();

    assert.equal(overlay.classList.contains('is-closing'), false, 'the exit must not hang when no transitionend arrives');
}

function testReopenDuringExitCancelsHide() {
    const document = new FakeDocument();
    const timers = new FakeTimers();
    const utils = loadUtils(document, timers);
    const app = createOverlay(document, 'app');
    const overlay = createOverlay(document, 'settingsOverlay');

    utils.openOverlay(overlay);
    utils.closeOverlay(overlay);
    utils.openOverlay(overlay);

    assert.equal(overlay.classList.contains('visible'), true, 'reopening during the exit makes the overlay visible again');
    assert.equal(overlay.classList.contains('is-closing'), false, 'reopening cancels the pending exit');
    assert.equal(overlay.inert, false, 'the reopened overlay is interactive');
    assert.equal(app.inert, true, 'the background is blocked again by the reopened overlay');

    // Таймер и слушатель выхода сняты: позднее завершение не скрывает открытый оверлей.
    timers.runPending();
    fireTransitionEnd(overlay, 'opacity', overlay);
    assert.equal(overlay.classList.contains('visible'), true, 'a stale exit must not hide the reopened overlay');
    assert.equal(overlay.classList.contains('is-closing'), false);
}

function main() {
    testNestedOverlayIsInteractive('settingsOverlay', 'confirmOverlay');
    testNestedOverlayIsInteractive('profileOverlay', 'chatBackgroundSelector');
    testNestedOverlayIsInteractive('profileOverlay', 'imageOverlay');
    console.log('PASS: nested overlays leave the top overlay clickable and restore the background');
    testClosingReleasesStackSynchronously();
    testExitEndsOnOwnOpacityTransitionEnd();
    testExitEndsByFallbackTimerWithoutTransitionEnd();
    testReopenDuringExitCancelsHide();
    console.log('PASS: closing releases the stack at once; the exit ends by transitionend or fallback timer; reopen cancels it');
}

try {
    main();
} catch (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
}
