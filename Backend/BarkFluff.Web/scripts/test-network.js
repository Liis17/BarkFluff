const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadNetwork() {
    const context = {
        AbortController: globalThis.AbortController,
        Date: Date,
        Error: Error,
        Math: Math,
        Promise: Promise,
        clearTimeout: clearTimeout,
        setTimeout: setTimeout,
        window: { BF: {} }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/network.js'), 'utf8'), context);
    return context.window.BF.network;
}

async function main() {
    const network = loadNetwork();

    let successMetadata;
    const success = await network.unary(
        function (_request, metadata, callback) {
            successMetadata = metadata;
            callback(null, { value: 42 });
            return { cancel: function () {} };
        },
        {},
        { existing: 'header' },
        { attemptTimeoutMs: 100, overallTimeoutMs: 100, maxAttempts: 1 }
    );
    assert.equal(success.value, 42);
    assert.equal(successMetadata.existing, 'header');
    assert.ok(Number(successMetadata.deadline) > Date.now());

    const order = [];
    let timeoutCallback;
    const timeoutPromise = network.unary(
        function (_request, _metadata, callback) {
            timeoutCallback = callback;
            return {
                cancel: function () {
                    order.push('cancel');
                }
            };
        },
        {},
        {},
        { attemptTimeoutMs: 10, overallTimeoutMs: 10, maxAttempts: 1, outcomeUnknown: true }
    );
    timeoutPromise.catch(function () {
        order.push('reject');
    });
    await assert.rejects(timeoutPromise, function (error) {
        assert.equal(error.kind, 'timeout');
        assert.equal(error.code, 4);
        assert.equal(error.outcomeUnknown, true);
        return true;
    });
    assert.deepEqual(order.sort(), ['cancel', 'reject']);
    timeoutCallback(null, { late: true });

    const controller = new AbortController();
    let abortCancelled = false;
    const aborted = network.unary(
        function () {
            return {
                cancel: function () {
                    abortCancelled = true;
                }
            };
        },
        {},
        {},
        { attemptTimeoutMs: 100, overallTimeoutMs: 100, maxAttempts: 1, signal: controller.signal }
    );
    controller.abort();
    await assert.rejects(aborted, function (error) {
        assert.equal(error.kind, 'cancelled');
        return true;
    });
    assert.equal(abortCancelled, true);

    const deadlines = [];
    let attempts = 0;
    const retried = await network.unary(
        function (_request, metadata, callback) {
            attempts += 1;
            deadlines.push(metadata.deadline);
            if (attempts === 1) callback({ code: 14, message: 'unavailable' });
            else callback(null, { ok: true });
            return { cancel: function () {} };
        },
        {},
        {},
        {
            attemptTimeoutMs: 100,
            overallTimeoutMs: 300,
            maxAttempts: 2,
            retryCodes: [14],
            baseDelayMs: 0,
            maxDelayMs: 0
        }
    );
    assert.equal(retried.ok, true);
    assert.equal(attempts, 2);
    assert.equal(deadlines.length, 2);

    console.log('PASS: unary transport bounds, cancels, and selectively retries grpc-web calls');
}

main().catch(function (error) {
    console.error('FAIL: ' + error.stack);
    process.exitCode = 1;
});
