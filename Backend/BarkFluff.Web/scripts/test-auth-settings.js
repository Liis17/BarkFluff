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
        this._textContent = '';
        this.value = '';
        this.hidden = false;
        this.disabled = false;
        this.checked = false;
        this.className = '';
    }

    get textContent() {
        return this._textContent + this.children.map((child) => child.textContent).join('');
    }

    set textContent(value) {
        this._textContent = String(value || '');
        this.replaceChildren();
    }

    get options() {
        return this.tagName === 'select' ? this.children : undefined;
    }

    appendChild(element) {
        element.parentElement = this;
        this.children.push(element);
        return element;
    }

    replaceChildren(...elements) {
        this.children.forEach((child) => {
            child.parentElement = null;
        });
        this.children = [];
        elements.forEach((element) => this.appendChild(element));
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
        if (!this.disabled) this.dispatchEvent({ type: 'click', target: this });
    }

    setAttribute(name, value) {
        this.attributes[name] = String(value);
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
            this.parentElement = null;
        }
    }
}

function descendants(element) {
    return element.children.flatMap((child) => [child, ...descendants(child)]);
}

function byClass(element, className) {
    return descendants(element).find((item) => item.className === className);
}

function buttonByText(element, text) {
    return descendants(element).find((item) => item.tagName === 'button' && item.textContent === text);
}

function inputByType(element, type) {
    return descendants(element).find((item) => item.tagName === 'input' && item.type === type);
}

function toggleByLabel(element, key) {
    return descendants(element).find(
        (item) => item.tagName === 'label' && item.className === 'auth-security-toggle' && item.textContent.includes(key)
    )?.children.find((item) => item.tagName === 'input');
}

function modeRadio(element, value) {
    return descendants(element).find(
        (item) => item.tagName === 'input' && item.type === 'radio' && item.value === String(value)
    );
}

function chooseMode(root, value) {
    [1, 2, 3].forEach((mode) => {
        modeRadio(root, mode).checked = mode === value;
    });
    modeRadio(root, value).dispatchEvent({ type: 'change' });
}

async function flush() {
    await new Promise((resolve) => setImmediate(resolve));
    await new Promise((resolve) => setImmediate(resolve));
}

function createFixture(overrides = {}, capabilityOverrides = {}) {
    let current = {
        loginMode: 1,
        preferredFactor: 1,
        authenticatorEnabled: true,
        emailEnabled: true,
        telegramLinked: true,
        telegramEnabled: true,
        telegramOtpEnabled: true,
        fastAuthTelegramEnabled: true,
        telegramUsername: 'fluff_bot_user',
        verifiedEmail: 'person@example.test',
        remainingRecoveryCodes: 5,
        ...overrides
    };
    const capabilities = {
        getEmailAvailable: () => true,
        getTelegramAvailable: () => true,
        ...capabilityOverrides
    };
    const calls = [];
    const updateRequests = [];
    const reauthenticationBegins = [];
    const body = new FakeElement('body');
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
        window: {
            BF: {
                i18n: {
                    t(key, params) {
                        return String(key).replace(/\{(\w+)\}/g, (_match, name) => params?.[name] ?? `{${name}}`);
                    }
                },
                node: { origin: () => 'https://node.example.test' },
                confirmations: {
                    async call(method, _type, request, _options) {
                        calls.push({ method, request });
                        if (method === 'getSecuritySettings') return { toObject: () => ({ ...current }) };
                        if (method === 'getAuthCapabilities') return capabilities;
                        if (method === 'updateSecuritySettings') {
                            updateRequests.push({ method, ...request });
                            current = {
                                ...current,
                                loginMode: request.loginMode,
                                preferredFactor: request.preferredFactor,
                                telegramEnabled: request.telegramEnabled,
                                telegramOtpEnabled: request.telegramOtpEnabled,
                                fastAuthTelegramEnabled: request.fastAuthTelegramEnabled
                            };
                            return { getRecoveryCodesList: () => [] };
                        }
                        if (method === 'unlinkTelegram') {
                            updateRequests.push({ method, ...request });
                            current = {
                                ...current,
                                loginMode: request.loginMode,
                                preferredFactor: request.preferredFactor,
                                telegramLinked: false,
                                telegramEnabled: false,
                                telegramOtpEnabled: false,
                                fastAuthTelegramEnabled: false
                            };
                            return { getRecoveryCodesList: () => [] };
                        }
                        if (method === 'generateRecoveryCodes') return { getCodesList: () => [] };
                        return {};
                    },
                    create() {
                        return {
                            begin(_method, _type, fields) {
                                reauthenticationBegins.push({ ...fields });
                                return Promise.resolve(challenge(fields.factor, 1));
                            },
                            cancel() {},
                            poll(update) {
                                update(challenge(1, 2));
                            },
                            async complete() {
                                return {
                                    getErrorCode: () => '',
                                    getState: () => 5,
                                    getSecurityProof: () => ({ id: 'proof-id', secret: 'proof-secret' }),
                                    getRecoveryCodesList: () => []
                                };
                            }
                        };
                    }
                }
            },
            addEventListener() {}
        }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/auth-confirmation.js'), 'utf8'),
        context
    );
    const container = new FakeElement('main');
    const cleanup = context.BF.authUI.renderSecurity(container);
    return {
        api: context.BF.authUI,
        body,
        calls,
        cleanup,
        container,
        reauthenticationBegins,
        updateRequests,
        current: () => current
    };
}

