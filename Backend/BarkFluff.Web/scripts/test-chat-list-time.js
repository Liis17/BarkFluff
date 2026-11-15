const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadUtils() {
    const BF = {
        i18n: {
            current: function () { return 'ru'; },
            t: function (key) { return key; },
            tp: function (key) { return key; }
        },
        icons: { html: function () { return ''; } }
    };
    const context = { BF: BF, window: { BF: BF } };
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/utils.js'), 'utf8'),
        context
    );
    return context.window.BF.utils;
}

const utils = loadUtils();

function daysAgo(days, hours) {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), now.getDate() - days, hours, 30).toISOString();
}

assert.equal(utils.formatChatListTime(null), '');
assert.match(utils.formatChatListTime(daysAgo(0, 0)), /^\d{2}:\d{2}$/);
assert.equal(utils.formatChatListTime(daysAgo(1, 23)), 'date.yesterday');
for (const days of [2, 6]) {
    const ts = daysAgo(days, 12);
    assert.equal(
        utils.formatChatListTime(ts),
        new Date(ts).toLocaleDateString('ru', { weekday: 'short' })
    );
}
assert.match(utils.formatChatListTime(daysAgo(7, 12)), /^\d{2}\.\d{2}\.\d{2}$/);
assert.match(utils.formatChatListTime(daysAgo(400, 12)), /^\d{2}\.\d{2}\.\d{2}$/);

console.log('PASS: chat list time is short: time, yesterday, weekday, dd.mm.yy');
