const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class FakeClassList {
    constructor() { this.values = new Set(); }
    add(value) { this.values.add(value); }
    remove(value) { this.values.delete(value); }
    contains(value) { return this.values.has(value); }
    toggle(value, force) {
        const enabled = force === undefined ? !this.values.has(value) : !!force;
        if (enabled) this.values.add(value);
        else this.values.delete(value);
        return enabled;
    }
}

class FakeElement {
    constructor(id, tagName) {
        this.id = id || '';
        this.tagName = (tagName || 'div').toUpperCase();
        this.children = [];
        this.listeners = {};
        this.attributes = {};
        this.classList = new FakeClassList();
        this.hidden = false;
        this.disabled = false;
        this.checked = false;
        this.readOnly = false;
        this.value = '';
        this.textContent = '';
        this.dataset = {};
        this.style = {};
        this.options = this.children;
    }
    get firstChild() { return this.children[0] || null; }
    appendChild(child) {
        child.parentElement = this;
        this.children.push(child);
        if (this.tagName === 'SELECT' && this.children.length === 1 && !this.value) this.value = child.value;
        return child;
    }
    removeChild(child) {
        this.children.splice(this.children.indexOf(child), 1);
        if (child.parentElement === this) child.parentElement = null;
        return child;
    }
    querySelector(selector) {
        if (selector === '.btn-label') return this.children.find((child) => child.classList.contains('btn-label')) || null;
        if (selector === '.btn-spinner') return this.children.find((child) => child.classList.contains('btn-spinner')) || null;
        return null;
    }
    addEventListener(type, callback) {
        (this.listeners[type] ||= []).push(callback);
    }
    dispatch(type, event) {
        const value = Object.assign({ type, target: this, key: '', preventDefault() { this.defaultPrevented = true; } }, event || {});
        for (const callback of this.listeners[type] || []) callback.call(this, value);
        return value;
    }
    click() {
        if (!this.disabled && !this.hidden) this.dispatch('click');
    }
    focus() { this.focused = true; }
    setAttribute(name, value) { this.attributes[name] = String(value); }
    getAttribute(name) { return this.attributes[name] || null; }
}

function deferred() {
    let resolve;
    let reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
}

function challenge(options) {
    options = options || {};
    return {
        getErrorCode: () => options.error || '',
        getState: () => options.state === undefined ? 1 : options.state,
        getFactor: () => options.factor || 0,
        getNeedsCode: () => !!options.needsCode,
        getAvailableFactorsList: () => options.available || [],
        getTelegramUrl: () => options.telegramUrl || ''
    };
}

function completed(options) {
    options = options || {};
    return {
        getErrorCode: () => options.error || '',
        getState: () => options.state === undefined ? 5 : options.state,
        getSession: () => options.session === false ? null : { token: 'session' }
    };
}

function responseMode(mode, error) {
    return { getLoginMode: () => mode, getErrorCode: () => error || '' };
}

