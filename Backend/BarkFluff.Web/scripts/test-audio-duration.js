const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadUtils() {
    const BF = {
        i18n: {
            current: function () { return 'en'; },
            t: function (key) { return key; },
            tp: function (key) { return key; }
        },
        icons: { html: function () { return ''; } }
    };
    const context = { BF: BF, window: { BF: BF } };
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/utils.js'), 'utf8'), context);
    return context.window.BF.utils;
}

const utils = loadUtils();

// webm из MediaRecorder: до полного проигрывания браузер отдаёт duration = Infinity.
assert.equal(utils.formatDuration(Infinity), '0:00');
assert.equal(utils.formatDuration(NaN), '0:00');
assert.equal(utils.formatDuration(0), '0:00');
assert.equal(utils.formatDuration(65.4), '1:05');

console.log('PASS: audio duration never renders Infinity/NaN');
