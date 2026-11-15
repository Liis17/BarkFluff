const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ID = '0f8fad5b-d9cb-469f-a165-70867728950e';

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function makeEl(tag) {
    var el = {
        tagName: tag,
        className: '',
        children: [],
        attributes: {},
        listeners: {},
        style: {},
        disabled: false,
        hidden: false,
        value: '',
        _text: '',
        setAttribute: function (n, v) {
            this.attributes[n] = v;
        },
        addEventListener: function (type, cb) {
            this.listeners[type] = cb;
        },
        appendChild: function (child) {
            this.children.push(child);
            return child;
        }
    };
    Object.defineProperty(el, 'innerHTML', {
        set: function () {
            el.children = [];
            el._text = '';
        },
        get: function () {
            return '';
        }
    });
    Object.defineProperty(el, 'textContent', {
        get: function () {
            return el._text + el.children.map(function (c) {
                return c.textContent;
            }).join(' ');
        },
        set: function (v) {
            el.children = [];
            el._text = String(v);
        }
    });
    return el;
}

function all(el, predicate, out) {
    out = out || [];
    if (predicate(el)) out.push(el);
    el.children.forEach(function (c) {
        all(c, predicate, out);
    });
    return out;
}

function createHarness() {
    var calls = [];
    var pendingScans = [];
    var decideResult = { accept: Promise.resolve(), reject: Promise.resolve() };

    function message(name, fields) {
        return function () {
            var data = {};
            fields.forEach(function (f) {
                var setter = 'set' + f[0].toUpperCase() + f.slice(1);
                this[setter] = function (v) {
                    data[f] = v;
                };
            }, this);
            this.data = data;
            this.kind = name;
        };
    }
    var BF = {
        clients: {
            fastAuth: {
                scanFastAuth: function () {},
                acceptFastAuth: function () {},
                rejectFastAuth: function () {}
            },
            authCall: function (method, req) {
                var name = req.kind;
                calls.push([name, Object.assign({}, req.data)]);
                if (name === 'scan') {
                    return new Promise(function (resolve, reject) {
                        pendingScans.push({ resolve: resolve, reject: reject });
                    });
                }
                return name === 'accept' ? decideResult.accept : decideResult.reject;
            }
        },
        network: { POLICIES: { MUTATION: {} } },
        i18n: {
            t: function (key) {
                return key;
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        proto: {
            barkfluff: {
                fast: {
                    auth: {
                        ScanFastAuthRequest: message('scan', ['fastAuthId']),
                        AcceptFastAuthRequest: message('accept', ['fastAuthId', 'confirmationCode']),
                        RejectFastAuthRequest: message('reject', ['fastAuthId', 'confirmationCode'])
                    }
                }
            }
        },
        document: {
            createElement: function (tag) {
                return makeEl(tag);
            }
        },
        navigator: {},
        Promise: Promise,
        setTimeout: setTimeout,
        clearTimeout: clearTimeout
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/fast-auth-link.js'), 'utf8'), context);
    var container = makeEl('div');
    BF.fastAuthLink.render(container);
    return {
        BF: BF,
        container: container,
        calls: calls,
        decideResult: decideResult,
        resolveScan: function (i, info) {
            pendingScans[i].resolve({
                getDeviceName: function () {
                    return info.deviceName || 'Pixel';
                },
                getOperationSystem: function () {
                    return 'Android 16';
                },
                getAppName: function () {
                    return 'BarkFluff';
                },
                getAppVersion: function () {
                    return '1.2';
                },
                getIpAddress: function () {
                    return '10.0.0.5';
                },
                getConfirmationCode: function () {
                    return 'confirm-1';
                }
            });
        },
        rejectScan: function (i, errorCode) {
            pendingScans[i].reject({ errorCode: errorCode });
        },
        enterCode: function (value) {
            var input = all(container, function (e) {
                return e.tagName === 'input' && e.className === 'sd-input';
            })[0];
            input.value = value;
            this.button('fastAuthLink.continue').listeners.click();
        },
        button: function (key) {
            return all(container, function (e) {
                return e.tagName === 'button' && e._text === key;
            })[0];
        }
    };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('a code that is not a FastAuth session id is rejected locally without ScanFastAuth', async function () {
        var h = createHarness();
        h.enterCode('https://example.test/login');
        assert.deepEqual(h.calls, []);
        assert.match(h.container.textContent, /fastAuthLink\.error\.invalid/);
    });

    await test('scan shows the device and only an explicit approval sends AcceptFastAuth with the confirmation code', async function () {
        var h = createHarness();
        h.enterCode('  ' + ID.toUpperCase() + ' ');
        assert.deepEqual(h.calls, [['scan', { fastAuthId: ID.toUpperCase() }]]);
        h.resolveScan(0, { deviceName: 'Pixel 9' });
        await settle();
        var text = h.container.textContent;
        assert.match(text, /fastAuthLink\.warning/);
        assert.match(text, /Pixel 9/);
        assert.match(text, /Android 16/);
        assert.match(text, /BarkFluff 1\.2/);
        assert.match(text, /10\.0\.0\.5/);
        assert.equal(h.calls.length, 1, 'nothing is approved before the click');

        h.button('fastAuthLink.accept').listeners.click();
        await settle();
        assert.deepEqual(h.calls[1], ['accept', { fastAuthId: ID.toUpperCase(), confirmationCode: 'confirm-1' }]);
        assert.match(h.container.textContent, /fastAuthLink\.accepted/);

        h.BF.fastAuthLink.stop();
        assert.equal(h.calls.length, 2, 'a decided request is not rejected on leaving');
    });

    await test('leaving the screen after a scan without a decision rejects the request', async function () {
        var h = createHarness();
        h.enterCode(ID);
        h.resolveScan(0, {});
        await settle();
        h.BF.fastAuthLink.stop();
        assert.deepEqual(h.calls[1], ['reject', { fastAuthId: ID, confirmationCode: 'confirm-1' }]);
    });

    await test('leaving while ScanFastAuth is in flight rejects as soon as the code arrives', async function () {
        var h = createHarness();
        h.enterCode(ID);
        h.BF.fastAuthLink.stop();
        h.resolveScan(0, {});
        await settle();
        assert.deepEqual(h.calls[1], ['reject', { fastAuthId: ID, confirmationCode: 'confirm-1' }]);
    });

    await test('server errors are explained and a failed approval can be retried', async function () {
        var h = createHarness();
        h.enterCode(ID);
        h.rejectScan(0, 'D2F71E8A-3C5B-4197-8A6D-4E9B27C5F1A8');
        await settle();
        assert.match(h.container.textContent, /fastAuthLink\.error\.expired/);

        h.enterCode(ID);
        h.resolveScan(1, {});
        await settle();
        h.decideResult.accept = Promise.reject({ errorCode: '3C8A1E5F-7D29-4B83-9E16-5A2C8F4B7D31' });
        h.button('fastAuthLink.accept').listeners.click();
        await settle();
        assert.match(h.container.textContent, /fastAuthLink\.error\.state/);
        assert.equal(h.button('fastAuthLink.accept').disabled, false);
        h.decideResult.reject = Promise.resolve();
        h.button('fastAuthLink.reject').listeners.click();
        await settle();
        assert.equal(h.calls[h.calls.length - 1][0], 'reject');
        assert.match(h.container.textContent, /fastAuthLink\.rejected/);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
