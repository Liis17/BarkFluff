const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class FakeElement {
    constructor(tagName) {
        this.tagName = tagName;
        this.children = [];
        this.listeners = new Map();
        this.attributes = {};
        this.parentElement = null;
        this.textContent = '';
        this.value = '';
        this.hidden = false;
        this.disabled = false;
    }

    get options() {
        return this.tagName === 'select' ? this.children : undefined;
    }

    appendChild(element) {
        element.parentElement = this;
        this.children.push(element);
        return element;
    }

    addEventListener(type, listener) {
        const listeners = this.listeners.get(type) || [];
        listeners.push(listener);
        this.listeners.set(type, listeners);
    }

    dispatchEvent(event) {
        for (const listener of this.listeners.get(event.type) || []) listener(event);
        return true;
    }

    click() {
        this.dispatchEvent({ type: 'click' });
    }

    setAttribute(name, value) {
        this.attributes[name] = value;
    }

    showModal() {
        this.open = true;
    }

    close() {
        this.open = false;
    }

    remove() {
        if (this.parentElement) {
            this.parentElement.children = this.parentElement.children.filter((child) => child !== this);
        }
    }
}

function descendants(element) {
    return element.children.flatMap((child) => [child, ...descendants(child)]);
}

function loadAuthUi(createAttempt) {
    const body = new FakeElement('body');
    const window = {
        BF: { i18n: { t: (key) => key }, confirmations: { create: createAttempt } },
        addEventListener() {}
    };
    const context = {
        AbortController,
        Array,
        Date,
        DOMException,
        Map,
        Promise,
        Set,
        clearInterval() {},
        document: { body, createElement: (tag) => new FakeElement(tag) },
        setInterval: () => 1,
        window
    };
    context.BF = window.BF;
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/auth-confirmation.js'), 'utf8'), context);
    return { api: window.BF.authUI, body };
}

