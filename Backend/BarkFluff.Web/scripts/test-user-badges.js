const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function makeEl(tag) {
    return {
        tagName: tag,
        className: '',
        dataset: {},
        children: [],
        get firstElementChild() {
            return this.children[0] || null;
        },
        replaceChildren: function () {
            this.children = Array.from(arguments);
        }
    };
}

function createHarness() {
    var requests = [];
    var pending = [];
    var slots = [];
    var now = 1000;
    var BF = {
        api: {
            getUserBadges: function (userId) {
                requests.push(userId);
                return new Promise(function (resolve, reject) {
                    pending.push({ resolve: resolve, reject: reject });
                });
            }
        },
        i18n: { t: String, current: () => 'en' },
        utils: { escapeHtml: String }
    };
    var context = vm.createContext({
        BF: BF,
        Promise: Promise,
        Date: { now: () => now },
        document: {
            createElement: makeEl,
            querySelectorAll: function (selector) {
                var match = /data-badge-user="(\d+)"/.exec(selector);
                return slots.filter((slot) => match && slot.dataset.badgeUser === match[1]);
            }
        }
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/user-badges.js'), 'utf8'), context);
    return {
        badges: BF.userBadges,
        requests: requests,
        slots: slots,
        answer: (index, badges) => pending[index].resolve({ badges: badges }),
        fail: (index) => pending[index].reject(new Error('unimplemented')),
        advance: (ms) => (now += ms)
    };
}

const primary = { id: 7, name: 'Core', imageUrl: 'https://b/7.png', miniImageUrl: 'https://b/7m.png', isPrimary: true };
const other = { id: 8, name: 'Other', imageUrl: 'https://b/8.png', miniImageUrl: '', isPrimary: false };

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('slots of one user share a single request and are filled with the primary mini image', async function () {
        var h = createHarness();
        h.slots.push(h.badges.createSlot(5));
        assert.equal(h.badges.slotHtml(5), '<span class="user-badge-slot" data-badge-user="5"></span>');
        assert.deepEqual(h.requests, [5], 'the second slot reuses the request in flight');
        h.answer(0, [other, primary]);
        await settle();
        var img = h.slots[0].firstElementChild;
        assert.equal(img.className, 'user-badge-mini');
        assert.equal(img.src, 'https://b/7m.png');
        assert.equal(img.alt, 'Core');
        assert.match(h.badges.slotHtml(5), /<img class="user-badge-mini" data-badge-id="7" src="https:\/\/b\/7m\.png"/);
        assert.deepEqual(h.requests, [5], 'a cached user is not requested again');
    });

    await test('a user without a primary badge gets an empty slot', async function () {
        var h = createHarness();
        h.slots.push(h.badges.createSlot(6));
        h.answer(0, [other]);
        await settle();
        assert.equal(h.slots[0].children.length, 0);
        assert.equal(h.badges.primary(6), null);
    });

    await test('a changed choice updates the slots already on screen', async function () {
        var h = createHarness();
        h.slots.push(h.badges.createSlot(5));
        h.answer(0, [primary]);
        await settle();
        h.badges.set(5, [Object.assign({}, primary, { isPrimary: false })]);
        assert.equal(h.slots[0].children.length, 0, 'clearing the primary removes the icon');
        h.badges.set(5, [Object.assign({}, other, { isPrimary: true })]);
        assert.equal(h.slots[0].firstElementChild.src, 'https://b/8.png', 'without a mini image the full one is used');
    });

    await test('a failed request is not repeated on every render, only after a pause', async function () {
        var h = createHarness();
        h.badges.slotHtml(9);
        h.fail(0);
        await settle();
        h.badges.slotHtml(9);
        assert.deepEqual(h.requests, [9]);
        h.advance(61 * 1000);
        h.badges.slotHtml(9);
        assert.deepEqual(h.requests, [9, 9]);
    });

    await test('a forced load refreshes a cached list', async function () {
        var h = createHarness();
        h.badges.load(5);
        h.answer(0, [other]);
        await settle();
        var fresh = h.badges.load(5, true);
        assert.deepEqual(h.requests, [5, 5]);
        h.answer(1, [primary]);
        assert.deepEqual((await fresh).map((b) => b.id), [7]);
        assert.equal(h.badges.primary(5).id, 7);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
