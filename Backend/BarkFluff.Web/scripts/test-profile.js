const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function makeEl(id) {
    var html = '';
    var el = {
        id: id,
        textContent: '',
        className: '',
        hidden: false,
        style: {},
        dataset: {},
        children: [],
        listeners: {},
        inserted: [],
        addEventListener: function (type, callback) {
            this.listeners[type] = callback;
        },
        appendChild: function (child) {
            this.children.push(child);
            return child;
        },
        replaceChildren: function () {
            this.children = Array.from(arguments);
        },
        insertAdjacentHTML: function (where, markup) {
            this.inserted.push(markup);
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
    var ids = [
        'profileOverlay',
        'profileClose',
        'profilePoster',
        'profileAvatar',
        'profileName',
        'profileUsername',
        'profileStatus',
        'profileBio',
        'profileBadges',
        'profileRegDate',
        'profileMediaContent',
        'profileUserId',
        'profileChatId',
        'profileMsgBtn',
        'profileBackgroundButton'
    ];
    var els = {};
    ids.forEach(function (id) {
        els['#' + id] = makeEl(id);
    });
    var tab = makeEl('tab');
    tab.dataset.type = 'files';
    var copyBtn = makeEl('copyBtn');
    copyBtn.dataset.copy = 'profileUserId';
    var log = { opened: [], closed: [], media: [], background: [], toasts: [], callButtons: [], sounds: [], clipboard: [] };
    var users = {};
    var pendingUsers = [];
    var BF = {
        api: {
            getUser: function (userId) {
                return new Promise(function (resolve) {
                    pendingUsers.push({ userId: userId, resolve: resolve });
                });
            }
        },
        files: {
            loadResilientBackground: function () {}
        },
        i18n: {
            t: function (key) {
                return key;
            }
        },
        sound: {
            play: function (name) {
                log.sounds.push(name);
            }
        },
        utils: {
            openOverlay: function (overlay) {
                log.opened.push(overlay.id);
            },
            closeOverlay: function (overlay) {
                log.closed.push(overlay.id);
            },
            formatLastSeen: function (ts) {
                return 'lastSeen:' + ts;
            }
        },
        chatMedia: {
            createPanels: function () {
                return { panels: true };
            },
            setTabActive: function (selector, panels, type) {
                log.media.push(['tab', type]);
            },
            render: function (type) {
                log.media.push(['render', type]);
            }
        },
        chatBackground: {
            open: function (chatId, title) {
                log.background.push([chatId, title]);
            }
        }
    };
    var clipboardFails = options && options.clipboardFails;
    var context = vm.createContext({
        BF: BF,
        document: {
            querySelector: function (selector) {
                return els[selector] || null;
            },
            querySelectorAll: function (selector) {
                if (selector === '#profileOverlay .profile-media-tab') return [tab];
                if (selector === '.profile-info-copy') return [copyBtn];
                return [];
            },
            getElementById: function (id) {
                return els['#' + id] || null;
            },
            createElement: function () {
                return makeEl();
            },
            createTextNode: function (text) {
                return { text: text };
            }
        },
        navigator: {
            clipboard: {
                writeText: function (text) {
                    log.clipboard.push(text);
                    return clipboardFails ? Promise.reject(new Error('x')) : Promise.resolve();
                }
            }
        },
        Date: Date,
        Promise: Promise
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/profile.js'), 'utf8'), context);
    var onlineIds = (options && options.online) || [];
    var entries = (options && options.entries) || {};
    BF.profile.init({
        getCurrentChatId: function () {
            return 7;
        },
        getCurrentChatInfo: function () {
            return { title: 'Team' };
        },
        isUserOnline: function (id) {
            return onlineIds.indexOf(id) >= 0;
        },
        getOnlineEntry: function (id) {
            return entries[id];
        },
        botBadgeMarkup: function () {
            return '<span class="bot"></span>';
        },
        setCallButtonsVisible: function (visible) {
            log.callButtons.push(visible);
        },
        showToast: function (text, isError) {
            log.toasts.push([text, !!isError]);
        }
    });
    return {
        BF: BF,
        els: els,
        tab: tab,
        copyBtn: copyBtn,
        log: log,
        answer: function (index, user) {
            pendingUsers[index].resolve(user ? { user: user } : null);
        }
    };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('opening a profile fills the card, loads the media tab and opens the overlay', async function () {
        var h = createHarness({ online: [5] });
        h.BF.profile.open(5);
        h.answer(0, {
            id: 5,
            username: 'ann',
            firstName: 'Ann',
            lastName: 'Lee',
            bio: 'hello',
            registrationDate: 0,
            badges: [{ name: 'Early', imageUrl: 'https://b/1.png' }, { name: 'Plain' }]
        });
        await settle();
        assert.equal(h.els['#profileName'].textContent, 'Ann Lee');
        assert.equal(h.els['#profileUsername'].textContent, '@ann');
        assert.equal(h.els['#profileBio'].textContent, 'hello');
        assert.equal(h.els['#profileBio'].style.display, 'block');
        assert.equal(h.els['#profileAvatar'].textContent, 'A');
        assert.equal(h.els['#profileStatus'].textContent, 'status.online');
        assert.match(h.els['#profileStatus'].className, /online/);
        assert.equal(h.els['#profileUserId'].textContent, 5);
        assert.equal(h.els['#profileChatId'].textContent, 7);
        assert.equal(h.els['#profileBadges'].children.length, 2);
        assert.deepEqual(h.log.callButtons, [true]);
        assert.deepEqual(JSON.parse(JSON.stringify(h.log.media)), [['tab', 'media'], ['render', 'media']]);
        assert.deepEqual(h.log.opened, ['profileOverlay']);
    });

    await test('a bot profile shows the badge, hides the status and the call buttons', async function () {
        var h = createHarness();
        h.BF.profile.open(9);
        h.answer(0, { id: 9, username: 'helper_bot', isBot: true });
        await settle();
        assert.deepEqual(h.els['#profileName'].inserted, ['<span class="bot"></span>']);
        assert.equal(h.els['#profileStatus'].hidden, true);
        assert.deepEqual(h.log.callButtons, [false]);
        assert.equal(h.els['#profileBio'].style.display, 'none');
        assert.equal(h.els['#profileRegDate'].textContent, '—', 'no registration date');
    });

    await test('an offline user shows the last seen time', async function () {
        var h = createHarness({ entries: { 4: { lastSeen: 12345 } } });
        h.BF.profile.open(4);
        h.answer(0, { id: 4, username: 'bob' });
        await settle();
        assert.equal(h.els['#profileStatus'].textContent, 'lastSeen:12345');
        assert.doesNotMatch(h.els['#profileStatus'].className, /online/);

        var unknown = createHarness(); // статус пользователя ещё не приходил
        unknown.BF.profile.open(6);
        unknown.answer(0, { id: 6, username: 'eve' });
        await settle();
        assert.equal(unknown.els['#profileStatus'].textContent, 'lastSeen:null');
    });

    await test('a slow response for a previously opened profile does not overwrite the current one', async function () {
        var h = createHarness();
        h.BF.profile.open(1);
        h.BF.profile.open(2);
        h.answer(1, { id: 2, username: 'second' });
        await settle();
        h.answer(0, { id: 1, username: 'first' });
        await settle();
        assert.equal(h.els['#profileName'].textContent, 'second');
        assert.equal(h.log.opened.length, 1, 'the overlay is opened once, for the latest profile');
    });

    await test('no overlay for a missing user or an empty id', async function () {
        var h = createHarness();
        h.BF.profile.open(0);
        h.BF.profile.open(8);
        h.answer(0, null);
        await settle();
        assert.equal(h.log.opened.length, 0);
    });

    await test('close button, backdrop and the message button close the panel; a click inside does not', async function () {
        var h = createHarness();
        h.els['#profileClose'].listeners.click();
        h.els['#profileOverlay'].listeners.click({ target: h.els['#profileOverlay'] });
        h.els['#profileMsgBtn'].listeners.click();
        assert.deepEqual(h.log.closed, ['profileOverlay', 'profileOverlay', 'profileOverlay']);
        h.els['#profileOverlay'].listeners.click({ target: {} });
        assert.equal(h.log.closed.length, 3);
    });

    await test('media tabs reload the shown tab; the background button opens the picker for the open chat', async function () {
        var h = createHarness();
        h.tab.listeners.click();
        assert.deepEqual(JSON.parse(JSON.stringify(h.log.media)), [['tab', 'files'], ['render', 'files']]);
        h.els['#profileBackgroundButton'].listeners.click();
        assert.deepEqual(JSON.parse(JSON.stringify(h.log.background)), [[7, 'Team']]);
    });

    await test('copy buttons copy the id with a confirmation, or show an error toast', async function () {
        var h = createHarness();
        h.els['#profileUserId'].textContent = '5';
        h.copyBtn.listeners.click();
        await settle();
        assert.deepEqual(h.log.clipboard, ['5']);
        assert.deepEqual(h.log.sounds, ['success']);
        assert.deepEqual(JSON.parse(JSON.stringify(h.log.toasts)), [['common.copied', false]]);

        var failing = createHarness({ clipboardFails: true });
        failing.els['#profileUserId'].textContent = '6';
        failing.copyBtn.listeners.click();
        await settle();
        assert.deepEqual(JSON.parse(JSON.stringify(failing.log.toasts)), [['error.copy', true]]);

        var empty = createHarness();
        empty.copyBtn.listeners.click(); // пустой текст — ничего не копируется
        await settle();
        assert.equal(empty.log.clipboard.length, 0);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