function makeHarness(options) {
    options = options || {};
    const ids = [
        'loginUpgrade', 'loginUpgradeMessage', 'loginUpgradeBtn', 'loginForm', 'loginInput', 'loginError', 'passwordGroup', 'passwordInput', 'passwordError',
        'signInBtn', 'changeLoginBtn', 'factorGroup', 'loginFactor', 'recoveryGroup', 'loginRecovery',
        'challengeGroup', 'challengeStatus', 'codeGroup', 'confirmationCode', 'confirmationCodeLabel',
        'confirmationError', 'confirmCodeBtn', 'challengeRetryBtn', 'challengeResendBtn', 'tempLoginCheck',
        'toRegisterBtn', 'legalAcceptCheck', 'legalConsentRow', 'fastAuthCard', 'fastAuthStatus',
        'nodeBar', 'nodeBarName', 'nodeChangeBtn', 'recoverPassword'
    ];
    const elements = Object.fromEntries(ids.map((id) => [id, new FakeElement(id, id === 'loginForm' ? 'form' : 'div')]));
    for (const buttonId of ['signInBtn', 'confirmCodeBtn']) {
        const button = elements[buttonId] || new FakeElement(buttonId, 'button');
        elements[buttonId] = button;
        const label = new FakeElement('', 'span');
        label.classList.add('btn-label');
        button.appendChild(label);
        const spinner = new FakeElement('', 'span');
        spinner.classList.add('btn-spinner');
        button.appendChild(spinner);
    }
    elements.confirmCodeBtn.tagName = 'BUTTON';
    elements.loginFactor.tagName = 'SELECT';
    elements.loginInput.tagName = 'INPUT';
    elements.passwordInput.tagName = 'INPUT';
    elements.confirmationCode.tagName = 'INPUT';
    elements.loginRecovery.tagName = 'INPUT';
    elements.legalAcceptCheck.tagName = 'INPUT';
    elements.tempLoginCheck.tagName = 'INPUT';
    const body = new FakeElement('body', 'body');
    const document = {
        readyState: 'complete',
        body,
        querySelector(selector) { return elements[selector.slice(1)] || null; },
        createElement(tagName) { return new FakeElement('', tagName); }
    };

    let origin = 'https://node-a.test';
    const callQueue = [];
    const beginQueue = [];
    const completeQueue = [];
    const resendQueue = [];
    const calls = [];
    const attempts = [];
    const tokens = { stored: null, saved: [], modes: [], cleared: 0 };
    const fastAuth = {
        started: false,
        generation: 0,
        starts: 0,
        cancels: 0,
        shown: 0,
        start() {
            if (this.started) return;
            this.started = true;
            this.generation++;
            this.starts++;
        },
        cancel() {
            this.started = false;
            this.generation++;
            this.cancels++;
        },
        completeGeneration(generation) {
            if (this.started && generation === this.generation) this.shown++;
        }
    };
    const timeouts = new Map();
    const intervals = new Map();
    let nextInterval = 0;
    const windowListeners = {};

    function queued(queue, fallback, call) {
        const value = queue.length ? queue.shift() : fallback;
        return typeof value === 'function' ? value(call) : value;
    }

    const context = {
        AbortController,
        DOMException,
        Date,
        Error,
        Math,
        Promise,
        document,
        localStorage: { getItem: () => null },
        setInterval(callback, delay) { const id = ++nextInterval; intervals.set(id, { callback, delay }); return id; },
        clearInterval(id) { intervals.delete(id); },
        setTimeout(callback) { const id = ++nextInterval; timeouts.set(id, callback); return id; },
        clearTimeout(id) { timeouts.delete(id); },
        window: {
            location: { href: '', reloadCount: 0, reload() { this.reloadCount++; } },
            navigator: { serviceWorker: options.serviceWorker },
            barkfluff: { IdentityApiClient: function () {} },
            proto: { barkfluff: { identity: { ResolveSignInRequest: function () {}, BeginSignInRequest: function () {} } } },
            addEventListener(type, callback) { (windowListeners[type] ||= []).push(callback); },
            BF: {
                i18n: { t: (key) => key === 'loginFlow.continue' && !options.oldLocale ? 'Continue' : key, ready: Promise.resolve() },
                node: {
                    origin: () => origin,
                    pinned: () => true,
                    meta: () => ({ name: 'Node A' }),
                    key: (value) => 'node:' + value,
                    clear() { origin = ''; }
                },
                nodePicker: { init() {}, openCount: 0, open() { this.openCount++; }, close() {} },
                tokens: {
                    get() { return tokens.stored; },
                    getAccessToken() { return tokens.stored && tokens.stored.accessToken; },
                    setTempMode(value) { tokens.modes.push(value); },
                    save(value) { tokens.stored = value; tokens.saved.push(value); },
                    clear() { tokens.stored = null; tokens.cleared++; }
                },
                auth: { getValidAccessToken: async () => null },
                legal: {
                    accepted: true,
                    isAccepted() { return this.accepted; },
                    accept() { this.accepted = true; },
                    init: async () => {},
                    flushCount: 0,
                    flushConsent() { this.flushCount++; return Promise.resolve(); }
                },
                fastAuth,
                sound: { play() {} },
                authUI: {
                    errorText(error) { return error && error.message ? error.message : 'auth.error.network'; },
                    cancelAll() {},
                    recover: async () => {}
                },
                confirmations: {
                    calls,
                    createCount: 0,
                    call(method, type, fields, options) {
                        const request = { method, type, fields, options };
                        calls.push(request);
                        return queued(callQueue, Promise.resolve(responseMode(1)), request);
                    },
                    create() {
                        const attempt = {
                            beginCalls: [], completeCalls: [], resendCalls: 0, cancelCalls: 0, pollCalls: [],
                            begin(method, type, fields) {
                                const request = { method, type, fields };
                                this.beginCalls.push(request);
                                return queued(beginQueue, Promise.resolve(challenge({ state: 2, factor: 0 })), request);
                            },
                            complete(code, recovery) {
                                const request = { code, recovery };
                                this.completeCalls.push(request);
                                return queued(completeQueue, Promise.resolve(completed()), request);
                            },
                            resend() {
                                this.resendCalls++;
                                return queued(resendQueue, Promise.resolve(challenge({ state: 1, factor: 3 })), this);
                            },
                            poll(onState, onError) { this.pollCalls.push({ onState, onError }); },
                            cancel() { this.cancelCalls++; }
                        };
                        this.createCount++;
                        attempts.push(attempt);
                        return attempt;
                    },
                    session() { return { accessToken: 'access-' + (tokens.saved.length + 1), refreshToken: 'refresh', accessTokenExpiration: 1000, refreshTokenExpiration: 2000 }; }
                }
            }
        }
    };
    context.window.barkfluff.IdentityApiClient.prototype.resolveSignIn = function () {};
    context.window.proto.barkfluff.identity.BeginSignInRequest.prototype.setReportLoginModeMismatch = function () {};
    if (options.staleContract) delete context.window.barkfluff.IdentityApiClient.prototype.resolveSignIn;
    if (options.missingSetter) delete context.window.proto.barkfluff.identity.BeginSignInRequest.prototype.setReportLoginModeMismatch;
    context.BF = context.window.BF;
    context.window.BF.nodePicker.openCount = 0;
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/login-page.js'), 'utf8'), context);

    return {
        elements,
        context,
        calls,
        attempts,
        beginQueue,
        callQueue,
        completeQueue,
        resendQueue,
        tokens,
        fastAuth,
        intervals,
        timeouts,
        dispatchWindow(type) { for (const callback of windowListeners[type] || []) callback(); },
        get origin() { return origin; },
        setOrigin(value) { origin = value; },
        ready: async () => { await Promise.resolve(); await new Promise(setImmediate); await new Promise(setImmediate); },
        resolve(mode, error) { callQueue.push(Promise.resolve(responseMode(mode, error))); },
        getElement(id) { return elements[id]; }
    };
}