function challenge(factor, state) {
    return {
        getErrorCode: () => '',
        getState: () => state,
        getNeedsCode: () => false,
        getFactor: () => factor,
        getAvailableFactorsList: () => [1, 2, 3],
        getTelegramUrl: () => ''
    };
}

function openReauthenticationDialog(fixture) {
    return fixture.body.children.find(
        (item) => item.tagName === 'dialog' && item.open && descendants(item).some((child) => child.textContent === 'security.reauthenticate')
    );
}

async function confirmReauthentication(fixture) {
    const dialog = openReauthenticationDialog(fixture);
    assert.ok(dialog, 'security proof dialog should open');
    buttonByText(dialog, 'common.confirm').click();
    await flush();
}

async function testConditionalFactorAvailability() {
    const fixture = createFixture({ preferredFactor: 3 });
    await flush();
    const root = byClass(fixture.container, 'auth-security');
    const preferredLabel = byClass(root, 'auth-security-preferred');
    const master = toggleByLabel(root, 'security.telegramEnabled');
    const telegramOtp = toggleByLabel(root, 'security.telegramFactor');
    const qrConfirmation = toggleByLabel(root, 'security.fastAuthTelegram');
    const preferred = descendants(preferredLabel).find((item) => item.tagName === 'select');

    assert.equal(preferredLabel.hidden, true, 'preferred factor is hidden outside mode 3');
    assert.equal(telegramOtp.disabled, false);
    assert.equal(qrConfirmation.disabled, false);
    master.checked = false;
    master.dispatchEvent({ type: 'change' });
    assert.equal(telegramOtp.checked, true, 'turning off Telegram keeps the draft OTP flag');
    assert.equal(qrConfirmation.checked, true, 'turning off Telegram keeps the draft QR flag');
    assert.equal(telegramOtp.disabled, true, 'Telegram OTP cannot be edited while Telegram is off');
    assert.equal(qrConfirmation.disabled, true, 'QR confirmation cannot be edited while Telegram is off');

    chooseMode(root, 3);
    assert.equal(preferredLabel.hidden, false);
    assert.deepEqual(
        preferred.options.map((option) => option.value),
        ['', '1', '2'],
        'available preferred factors follow the current Telegram draft'
    );
    assert.equal(preferred.value, '', 'an unavailable preferred factor is not silently replaced');
    assert.equal(buttonByText(root, 'common.save').disabled, true);
    preferred.value = '2';
    preferred.dispatchEvent({ type: 'change' });
    assert.equal(buttonByText(root, 'common.save').disabled, false);
    assert.equal(fixture.updateRequests.length, 0);
    console.log('PASS: preferred factors and Telegram controls track the settings draft without rewriting hidden flags');
}

