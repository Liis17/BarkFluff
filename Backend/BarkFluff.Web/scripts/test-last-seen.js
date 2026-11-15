const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadUtils(format) {
    var BF = {
        i18n: {
            current: function () {
                return 'en';
            },
            t: function (key, params) {
                return params ? key + ' ' + JSON.stringify(params) : key;
            },
            tp: function (key, count) {
                return key + ':' + count;
            }
        },
        icons: {
            html: function () {
                return '';
            }
        },
        personalization: {
            getLastSeenFormat: function () {
                return format;
            }
        }
    };
    var context = vm.createContext({ BF: BF, document: {}, window: { BF: BF } });
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/utils.js'), 'utf8'), context);
    return context.window.BF.utils;
}

function daysAgoAtNoon(days) {
    var d = new Date();
    d.setDate(d.getDate() - days);
    d.setHours(12, 0, 0, 0);
    return d;
}

function test(name, fn) {
    fn();
    console.log('PASS: ' + name);
}

test('relative format stays the default behaviour', function () {
    var utils = loadUtils('relative');
    assert.equal(utils.formatLastSeen(Date.now() - 5 * 60 * 1000), 'lastSeen.minutes:5');
    assert.equal(utils.formatLastSeen(null), 'lastSeen.offline');
});

test('exact format shows time for today and yesterday', function () {
    var utils = loadUtils('exact');
    assert.match(utils.formatLastSeen(Date.now()), /^lastSeen\.exactToday \{"time":"[^"]+"\}$/);
    assert.match(utils.formatLastSeen(daysAgoAtNoon(1).getTime()), /^lastSeen\.exactYesterday \{"time":"[^"]+"\}$/);
});

test('exact format shows date and time for older visits, with year only when it differs', function () {
    var utils = loadUtils('exact');
    var older = daysAgoAtNoon(3);
    var text = utils.formatLastSeen(older.getTime());
    assert.match(text, /^lastSeen\.exactDate \{"date":"[^"]+","time":"[^"]+"\}$/);
    assert.equal(text.indexOf(String(older.getFullYear())) >= 0, older.getFullYear() !== new Date().getFullYear());

    var lastYear = daysAgoAtNoon(400);
    assert.ok(utils.formatLastSeen(lastYear.getTime()).indexOf(String(lastYear.getFullYear())) >= 0);
    assert.equal(utils.formatLastSeen(null), 'lastSeen.offline');
});
