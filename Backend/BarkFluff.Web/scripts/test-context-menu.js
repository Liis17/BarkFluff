const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function createMenu(document, specs) {
    var items = specs.map(function (spec) {
        return {
            name: spec.name,
            disabled: !!spec.disabled,
            offsetWidth: spec.hidden ? 0 : 100,
            offsetHeight: spec.hidden ? 0 : 30,
            getClientRects: function () {
                return spec.hidden ? [] : [{}];
            },
            focus: function () {
                document.activeElement = this;
            }
        };
    });
    return {
        items: items,
        querySelectorAll: function (selector) {
            assert.equal(selector, '[role="menuitem"]');
            return items;
        }
    };
}

function loadUtils(document) {
    var BF = {
        i18n: {
            current: function () {
                return 'ru';
            },
            t: function (key) {
                return key;
            }
        },
        icons: {
            html: function () {
                return '';
            }
        }
    };
    var context = vm.createContext({ BF: BF, document: document, window: { BF: BF } });
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/utils.js'), 'utf8'), context);
    return context.window.BF.utils;
}

function keydown(key) {
    return {
        key: key,
        defaultPrevented: false,
        propagationStopped: false,
        preventDefault: function () {
            this.defaultPrevented = true;
        },
        stopPropagation: function () {
            this.propagationStopped = true;
        }
    };
}

function test(name, fn) {
    fn();
    console.log('PASS: ' + name);
}

var document = { activeElement: null };
var utils = loadUtils(document);
var menu = createMenu(document, [
    { name: 'reply' },
    { name: 'copy-image', hidden: true },
    { name: 'forward' },
    { name: 'pin', disabled: true },
    { name: 'delete' }
]);
var focused = function () {
    return document.activeElement && document.activeElement.name;
};

test('focusMenuItem skips hidden and disabled items and wraps around', function () {
    utils.focusMenuItem(menu, 0);
    assert.equal(focused(), 'reply');
    utils.focusMenuItem(menu, -1);
    assert.equal(focused(), 'delete');
    utils.focusMenuItem(menu, 3);
    assert.equal(focused(), 'reply');
});

test('arrow keys cycle through visible items, Home/End jump to the edges', function () {
    utils.focusMenuItem(menu, 0);
    var e = keydown('ArrowDown');
    assert.equal(utils.handleMenuKeydown(e, menu, function () {}), true);
    assert.equal(focused(), 'forward');
    assert.equal(e.defaultPrevented, true);
    assert.equal(e.propagationStopped, true);
    utils.handleMenuKeydown(keydown('ArrowDown'), menu, function () {});
    assert.equal(focused(), 'delete');
    utils.handleMenuKeydown(keydown('ArrowDown'), menu, function () {});
    assert.equal(focused(), 'reply', 'ArrowDown wraps to the first item');
    utils.handleMenuKeydown(keydown('ArrowUp'), menu, function () {});
    assert.equal(focused(), 'delete', 'ArrowUp wraps to the last item');
    utils.handleMenuKeydown(keydown('Home'), menu, function () {});
    assert.equal(focused(), 'reply');
    utils.handleMenuKeydown(keydown('End'), menu, function () {});
    assert.equal(focused(), 'delete');
});

test('ArrowUp with focus outside the menu goes to the last item', function () {
    document.activeElement = null;
    utils.handleMenuKeydown(keydown('ArrowUp'), menu, function () {});
    assert.equal(focused(), 'delete');
});

test('Escape and Tab dismiss the menu; other keys are left to the browser', function () {
    var reasons = [];
    var dismiss = function (reason) {
        reasons.push(reason);
    };
    var esc = keydown('Escape');
    assert.equal(utils.handleMenuKeydown(esc, menu, dismiss), true);
    assert.equal(esc.defaultPrevented, true);
    utils.handleMenuKeydown(keydown('Tab'), menu, dismiss);
    assert.deepEqual(reasons, ['escape', 'tab']);
    var enter = keydown('Enter');
    assert.equal(utils.handleMenuKeydown(enter, menu, dismiss), false);
    assert.equal(enter.defaultPrevented, false, 'Enter keeps native button activation');
});