async function testInvalidModeNeedsSetup() {
    const fixture = createFixture({
        authenticatorEnabled: false,
        emailEnabled: false,
        telegramLinked: false,
        telegramEnabled: false,
        telegramOtpEnabled: false,
        fastAuthTelegramEnabled: false,
        verifiedEmail: 'person@example.test'
    });
    await flush();
    const root = byClass(fixture.container, 'auth-security');
    chooseMode(root, 3);
    assert.equal(modeRadio(root, 3).disabled, false, 'mode 3 can be selected before a factor is configured');
    assert.equal(modeRadio(root, 2).disabled, true, 'passwordless Telegram mode needs a configured Telegram link');
    assert.ok(descendants(root).some((item) => item.textContent === 'security.prerequisite.telegramLogin'));
    assert.equal(buttonByText(root, 'common.save').disabled, true, 'mode 3 cannot be saved without an available factor');
    assert.ok(descendants(root).some((item) => item.textContent === 'security.prerequisite.secondFactor'));
    assert.equal(buttonByText(root, 'security.setupAuthenticator').hidden, false, 'a factor setup action is available');
    console.log('PASS: mode 3 stays selectable before setup and cannot be saved without an enabled factor');
}

async function testSaveUsesPersistedProofAndDraftSnapshot() {
    const fixture = createFixture({ loginMode: 1, preferredFactor: 1 });
    await flush();
    const root = byClass(fixture.container, 'auth-security');
    chooseMode(root, 3);
    const preferred = descendants(byClass(root, 'auth-security-preferred')).find((item) => item.tagName === 'select');
    preferred.value = '2';
    preferred.dispatchEvent({ type: 'change' });
    buttonByText(root, 'common.save').click();
    await flush();

    const proofDialog = openReauthenticationDialog(fixture);
    assert.ok(proofDialog, 'saving requires reauthentication');
    const password = inputByType(proofDialog, 'password');
    assert.ok(password, 'the proof form follows the persisted password sign-in mode');
    assert.equal(password.parentElement.hidden, false);
    await confirmReauthentication(fixture);
    await flush();

    assert.equal(fixture.reauthenticationBegins.length, 1);
    assert.equal(fixture.updateRequests.length, 1);
    assert.equal(fixture.updateRequests[0].loginMode, 3, 'the update uses the current draft mode');
    assert.equal(fixture.updateRequests[0].preferredFactor, 2, 'the update uses the current draft factor');
    assert.equal(fixture.updateRequests[0].securityProof.secret, 'proof-secret');
    console.log('PASS: security updates prove the persisted mode and atomically submit the current draft snapshot');
}

async function testCancelledProofPreservesDraft() {
    const fixture = createFixture({ loginMode: 1 });
    await flush();
    const root = byClass(fixture.container, 'auth-security');
    chooseMode(root, 3);
    const preferred = descendants(byClass(root, 'auth-security-preferred')).find((item) => item.tagName === 'select');
    preferred.value = '2';
    preferred.dispatchEvent({ type: 'change' });
    buttonByText(root, 'common.save').click();
    await flush();

    const proofDialog = openReauthenticationDialog(fixture);
    assert.equal(modeRadio(byClass(fixture.container, 'auth-security'), 3).disabled, true, 'settings controls are disabled during proof');
    buttonByText(proofDialog, 'common.cancel').click();
    await flush();

    assert.equal(modeRadio(byClass(fixture.container, 'auth-security'), 3).checked, true);
    assert.equal(modeRadio(byClass(fixture.container, 'auth-security'), 3).disabled, false, 'computed enabled state is restored after cancellation');
    assert.equal(buttonByText(byClass(fixture.container, 'auth-security'), 'common.save').disabled, false);
    assert.equal(fixture.updateRequests.length, 0, 'cancelling proof does not save the draft');
    console.log('PASS: cancelling reauthentication preserves the edited settings draft');
}

async function testPersistedLastFactorCannotBeDisabledEarly() {
    const fixture = createFixture({
        loginMode: 3,
        preferredFactor: 1,
        authenticatorEnabled: true,
        emailEnabled: false,
        telegramLinked: false,
        telegramEnabled: false,
        telegramOtpEnabled: false,
        fastAuthTelegramEnabled: false,
        verifiedEmail: 'person@example.test'
    });
    await flush();
    const root = byClass(fixture.container, 'auth-security');
    chooseMode(root, 1);
    const disable = buttonByText(root, 'common.disable');
    assert.equal(disable.disabled, true, 'the server still sees the persisted password + factor mode');
    assert.ok(descendants(root).some((item) => item.textContent === 'security.prerequisite.onlyFactorDisable'));
    buttonByText(root, 'common.save').click();
    await flush();
    await confirmReauthentication(fixture);
    await flush();
    const refreshed = byClass(fixture.container, 'auth-security');
    assert.equal(buttonByText(refreshed, 'common.disable').disabled, false, 'factor removal is available after the mode change is saved');
    console.log('PASS: the only persisted second factor stays protected until the new sign-in method is saved');
}

