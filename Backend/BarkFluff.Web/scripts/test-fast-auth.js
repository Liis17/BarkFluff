const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function element() {
    const classes = new Set();
    return {
        textContent: '',
        dataset: {},
        classList: {
            add: (value) => classes.add(value),
            remove: (...values) => values.forEach((value) => classes.delete(value))
        },
        setAttribute(name, value) { this[name] = value; },
        getAttribute(name) { return this[name] || null; }
    };
}

async function settle() {
    for (let i = 0; i < 5; i++) await new Promise(setImmediate);
}

async function testRestrictedQrError(errorCode, expectedMessage) {
    const elements = Object.fromEntries(['fastAuthCard', 'fastAuthImg', 'fastAuthSpinner', 'fastAuthStatus'].map((id) => [id, element()]));
    let clearCount = 0;
    const scheduledTimeouts = [];
    const codes = {
        ACCOUNT_BLOCKED: 'F34D8C22-C075-4E9C-92AD-C893F192D8FA',
        ACCOUNT_PENDING_DELETION: '8E6F0F30-98AF-4F96-A63B-20D52AEEFB6A'
    };
    const BF = {
        auth: { ERROR_CODES: codes },
        i18n: { t: (key) => key },
        tokens: { clear() { clearCount++; } },
        metadata: { build: () => ({}) },
        node: { origin: () => 'https://node.test' },
        network: {
            POLICIES: { MUTATION: {} },
            unary: () => Promise.reject({ metadata: { 'x-error-code': errorCode } })
        }
    };
    const context = vm.createContext({
        BF,
        document: { getElementById: (id) => elements[id] || null },
        setInterval: () => 1,
        clearInterval() {},
        setTimeout(callback) { scheduledTimeouts.push(callback); return scheduledTimeouts.length; },
        clearTimeout() {},
        Promise,
        Date,
        Math,
        window: {
            BF,
            barkfluff: { FastAuthApiClient: function () { return { generateFastAuthToken() {} }; } },
            proto: { barkfluff: { fast: { auth: {
                GenerateFastAuthTokenRequest: function () { this.setFormat = function () {}; },
                TokenFormat: { TOKEN_FORMAT_QR: 0 }
            } } } },
            addEventListener() {}
        }
    });
    context.window.window = context.window;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/fast-auth.js'), 'utf8'), context);

    BF.fastAuth.start();
    await settle();

    assert.equal(elements.fastAuthStatus.textContent, expectedMessage);
    assert.equal(clearCount, 1, 'restricted QR errors clear any stored session');
    assert.equal(scheduledTimeouts.length, 0, 'restricted QR errors stop the retry loop');
}

async function main() {
    await testRestrictedQrError('F34D8C22-C075-4E9C-92AD-C893F192D8FA', 'accountRestriction.rpcBlocked');
    await testRestrictedQrError('8E6F0F30-98AF-4F96-A63B-20D52AEEFB6A', 'accountRestriction.rpcPendingDeletion');
    console.log('PASS: restricted QR errors show localized guidance, clear the session, and stop automatic retries');
}

main().catch((error) => {
    console.error('FAIL: ' + error.stack);
    process.exitCode = 1;
});
