const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadHealth(fetchImpl) {
    const requests = [];
    const context = {
        Date: Date,
        Promise: Promise,
        Math: Math,
        setTimeout: setTimeout,
        clearTimeout: clearTimeout,
        window: {
            performance: { now: () => Date.now() },
            AbortController: globalThis.AbortController,
            fetch: function (url, options) {
                requests.push({ url: url, options: options });
                return fetchImpl(url, options);
            },
            BF: {
                node: { origin: () => 'https://node.example.test' }
            }
        }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/health.js'), 'utf8'),
        context
    );
    return { health: context.window.BF.health, requests: requests };
}

async function main() {
    const loaded = loadHealth(function (url) {
        if (url.endsWith('/ping/identity')) {
            return Promise.resolve({
                ok: true,
                status: 200,
                text: () => Promise.resolve('pong')
            });
        }
        if (url.endsWith('/ping/beacon')) {
            return Promise.resolve({
                ok: true,
                status: 204,
                text: () => Promise.resolve('pong')
            });
        }
        if (url.endsWith('/ping/users')) {
            return Promise.resolve({
                ok: false,
                status: 503,
                text: () => Promise.resolve('service unavailable')
            });
        }
        return Promise.reject(new Error('offline'));
    });

    const results = await loaded.health.check();
    assert.equal(results.length, 10, 'all node liveness services should be checked');
    assert.equal(
        results.some((service) => service.id === 'navigator'),
        false,
        'Navigator should not be included in node health checks'
    );
    const identity = results.find((service) => service.id === 'identity');
    const beacon = results.find((service) => service.id === 'beacon');
    const users = results.find((service) => service.id === 'users');
    const web = results.find((service) => service.id === 'web');

    assert.equal(identity.available, true, '200 pong should be available');
    assert.equal(identity.status, 200);
    assert.equal(beacon.available, false, 'only HTTP 200 pong should be available');
    assert.equal(beacon.status, 204);
    assert.equal(users.available, false, 'non-2xx response should be unavailable');
    assert.equal(users.status, 503);
    assert.equal(web.available, false, 'network failure should be unavailable');
    assert.ok(loaded.requests.every((request) => request.options.cache === 'no-store'));

    console.log('PASS: health checks classify pong, HTTP errors, and network failures');
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