async function settle(harness) {
    await harness.ready();
    await new Promise(setImmediate);
    await new Promise(setImmediate);
}

function submit(harness) {
    harness.getElement('loginForm').dispatch('submit');
}

async function enterPassword(harness, password) {
    harness.getElement('passwordInput').value = password;
    submit(harness);
    await settle(harness);
}

async function resolveMode(harness, mode, identifier) {
    harness.resolve(mode);
    harness.getElement('loginInput').value = identifier;
    submit(harness);
    await settle(harness);
}

async function testPasswordFlow() {
    const h = makeHarness();
    await h.ready();
    h.resolve(1);
    h.beginQueue.push(Promise.resolve(challenge({ state: 2, factor: 0 })));
    h.completeQueue.push(Promise.resolve(completed()));
    h.getElement('loginInput').value = '  user@example.test  ';
    submit(h);
    submit(h);
    await settle(h);
    assert.equal(h.calls.filter((call) => call.method === 'resolveSignIn').length, 1, 'identifier submit is single-flight');
    assert.equal(h.calls[0].fields.login, 'user@example.test', 'ResolveSignIn receives the trimmed email');
    assert.equal(Object.keys(h.calls[0].fields).join(','), 'login', 'ResolveSignIn does not receive a hidden password');
    assert.equal(h.getElement('passwordGroup').hidden, false);
    assert.equal(h.getElement('passwordInput').disabled, false);
    await enterPassword(h, 'correct-password');
    assert.equal(h.attempts[0].beginCalls.length, 1, 'double password submit starts one challenge');
    assert.equal(JSON.stringify(h.attempts[0].beginCalls[0].fields), JSON.stringify({
        login: 'user@example.test', password: 'correct-password', loginMode: 1, factor: 0,
        useRecoveryCode: false, reportLoginModeMismatch: true
    }));
    assert.equal(h.attempts[0].completeCalls.length, 1, 'approved password challenge completes without asking for another confirmation');
    assert.equal(h.attempts[0].completeCalls[0].code, '');
    assert.equal(h.tokens.saved.length, 1);
    assert.equal(h.context.BF.legal.flushCount, 1);
    assert.equal(h.context.window.location.href, '/messenger');
    assert.equal(h.tokens.modes[0], false);
}