async function main() {
    const pending = [];
    let cancelCount = 0;
    let beginCount = 0;
    const attempt = {
        begin(_method, _type, fields) {
            if (beginCount++ === 0) return Promise.resolve(response(fields.factor, 1, true));
            return new Promise((resolve, reject) => pending.push({ factor: fields.factor, resolve, reject }));
        },
        cancel() {
            cancelCount += 1;
        },
        poll() {},
        async complete() {
            return { getErrorCode: () => '', getState: () => 1 };
        }
    };
    const { api, body } = loadAuthUi(() => attempt);
    const operation = api.run(
        'beginReauthentication',
        'BeginReauthenticationRequest',
        { factor: 3 },
        { switchFactor: true }
    );
    await new Promise((resolve) => setImmediate(resolve));

    const dialog = body.children[0];
    const [factor] = descendants(dialog).filter((element) => element.tagName === 'select');
    const status = descendants(dialog).find((element) => element.attributes.role === 'status');
    const error = descendants(dialog).find((element) => element.attributes.role === 'alert');
    factor.value = '1';
    factor.dispatchEvent({ type: 'change' });
    factor.value = '3';
    factor.dispatchEvent({ type: 'change' });
    assert.deepEqual(
        pending.map((request) => request.factor),
        [1, 3]
    );

    pending[0].reject(new Error('stale begin failed'));
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(factor.value, '3');
    assert.equal(status.textContent, 'common.loadingShort');
    assert.equal(error.textContent, '');

    const cancel = descendants(dialog).find(
        (element) => element.tagName === 'button' && element.textContent === 'common.cancel'
    );
    cancel.click();
    await assert.rejects(operation, (reason) => reason.name === 'AbortError');
    assert.equal(cancelCount, 1);
    console.log('PASS: stale factor-switch responses cannot alter the active authentication dialog');

    let completedCode = null;
    const emailAttempt = {
        begin() {
            return Promise.resolve(response(2, 1, true));
        },
        cancel() {},
        poll() {},
        async complete(code) {
            completedCode = code;
            return {
                getErrorCode: () => '',
                getState: () => 5,
                getRecoveryCodesList: () => []
            };
        }
    };
    const emailUi = loadAuthUi(() => emailAttempt);
    const emailOperation = emailUi.api.run(
        'beginEmailBinding',
        'BeginEmailBindingRequest',
        { email: 'person@example.test' }
    );
    await new Promise((resolve) => setImmediate(resolve));

    const emailDialog = emailUi.body.children[0];
    const emailCode = descendants(emailDialog).find((element) => element.tagName === 'input');
    const emailConfirm = descendants(emailDialog).find(
        (element) => element.tagName === 'button' && element.textContent === 'common.confirm'
    );
    emailCode.value = '123456';
    assert.equal(emailConfirm.hidden, false);
    assert.equal(emailConfirm.disabled, false);
    emailConfirm.click();
    assert.equal(completedCode, '123456', 'email confirmation click must submit the entered code');
    await emailOperation;
    console.log('PASS: clicking Confirm submits the email verification code');

    const telegramAttempt = {
        begin() {
            return Promise.resolve(response(3, 1, false));
        },
        cancel() {},
        poll() {},
        async complete() {
            throw new Error('Telegram approval should not complete from the browser');
        }
    };
    const telegramUi = loadAuthUi(() => telegramAttempt);
    const telegramOperation = telegramUi.api.run(
        'beginSignIn',
        'BeginSignInRequest',
        { loginMode: 2 },
        { telegramLogin: true }
    );
    await new Promise((resolve) => setImmediate(resolve));

    const telegramDialog = telegramUi.body.children[0];
    const telegramStatus = descendants(telegramDialog).find(
        (element) => element.attributes.role === 'status'
    );
    const resendApproval = descendants(telegramDialog).find(
        (element) => element.tagName === 'button' && element.textContent === 'security.resendApproval'
    );
    assert.equal(telegramStatus.textContent, 'security.telegramLoginWaiting');
    assert.equal(resendApproval.hidden, false);
    const telegramCancel = descendants(telegramDialog).find(
        (element) => element.tagName === 'button' && element.textContent === 'common.cancel'
    );
    telegramCancel.click();
    await assert.rejects(telegramOperation, (reason) => reason.name === 'AbortError');
    console.log('PASS: Telegram button login explains the account mode and labels resend as an approval request');

    let pollsAfterMismatch = 0;
    const mismatchAttempt = {
        begin() {
            return Promise.resolve({ getErrorCode: () => 'login_mode_disabled' });
        },
        cancel() {},
        poll() {
            pollsAfterMismatch += 1;
        }
    };
    const mismatchUi = loadAuthUi(() => mismatchAttempt);
    const mismatchOperation = mismatchUi.api.run('beginSignIn', 'BeginSignInRequest', { loginMode: 1 });
    await new Promise((resolve) => setImmediate(resolve));
    const mismatchDialog = mismatchUi.body.children[0];
    const mismatchError = descendants(mismatchDialog).find((element) => element.attributes.role === 'alert');
    const mismatchStatus = descendants(mismatchDialog).find((element) => element.attributes.role === 'status');
    assert.equal(mismatchError.textContent, 'auth.error.loginModeDisabled');
    assert.equal(mismatchStatus.textContent, '');
    assert.equal(pollsAfterMismatch, 0, 'a rejected sign-in mode has no challenge to poll');
    descendants(mismatchDialog).find(
        (element) => element.tagName === 'button' && element.textContent === 'common.cancel'
    ).click();
    await assert.rejects(mismatchOperation, (reason) => reason.name === 'AbortError');
    console.log('PASS: a disabled sign-in mode shows a localized error without polling a missing challenge');
}

function response(factor, state, needsCode) {
    return {
        getState: () => state,
        getNeedsCode: () => needsCode,
        getFactor: () => factor,
        getAvailableFactorsList: () => [1, 3],
        getTelegramUrl: () => '',
        getErrorCode: () => ''
    };
}

main().catch((error) => {
    console.error('FAIL: ' + error.stack);
    process.exitCode = 1;
});
