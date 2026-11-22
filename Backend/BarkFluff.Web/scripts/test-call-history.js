const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function makeEl(dataset) {
    var classes = [];
    var el = {
        hidden: false,
        disabled: false,
        textContent: '',
        className: '',
        dataset: dataset || {},
        children: [],
        listeners: {},
        attributes: {},
        classList: {
            toggle: function (name, on) {
                classes = classes.filter(function (c) {
                    return c !== name;
                });
                if (on) classes.push(name);
            },
            contains: function (name) {
                return classes.indexOf(name) >= 0;
            }
        },
        setAttribute: function (name, value) {
            this.attributes[name] = value;
        },
        addEventListener: function (type, callback) {
            this.listeners[type] = callback;
        },
        appendChild: function (child) {
            this.children.push(child);
            return child;
        },
        querySelector: function () {
            // единственный запрос модуля к строке — элемент имени
            if (!this.nameEl) this.nameEl = makeEl();
            return this.nameEl;
        }
    };
    Object.defineProperty(el, 'innerHTML', {
        get: function () {
            return el._html || '';
        },
        set: function (value) {
            el._html = value;
            el.children = [];
        }
    });
    return el;
}

function createHarness(options) {
    var settings = options || {};
    var allBtn = makeEl({ filter: 'all' });
    var missedBtn = makeEl({ filter: 'missed' });
    var overlay = makeEl();
    overlay.querySelectorAll = function () {
        return [allBtn, missedBtn];
    };
    var elements = {
        callHistoryOverlay: overlay,
        callHistoryList: makeEl(),
        callHistoryMore: makeEl(),
        callHistoryClose: makeEl()
    };
    var requests = [];
    var opened = [];
    var overlayState = { open: false };
    var BF = {
        api: {
            listCallHistory: function (missedOnly, before, limit) {
                return new Promise(function (resolve, reject) {
                    requests.push({ missedOnly: missedOnly, before: before, limit: limit, resolve: resolve, reject: reject });
                });
            },
            getPersonChatId: function (userId) {
                return Promise.resolve({ chatId: 'dm-' + userId });
            }
        },
        i18n: {
            t: function (key) {
                return key;
            }
        },
        utils: {
            escapeHtml: String,
            formatChatListTime: function () {
                return '12:00';
            },
            isRestrictedUser: function (user) {
                return Number(user.accountStatus) > 0;
            },
            restrictedUserName: function () {
                return 'accountRestriction.userDeleted';
            },
            openOverlay: function () {
                overlayState.open = true;
            },
            closeOverlay: function () {
                overlayState.open = false;
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            getElementById: function (id) {
                return elements[id];
            },
            createElement: function () {
                return makeEl();
            }
        },
        Promise: Promise
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/call-history.js'), 'utf8'), context);
    BF.callHistory.init({
        getChats: function () {
            return settings.chats || [];
        },
        getUser: function (userId) {
            return Promise.resolve((settings.users || {})[userId] || null);
        },
        openChat: function (chatId) {
            opened.push(chatId);
        },
        showToast: function () {}
    });
    return {
        BF: BF,
        list: elements.callHistoryList,
        more: elements.callHistoryMore,
        missedBtn: missedBtn,
        requests: requests,
        opened: opened,
        overlayState: overlayState
    };
}

function call(overrides) {
    return Object.assign(
        {
            callId: 'c',
            chatId: '',
            peerUserId: 0,
            isGroup: false,
            mediaType: 1,
            direction: 2,
            endReason: 1,
            startedAt: 1000,
            answeredAt: 1001,
            durationSeconds: 75
        },
        overrides
    );
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('opening loads all calls; names, direction, duration and missed state are rendered', async function () {
        var h = createHarness({ chats: [{ id: 'g1', title: 'Team' }], users: { 5: { firstName: 'Ann', lastName: 'Lee' } } });
        h.BF.callHistory.open();
        assert.equal(h.overlayState.open, true);
        assert.equal(h.requests[0].missedOnly, false);
        assert.equal(h.requests[0].before, null);
        h.requests[0].resolve({
            items: [
                call({ isGroup: true, chatId: 'g1', mediaType: 2 }),
                call({ peerUserId: 5, direction: 1, answeredAt: null, endReason: 3 })
            ],
            hasMore: false
        });
        await settle();
        var rows = h.list.children;
        assert.equal(rows.length, 2);
        assert.equal(rows[0].nameEl.textContent, 'Team');
        assert.match(rows[0].innerHTML, /callHistory\.outgoing · attachment\.video · 12:00 · 1:15/);
        assert.equal(rows[1].nameEl.textContent, 'Ann Lee');
        assert.match(rows[1].className, /missed/);
        assert.match(rows[1].innerHTML, /callHistory\.missedCall · attachment\.audio · 12:00</);
        assert.equal(h.more.hidden, true);
    });

    await test('"show more" continues from the oldest loaded call', async function () {
        var h = createHarness();
        h.BF.callHistory.open();
        h.requests[0].resolve({ items: [call({ startedAt: 3000 }), call({ startedAt: 2000 })], hasMore: true });
        await settle();
        assert.equal(h.more.hidden, false);
        h.more.listeners.click();
        assert.equal(h.requests[1].before, 2000);
        h.requests[1].resolve({ items: [call({ startedAt: 1000 })], hasMore: false });
        await settle();
        assert.equal(h.list.children.length, 3);
        assert.equal(h.more.hidden, true);
    });

    await test('switching to missed reloads and drops the response of the previous filter', async function () {
        var h = createHarness();
        h.BF.callHistory.open();
        h.missedBtn.listeners.click();
        assert.equal(h.requests[1].missedOnly, true);
        assert.equal(h.missedBtn.classList.contains('active'), true);
        h.requests[1].resolve({ items: [], hasMore: false });
        await settle();
        h.requests[0].resolve({ items: [call()], hasMore: true });
        await settle();
        assert.equal(h.list.children.length, 1, 'only the empty state');
        assert.equal(h.list.children[0].className, 'call-history-empty');
    });

    await test('the first page shows skeleton rows until it arrives, then they are replaced by the calls', async function () {
        var h = createHarness();
        h.BF.callHistory.open();
        assert.match(h.list.innerHTML, /call-history-skeleton/);
        assert.equal(h.list.children.length, 0);
        h.requests[0].resolve({ items: [call({ peerUserId: 5 })], hasMore: false });
        await settle();
        assert.doesNotMatch(h.list.innerHTML, /skeleton/);
        assert.equal(h.list.children.length, 1);
    });

    await test('a failed first page removes the skeleton instead of leaving it on screen', async function () {
        var h = createHarness();
        h.BF.callHistory.open();
        h.requests[0].reject(new Error('network'));
        await settle();
        assert.doesNotMatch(h.list.innerHTML, /skeleton/);
        assert.equal(h.list.children.length, 1);
        assert.equal(h.list.children[0].className, 'call-history-empty');
    });

    await test('clicking a call opens its chat and closes the history', async function () {
        var h = createHarness();
        h.BF.callHistory.open();
        h.requests[0].resolve({ items: [call({ isGroup: true, chatId: 'g1' }), call({ peerUserId: 9 })], hasMore: false });
        await settle();
        h.list.children[1].listeners.click();
        await settle();
        assert.deepEqual(h.opened, ['dm-9']);
        assert.equal(h.overlayState.open, false);
        h.BF.callHistory.open();
        h.requests[1].resolve({ items: [call({ isGroup: true, chatId: 'g1' })], hasMore: false });
        await settle();
        h.list.children[0].listeners.click();
        assert.deepEqual(h.opened, ['dm-9', 'g1']);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