async function testTelegramFlowAndQrRace() {
    const h = makeHarness();
    await h.ready();
    const oldQrGeneration = h.fastAuth.generation;
    h.resolve(2);
    h.beginQueue.push(Promise.resolve(challenge({ state: 1, factor: 3, available: [3] })));
    h.getElement('loginInput').value = '  telegram-user  ';
    submit(h);
    await settle(h);
    assert.equal(h.attempts[0].beginCalls[0].fields.login, 'telegram-user');
    assert.equal(h.attempts[0].beginCalls[0].fields.password, '', 'Telegram mode never submits a password');
    assert.equal(h.attempts[0].beginCalls[0].fields.loginMode, 2);
    assert.equal(h.getElement('passwordGroup').hidden, true);
    assert.equal(h.getElement('challengeStatus').textContent, 'security.telegramLoginWaiting');
    assert.equal(h.attempts[0].pollCalls.length, 1);
    assert.equal(h.tokens.saved.length, 0, 'tokens are not saved while Telegram approval is pending');
    h.beginQueue.push(Promise.resolve(challenge({ state: 1, factor: 3, needsCode: true, available: [3] })));
    h.getElement('loginRecovery').checked = true;
    h.getElement('loginRecovery').dispatch('change');
    await settle(h);
    assert.equal(h.attempts[0].beginCalls[1].fields.password, '', 'Telegram recovery follows the passwordless server policy');
    assert.equal(h.attempts[0].beginCalls[1].fields.useRecoveryCode, true);
    h.fastAuth.completeGeneration(oldQrGeneration);
    assert.equal(h.fastAuth.shown, 0, 'a QR generated before the login attempt is ignored after cancellation');
}

async function testUnknownAccountAndOldServer() {
    const unknown = makeHarness();
    await unknown.ready();
    unknown.callQueue.push(Promise.resolve(responseMode(0, 'account_not_found')));
    unknown.getElement('loginInput').value = 'unknown-user';
    unknown.getElement('passwordInput').value = 'must-not-submit';
    submit(unknown);
    await settle(unknown);
    assert.equal(unknown.attempts.length, 0, 'unknown identifier never reaches BeginSignIn');
    assert.equal(unknown.getElement('loginError').textContent, 'loginFlow.accountNotFound');
    assert.equal(unknown.getElement('passwordInput').disabled, true);
    assert.equal(unknown.tokens.saved.length, 0);

    const oldServer = makeHarness();
    await oldServer.ready();
    const unimplemented = new Error('UNIMPLEMENTED');
    unimplemented.code = 12;
    oldServer.callQueue.push(Promise.reject(unimplemented));
    oldServer.getElement('loginInput').value = 'user';
    submit(oldServer);
    await settle(oldServer);
    assert.equal(oldServer.getElement('loginError').textContent, 'loginFlow.serverUpdateRequired');
    assert.equal(oldServer.attempts.length, 0);
}

