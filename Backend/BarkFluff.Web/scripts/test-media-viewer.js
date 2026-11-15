const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const wwwroot = path.join(__dirname, '..', 'wwwroot');

function makeEl(id) {
    var classes = new Set();
    return {
        id: id,
        hidden: false,
        style: {},
        classList: {
            add: function (name) {
                classes.add(name);
            },
            remove: function (name) {
                classes.delete(name);
            },
            contains: function (name) {
                return classes.has(name);
            }
        },
        setAttribute: function () {},
        removeAttribute: function () {},
        addEventListener: function () {},
        play: function () {},
        pause: function () {}
    };
}

function createHarness() {
    var els = {};
    ['imageOverlay', 'overlayImage', 'overlayVideo', 'overlayPrev', 'overlayNext'].forEach(function (id) {
        els['#' + id] = makeEl(id);
    });
    var keydown = [];
    var closed = [];
    var BF = {
        files: {
            bindResilientMedia: function () {},
            refreshFileUrl: function () {
                return Promise.resolve(null);
            }
        },
        utils: {
            openOverlay: function (el) {
                el.classList.add('visible');
            },
            closeOverlay: function (el) {
                closed.push(el.id);
                el.classList.remove('visible');
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        window: null,
        document: {
            querySelector: function (selector) {
                return els[selector] || null;
            },
            addEventListener: function (type, callback, capture) {
                if (type === 'keydown') keydown.push({ callback: callback, capture: capture === true });
            }
        },
        requestAnimationFrame: function (callback) {
            callback();
            return 1;
        },
        cancelAnimationFrame: function () {},
        setTimeout: function () {
            return 1;
        },
        clearTimeout: function () {}
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(wwwroot, 'js', 'app', 'media-viewer.js'), 'utf8'), context);
    BF.mediaViewer.init({
        getCurrentChatId: function () {
            return null;
        }
    });

    function press(key) {
        var event = {
            key: key,
            prevented: false,
            stopped: false,
            preventDefault: function () {
                this.prevented = true;
            },
            stopPropagation: function () {
                this.stopped = true;
            }
        };
        // Capture-phase listeners run before bubble-phase ones; a stopped event never reaches bubble listeners.
        keydown
            .filter(function (listener) {
                return listener.capture;
            })
            .forEach(function (listener) {
                listener.callback(event);
            });
        if (!event.stopped)
            keydown
                .filter(function (listener) {
                    return !listener.capture;
                })
                .forEach(function (listener) {
                    listener.callback(event);
                });
        return event;
    }

    return { BF: BF, els: els, closed: closed, press: press };
}

(function escapeIgnoredWhileViewerHidden() {
    var harness = createHarness();
    var event = harness.press('Escape');
    assert.equal(event.stopped, false);
    assert.deepEqual(harness.closed, []);
})();

(function escapeClosesOnlyTheViewer() {
    var harness = createHarness();
    harness.BF.mediaViewer.show('video', 'https://example.test/video.mp4', null);
    assert.equal(harness.els['#imageOverlay'].classList.contains('visible'), true);

    var event = harness.press('Escape');

    assert.deepEqual(harness.closed, ['imageOverlay']);
    assert.equal(event.prevented, true);
    // Overlays underneath (pinned list, profile) must not receive the same Escape.
    assert.equal(event.stopped, true);
})();

(function overlayVideoIsFocusable() {
    // Without tabindex the overlay focus trap finds nothing to focus: Space and Tab do nothing.
    var html = fs.readFileSync(path.join(wwwroot, 'messenger.html'), 'utf8');
    var video = html.match(/<video id="overlayVideo"[^>]*>/);
    assert.ok(video, 'overlayVideo is missing from messenger.html');
    assert.match(video[0], /\stabindex="0"/);
})();

console.log('media viewer tests passed');