async function testPersistedPreferredFactorCannotBeDisabledBeforePreferenceSave() {
    const fixture = createFixture({
        loginMode: 3,
        preferredFactor: 1,
        authenticatorEnabled: true,
        emailEnabled: true,
        telegramLinked: false,
        telegramEnabled: false,
        telegramOtpEnabled: false,
        fastAuthTelegramEnabled: false,
        verifiedEmail: 'person@example.test'
    });
    await flush();
    let root = byClass(fixture.container, 'auth-security');
    const disableAuthenticator = buttonByText(root, 'common.disable');
    assert.equal(disableAuthenticator.disabled, true, 'the persisted preferred factor cannot be removed yet');
    assert.ok(descendants(root).some((item) => item.textContent === 'security.prerequisite.switchPreferredBeforeDisable'));

    const preferred = descendants(byClass(root, 'auth-security-preferred')).find((item) => item.tagName === 'select');
    preferred.value = '2';
    preferred.dispatchEvent({ type: 'change' });
    assert.equal(disableAuthenticator.disabled, true, 'an unsaved preference change does not affect persisted server policy');
    buttonByText(root, 'common.save').click();
    await flush();
    await confirmReauthentication(fixture);
    await flush();

    root = byClass(fixture.container, 'auth-security');
    assert.equal(buttonByText(root, 'common.disable').disabled, false, 'the old preferred factor can be disabled after the preference is saved');
    console.log('PASS: the persisted preferred factor stays protected until its replacement is saved');
}

async function testUnlinkRequiresSafeRemainingMode() {
    const fixture = createFixture({
        loginMode: 2,
        preferredFactor: 3,
        authenticatorEnabled: false,
        emailEnabled: false,
        telegramLinked: true,
        telegramEnabled: true,
        telegramOtpEnabled: true,
        fastAuthTelegramEnabled: true,
        verifiedEmail: 'person@example.test'
    });
    await flush();
    const root = byClass(fixture.container, 'auth-security');
    const unlink = buttonByText(root, 'security.unlinkTelegram');
    assert.equal(unlink.disabled, true, 'passwordless Telegram mode cannot remain after unlinking');
    assert.ok(descendants(root).some((item) => item.textContent === 'security.prerequisite.unlinkMode'));
    chooseMode(root, 1);
    assert.equal(unlink.disabled, false, 'unlink becomes available after selecting a safe remaining sign-in method');
    assert.equal(fixture.updateRequests.length, 0, 'choosing a residual mode does not implicitly save it');
    console.log('PASS: Telegram unlink is gated until an explicit safe remaining sign-in method is selected');
}

async function testCancelledSettingsLoadDoesNotRenderStaleResponse() {
    let finishLoad;
    const body = new FakeElement('body');
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
        window: {
            BF: {
                i18n: { t: (key) => key },
                node: { origin: () => 'https://node.example.test' },
                confirmations: {
                    call(method) {
                        if (method === 'getSecuritySettings')
                            return new Promise((resolve) => {
                                finishLoad = resolve;
                            });
                        return Promise.resolve({ getEmailAvailable: () => true, getTelegramAvailable: () => true });
                    }
                }
            },
            addEventListener() {}
        }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/auth-confirmation.js'), 'utf8'),
        context
    );
    const container = new FakeElement('main');
    const cleanup = context.BF.authUI.renderSecurity(container);
    cleanup();
    finishLoad({ toObject: () => ({ loginMode: 1 }) });
    await flush();
    assert.equal(container.children.length, 0, 'a response arriving after settings close is ignored');
    console.log('PASS: closing settings prevents an in-flight response from rendering stale security controls');
}

async function main() {
    await testConditionalFactorAvailability();
    await testInvalidModeNeedsSetup();
    await testSaveUsesPersistedProofAndDraftSnapshot();
    await testCancelledProofPreservesDraft();
    await testPersistedLastFactorCannotBeDisabledEarly();
    await testPersistedPreferredFactorCannotBeDisabledBeforePreferenceSave();
    await testUnlinkRequiresSafeRemainingMode();
    await testCancelledSettingsLoadDoesNotRenderStaleResponse();
}

main().catch((error) => {
    console.error('FAIL: ' + error.stack);
    process.exitCode = 1;
});