async function testPasswordCodeRetryAndSinglePoll() {
    const h = makeHarness();
    await h.ready();
    h.resolve(3);
    h.getElement('loginInput').value = 'factor-user';
    submit(h);
    await settle(h);

    const wrongPassword = new Error('bad credentials');
    wrongPassword.errorCode = '21BFB9B5-C377-45D1-9B15-6B7F3432B397';
    h.beginQueue.push(Promise.reject(wrongPassword));
    await enterPassword(h, 'wrong');
    assert.equal(h.getElement('passwordError').textContent, 'auth.error.badCredentials');
    assert.equal(h.tokens.saved.length, 0);

    h.beginQueue.push(Promise.resolve(challenge({ state: 1, factor: 1, needsCode: true, available: [1, 2, 3] })));
    await enterPassword(h, 'correct');
    assert.equal(h.getElement('codeGroup').hidden, false);
    assert.equal(h.getElement('factorGroup').hidden, false);
    assert.equal(h.attempts[0].beginCalls[1].fields.factor, 0, 'server preferred factor is requested first');
    assert.equal(h.tokens.saved.length, 0, 'a pending second factor never saves tokens');

    const factorOptions = h.getElement('loginFactor').children.slice();
    h.getElement('loginFactor').value = '2';
    h.getElement('confirmationCode').value = '123456';
    h.attempts[0].pollCalls[0].onState(challenge({ state: 1, factor: 1, needsCode: true, available: [1, 2, 3] }));
    await settle(h);
    assert.equal(h.getElement('confirmationCode').value, '123456', 'a polling update preserves a code being entered');
    factorOptions.forEach((option, index) => {
        assert.strictEqual(h.getElement('loginFactor').children[index], option, 'unchanged factors preserve their option nodes');
    });
    assert.equal(h.getElement('loginFactor').value, '2', 'an unchanged poll preserves current factor selection interaction');
    assert.equal(h.attempts[0].pollCalls.length, 1, 'poll updates do not start another polling loop');

    h.completeQueue.push(Promise.resolve(completed({ error: 'invalid_code', state: 1, session: false })));
    h.getElement('confirmCodeBtn').click();
    await settle(h);
    assert.equal(h.getElement('confirmationError').textContent, 'auth.error.badCode');
    assert.equal(h.tokens.saved.length, 0);

    h.beginQueue.push(Promise.resolve(challenge({ state: 1, factor: 2, needsCode: true, available: [1, 2, 3] })));
    h.getElement('loginFactor').value = '2';
    h.getElement('loginFactor').dispatch('change');
    await settle(h);
    assert.ok(h.attempts[0].cancelCalls > 0, 'changing factor cancels the previous challenge');
    assert.equal(h.attempts[0].beginCalls.length, 3);
    assert.equal(h.attempts[0].beginCalls[2].fields.factor, 2);

    h.beginQueue.push(Promise.resolve(challenge({ state: 1, factor: 2, needsCode: true, available: [1, 2, 3] })));
    h.getElement('loginRecovery').checked = true;
    h.getElement('loginRecovery').dispatch('change');
    await settle(h);
    assert.equal(h.attempts[0].beginCalls[3].fields.password, 'correct', 'mode 3 recovery still submits the password');
    assert.equal(h.attempts[0].beginCalls[3].fields.useRecoveryCode, true);

    h.getElement('confirmationCode').value = 'correct-code';
    const timeout = new Error('request timed out');
    timeout.kind = 'timeout';
    timeout.code = 4;
    h.completeQueue.push(Promise.reject(timeout));
    h.getElement('confirmationCode').dispatch('keydown', { key: 'Enter' });
    await settle(h);
    assert.equal(h.getElement('challengeRetryBtn').hidden, false, 'a timed out code completion offers a retry');
    h.completeQueue.push(Promise.resolve(completed()));
    h.getElement('challengeRetryBtn').click();
    await settle(h);
    assert.equal(h.attempts[0].completeCalls.at(-1).code, 'correct-code');
    assert.equal(h.tokens.saved.length, 1);
    assert.equal(h.context.window.location.href, '/messenger');
}

async function testModeDriftRequiresUserAction() {
    const h = makeHarness();
    await h.ready();
    h.resolve(2);
    h.callQueue.push(Promise.resolve(responseMode(1)));
    h.beginQueue.push(Promise.resolve(challenge({ error: 'login_mode_disabled' })));
    h.getElement('loginInput').value = 'drift-user';
    submit(h);
    await settle(h);
    assert.equal(h.calls.filter((call) => call.method === 'resolveSignIn').length, 2, 'a mismatch resolves the current server mode');
    assert.equal(h.attempts[0].beginCalls.length, 1, 'mismatch does not restart Telegram automatically');
    assert.equal(h.getElement('loginFlowModeChanged')?.textContent, undefined);
    assert.equal(h.getElement('loginError').textContent, 'loginFlow.modeChanged');
    assert.equal(h.getElement('passwordGroup').hidden, true, 'the resolved password mode still requires an explicit Continue');

    h.resolve(1);
    submit(h);
    await settle(h);
    assert.equal(h.getElement('passwordGroup').hidden, false);
    assert.equal(h.attempts[0].beginCalls.length, 1);
}

