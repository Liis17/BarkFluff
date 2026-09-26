const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadConfirmations() {
    let origin = 'https://node-a.test';
    const timers = new Map();
    const calls = [];
    let nextTimer = 0;
    const identity = {
        AuthChallengeReference: class {
            setId(value) {
                this.id = value;
            }
            setSecret(value) {
                this.secret = value;
            }
        },
        BeginSignInRequest: class {
            setLogin(value) {
                this.login = value;
            }
            setPassword(value) {
                this.password = value;
            }
        },
        CompleteAuthChallengeRequest: class {
            setChallenge(value) {
                this.challenge = value;
            }
            setCode(value) {
                this.code = value;
            }
            setUseRecoveryCode(value) {
                this.useRecoveryCode = value;
            }
        }
    };
    const reference = { getId: () => 'challenge-id', getSecret: () => 'browser-secret' };
    let remoteState = 1;
    class IdentityApiClient {
        constructor(clientOrigin) {
            return new Proxy(
                {},
                {
                    get: (_target, method) => (request, metadata) => {
                        calls.push({ origin: clientOrigin, method, request, metadata });
                        if (method === 'beginSignIn') return Promise.resolve({ getChallenge: () => reference });
                        if (method === 'getAuthChallenge') return Promise.resolve({ getState: () => remoteState });
                        if (method === 'completeAuthChallenge') return Promise.resolve({ getState: () => 5 });
                        return Promise.resolve({});
                    }
                }
            );
        }
    }
    const context = {
        AbortController,
        Date,
        DOMException,
        Error,
        Math,
        Promise,
        clearTimeout(id) {
            timers.delete(id);
        },
        setTimeout(callback, delay) {
            const id = ++nextTimer;
            timers.set(id, { callback, delay });
            return id;
        },
        window: {
            BF: {
                node: { origin: () => origin },
                network: {
                    POLICIES: { READ: { name: 'read' }, MUTATION: { name: 'mutation' } },
                    withSignal: (policy, signal) => Object.assign({}, policy, { signal }),
                    unary(method, request, metadata, policy) {
                        const result = method(request, metadata, policy);
                        return Promise.resolve(result);
                    }
                },
                metadata: { build: (token) => ({ token: token || '' }) },
                clients: { getValidToken: async () => 'session-token' },
                auth: { getValidAccessToken: async () => 'session-token' }
            },
            proto: { barkfluff: { identity } },
            barkfluff: { IdentityApiClient }
        }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/auth-challenge.js'), 'utf8'), context);
    return {
        api: context.window.BF.confirmations,
        calls,
        timers,
        setOrigin(value) {
            origin = value;
        },
        setState(value) {
            remoteState = value;
        },
        runTimer(id) {
            const timer = timers.get(id);
            timers.delete(id);
            timer.callback();
        }
    };
}

async function main() {
    const harness = loadConfirmations();
    const attempt = harness.api.create();
    const begun = await attempt.begin('beginSignIn', 'BeginSignInRequest', { login: 'liis', password: 'pw' });
    assert.equal(harness.calls[0].method, 'beginSignIn');
    assert.equal(harness.calls[0].request.login, 'liis');
    assert.equal(begun.getChallenge().getSecret(), 'browser-secret');

    let observed = 0;
    attempt.poll(
        () => {
            observed += 1;
        },
        (error) => {
            throw error;
        }
    );
    const firstTimer = Array.from(harness.timers.keys())[0];
    assert.equal(harness.timers.get(firstTimer).delay, 2000);
    harness.setState(1);
    harness.runTimer(firstTimer);
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(observed, 1);
    const secondTimer = Array.from(harness.timers.keys())[0];
    harness.setOrigin('https://node-b.test');
    attempt.cancel();
    assert.equal(harness.timers.size, 0);
    await new Promise((resolve) => setImmediate(resolve));
    const cancel = harness.calls.find((call) => call.method === 'cancelAuthChallenge');
    assert.ok(cancel);
    assert.equal(cancel.request.getId(), 'challenge-id');
    assert.equal(cancel.request.getSecret(), 'browser-secret');
    assert.equal(cancel.origin, 'https://node-a.test');
    assert.equal(observed, 1, 'a cancelled old-node attempt cannot publish more status');
    assert.ok(secondTimer);

    const session = harness.api.session({
        getAccessToken: () => ({
            getValue: () => 'access',
            getExpirationDate: () => ({ toDate: () => new Date(1000) })
        }),
        getRefreshToken: () => ({
            getValue: () => 'refresh',
            getExpirationDate: () => ({ toDate: () => new Date(2000) })
        })
    });
    assert.equal(
        JSON.stringify(session),
        JSON.stringify({
            accessToken: 'access',
            accessTokenExpiration: 1000,
            refreshToken: 'refresh',
            refreshTokenExpiration: 2000
        })
    );
    console.log('PASS: auth challenge requests bind to a node, poll every 2s, cancel stale attempts and map sessions');
}

main().catch((error) => {
    console.error('FAIL: ' + error.stack);
    process.exitCode = 1;
});