async function testTransientPollErrorKeepsTheApprovalChallenge() {
    const h = makeHarness();
    await h.ready();
    h.resolve(2);
    h.beginQueue.push(Promise.resolve(challenge({ state: 1, factor: 3, available: [3] })));
    h.getElement('loginInput').value = 'telegram-user';
    submit(h);
    await settle(h);

    const attempt = h.attempts[0];
    const unavailable = new Error('temporarily unavailable');
    unavailable.code = 14;
    unavailable.retryable = true;
    attempt.pollCalls[0].onError(unavailable);
    await settle(h);
    assert.equal(attempt.cancelCalls, 0, 'a retryable poll error keeps the server approval alive');
    assert.equal(h.getElement('challengeRetryBtn').hidden, true, 'automatic polling remains active without a new-challenge retry');
    assert.equal(attempt.beginCalls.length, 1, 'a transient poll error does not send another begin request');
    assert.equal(attempt.pollCalls.length, 1, 'the same poll subscription remains responsible for the challenge');
    assert.equal(h.getElement('challengeStatus').textContent, 'auth.error.network', 'the transient failure is shown inline');

    attempt.pollCalls[0].onState(challenge({ state: 2, factor: 3, available: [3] }));
    await settle(h);
    assert.equal(attempt.completeCalls.length, 1, 'the same approved challenge completes once');
    assert.equal(h.tokens.saved.length, 1);
    assert.equal(h.context.window.location.href, '/messenger');
}

async function testNonRetryablePollErrorOffersManagedRetry() {
    const h = makeHarness();
    await h.ready();
    h.resolve(2);
    h.beginQueue.push(Promise.resolve(challenge({ state: 1, factor: 3, available: [3] })));
    h.getElement('loginInput').value = 'telegram-user';
    submit(h);
    await settle(h);

    const attempt = h.attempts[0];
    const rejected = new Error('challenge rejected');
    rejected.code = 7;
    rejected.retryable = false;
    attempt.pollCalls[0].onError(rejected);
    await settle(h);
    assert.ok(attempt.cancelCalls > 0, 'a non-retryable poll error cancels the failed challenge');
    assert.equal(h.getElement('challengeRetryBtn').hidden, false, 'the user can retry after a non-retryable error');
    assert.equal(attempt.beginCalls.length, 1, 'the failed challenge does not restart automatically');

    h.beginQueue.push(Promise.resolve(challenge({ state: 2, factor: 3, available: [3] })));
    h.getElement('challengeRetryBtn').click();
    await settle(h);
    assert.equal(attempt.beginCalls.length, 2, 'the explicit retry begins one new challenge');
    assert.equal(attempt.completeCalls.length, 1);
    assert.equal(h.tokens.saved.length, 1);
}

async function testCancellationAndTokenGate() {
    const staleResolve = makeHarness();
    await staleResolve.ready();
    const pendingResolve = deferred();
    staleResolve.callQueue.push(pendingResolve.promise);
    staleResolve.getElement('loginInput').value = 'stale-user';
    submit(staleResolve);
    const signal = staleResolve.calls[0].options.signal;
    staleResolve.getElement('toRegisterBtn').click();
    assert.equal(signal.aborted, true, 'registration cancels an in-flight resolve');
    pendingResolve.resolve(responseMode(1));
    await settle(staleResolve);
    assert.equal(staleResolve.getElement('passwordGroup').hidden, true, 'late resolve cannot reveal password step');

    const staleAttempt = makeHarness();
    await staleAttempt.ready();
    staleAttempt.resolve(2);
    const pendingBegin = deferred();
    staleAttempt.beginQueue.push(pendingBegin.promise);
    staleAttempt.getElement('loginInput').value = 'edit-user';
    submit(staleAttempt);
    await settle(staleAttempt);
    const attempt = staleAttempt.attempts[0];
    staleAttempt.getElement('changeLoginBtn').click();
    assert.ok(attempt.cancelCalls > 0, 'Edit cancels a sign-in attempt');
    assert.equal(staleAttempt.getElement('passwordInput').value, '', 'Edit clears the in-memory password');
    pendingBegin.resolve(challenge({ state: 2, factor: 3 }));
    await settle(staleAttempt);
    assert.equal(staleAttempt.getElement('challengeGroup').hidden, true, 'late BeginSignIn cannot reopen confirmation');
    assert.equal(staleAttempt.tokens.saved.length, 0);

    const legal = makeHarness();
    await legal.ready();
    legal.resolve(2);
    legal.beginQueue.push(Promise.resolve(challenge({ state: 1, factor: 3 })));
    legal.getElement('loginInput').value = 'legal-user';
    submit(legal);
    await settle(legal);
    const legalAttempt = legal.attempts[0];
    legal.getElement('legalAcceptCheck').checked = false;
    legal.getElement('legalAcceptCheck').dispatch('change');
    assert.ok(legalAttempt.cancelCalls > 0, 'revoking consent cancels the active challenge');
    assert.equal(legal.tokens.saved.length, 0);

    const node = makeHarness();
    await node.ready();
    node.resolve(2);
    node.beginQueue.push(Promise.resolve(challenge({ state: 1, factor: 3 })));
    node.getElement('loginInput').value = 'node-user';
    submit(node);
    await settle(node);
    const nodeAttempt = node.attempts[0];
    node.getElement('nodeChangeBtn').click();
    assert.ok(nodeAttempt.cancelCalls > 0, 'changing nodes cancels the active challenge');
    assert.equal(node.origin, '');
    assert.equal(node.tokens.saved.length, 0);
}

function eventTarget(values) {
    const listeners = new Map();
    return Object.assign({
        addEventListener(type, callback) { if (!listeners.has(type)) listeners.set(type, new Set()); listeners.get(type).add(callback); },
        removeEventListener(type, callback) { listeners.get(type)?.delete(callback); },
        emit(type) { for (const callback of [...(listeners.get(type) || [])]) callback(); },
        listenerCount() { return [...listeners.values()].reduce((sum, callbacks) => sum + callbacks.size, 0); }
    }, values);
}

async function testStaleRuntimeUpgrade() {
    for (const options of [{ staleContract: true }, { missingSetter: true }, { oldLocale: true }]) {
        const h = makeHarness(options);
        await h.ready();
        assert.equal(h.elements.loginForm.hidden, true);
        assert.equal(h.elements.fastAuthCard.hidden, true);
        assert.equal(h.elements.loginUpgradeMessage.textContent, 'pwa.updateAvailable');
        h.elements.loginInput.value = 'user';
        h.elements.passwordInput.value = 'must-not-send';
        submit(h);
        await settle(h);
        assert.equal(h.calls.length, 0);
        assert.equal(h.attempts.length, 0);
        assert.equal(h.fastAuth.starts, 0);
        assert.equal(h.tokens.saved.length, 0);
    }
    const waiting = { sent: [], postMessage(message) { this.sent.push(message); } };
    const registration = eventTarget({ waiting, updateCount: 0, update() { this.updateCount++; return Promise.resolve(); } });
    const sw = eventTarget({ controller: {}, getRegistration: async () => registration });
    const h = makeHarness({ staleContract: true, oldLocale: true, serviceWorker: sw });
    await h.ready();
    h.elements.loginUpgradeBtn.click();
    h.elements.loginUpgradeBtn.click();
    await settle(h);
    assert.equal(waiting.sent.length, 1);
    assert.equal(waiting.sent[0].type, 'SKIP_WAITING');
    assert.equal(registration.updateCount, 0);
    assert.equal(h.context.window.location.reloadCount, 0);
    sw.emit('controllerchange');
    assert.equal(h.context.window.location.reloadCount, 0, 'same controller does not trigger reload');
    sw.controller = {};
    sw.emit('controllerchange');
    sw.emit('controllerchange');
    assert.equal(h.context.window.location.reloadCount, 1);
    assert.equal(sw.listenerCount(), 0);
    assert.equal(registration.listenerCount(), 0);
    assert.equal(h.timeouts.size, 0);
}

async function testInstallingUpgradeAndFailures() {
    const installing = eventTarget({ state: 'installing' });
    const registration = eventTarget({ installing, waiting: null, update: async () => {} });
    const sw = eventTarget({ controller: {}, getRegistration: async () => registration });
    const h = makeHarness({ staleContract: true, serviceWorker: sw });
    await h.ready();
    h.elements.loginUpgradeBtn.click();
    await settle(h);
    let messages = 0;
    registration.waiting = { postMessage() { messages++; } };
    installing.state = 'installed';
    installing.emit('statechange');
    installing.emit('statechange');
    assert.equal(messages, 1);
    h.dispatchWindow('pagehide');
    sw.controller = {};
    sw.emit('controllerchange');
    assert.equal(h.context.window.location.reloadCount, 0, 'pagehide cancels the generation');
    assert.equal(installing.listenerCount(), 0);
    assert.equal(sw.listenerCount(), 0);
    assert.equal(h.timeouts.size, 0);

    const discovered = eventTarget({ waiting: null, installing: null, update: async () => {} });
    const service = eventTarget({ controller: {}, getRegistration: async () => discovered });
    const found = makeHarness({ oldLocale: true, serviceWorker: service });
    await found.ready();
    found.elements.loginUpgradeBtn.click();
    await settle(found);
    discovered.installing = eventTarget({ state: 'installing' });
    discovered.emit('updatefound');
    let activated = false;
    discovered.waiting = { postMessage() { activated = true; } };
    discovered.installing.state = 'installed';
    discovered.installing.emit('statechange');
    assert.equal(activated, true, 'updatefound discovers workers installed by update()');
    service.controller = {};
    service.emit('controllerchange');
    assert.equal(found.context.window.location.reloadCount, 1);
    assert.equal(discovered.installing.listenerCount(), 0);

    const activatedService = eventTarget({ controller: {}, getRegistration() { throw new Error('already active'); } });
    const alreadyActive = makeHarness({ staleContract: true, serviceWorker: activatedService });
    await alreadyActive.ready();
    activatedService.controller = {};
    alreadyActive.elements.loginUpgradeBtn.click();
    alreadyActive.elements.loginUpgradeBtn.click();
    assert.equal(alreadyActive.context.window.location.reloadCount, 1, 'a shell activated by another tab can be loaded on explicit click');
    assert.equal(alreadyActive.timeouts.size, 0);

    const noWorker = makeHarness({ staleContract: true });
    await noWorker.ready();
    noWorker.elements.loginUpgradeBtn.click();
    assert.equal(noWorker.elements.loginUpgradeMessage.textContent, 'auth.error.network');
    assert.equal(noWorker.timeouts.size, 0);

    for (const kind of ['offline', 'no-update', 'missing-registration', 'redundant']) {
        const worker = eventTarget({ state: 'installing' });
        let updates = 0;
        const reg = eventTarget({ installing: worker, waiting: null, update() {
            updates++;
            return kind === 'offline' ? Promise.reject(new Error('offline')) : Promise.resolve();
        } });
        const serviceWorker = eventTarget({ controller: {}, getRegistration: async () => kind === 'missing-registration' ? null : reg });
        const failed = makeHarness({ staleContract: true, serviceWorker });
        await failed.ready();
        failed.elements.loginUpgradeBtn.click();
        await settle(failed);
        if (kind === 'no-update') for (const callback of [...failed.timeouts.values()]) callback();
        if (kind === 'redundant') { worker.state = 'redundant'; worker.emit('statechange'); }
        assert.equal(failed.elements.loginUpgradeBtn.disabled, false, kind);
        assert.equal(failed.elements.loginUpgradeMessage.textContent, 'auth.error.network', kind);
        assert.equal(failed.context.window.location.reloadCount, 0);
        assert.equal(failed.calls.length, 0);
        assert.equal(serviceWorker.listenerCount(), 0);
        assert.equal(reg.listenerCount(), 0);
        assert.equal(worker.listenerCount(), 0);
        assert.equal(failed.timeouts.size, 0);
        await settle(failed);
        assert.ok(updates <= 1, 'no automatic retry');
        failed.elements.loginUpgradeBtn.click();
        await settle(failed);
        if (kind === 'no-update') for (const callback of [...failed.timeouts.values()]) callback();
        if (kind === 'redundant') worker.emit('statechange');
        assert.ok(updates <= 2, 'retry requires another click');
        failed.dispatchWindow('pagehide');
    }
}

async function main() {
    await testStaleRuntimeUpgrade();
    await testInstallingUpgradeAndFailures();
    console.log('PASS: stale installed login shells block auth, activate updates on explicit action, reload once, and bound failed updates with cleanup');
    await testPasswordFlow();
    await testTelegramFlowAndQrRace();
    await testUnknownAccountAndOldServer();
    await testPasswordCodeRetryAndSinglePoll();
    await testTransientPollErrorKeepsTheApprovalChallenge();
    await testNonRetryablePollErrorOffersManagedRetry();
    await testModeDriftRequiresUserAction();
    await testCancellationAndTokenGate();
    console.log('PASS: email-first login resolves modes, handles challenges, retries, rejects stale work, and saves tokens only on completion');
}

main().catch((error) => {
    console.error('FAIL: ' + error.stack);
    process.exitCode = 1;
});
