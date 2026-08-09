const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const INVALID_REFRESH_TOKEN = '7E6A31C5-3C4D-412E-87BC-0A387617A5D3';

function loadModule(file, refreshError) {
    var cleared = false;
    var refreshToken = 'refresh-token';
    var location = { href: '/messenger' };
    class IdentityApiClient {
        createToken(_request, _metadata, callback) { callback(refreshError); }
    }
    class Client {}
    var context = {
        Promise: Promise,
        console: console,
        window: {
            location: location,
            BF: {
                node: { origin: function () { return 'https://node.example.test'; } },
                tokens: {
                    getRefreshToken: function () { return refreshToken; },
                    isAccessExpired: function () { return true; },
                    getAccessToken: function () { return 'access-token'; },
                    get: function () { return refreshToken ? { accessToken: 'access-token', refreshToken: refreshToken } : null; },
                    save: function () {},
                    clear: function () { cleared = true; refreshToken = null; }
                },
                metadata: { build: function () { return {}; } }
            },
            barkfluff: {
                IdentityApiClient: IdentityApiClient,
                MessagesApiClient: Client,
                UsersApiClient: Client,
                FilesApiClient: Client,
                UpdatesApiClient: Client,
                OnlinerApiClient: Client,
                FastAuthApiClient: Client,
                CallsApiClient: Client
            },
            proto: { barkfluff: { identity: { CreateTokenRequest: class { setRefreshToken() {} } } } }
        }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(file, 'utf8'), context);
    return { context: context, wasCleared: function () { return cleared; }, location: location };
}

async function main() {
    var clientsFile = path.join(__dirname, '../wwwroot/js/app/clients.js');
    var transientClients = loadModule(clientsFile, { code: 2, message: 'transport closed' });

    await assert.rejects(
        transientClients.context.window.BF.clients.authCall(function (_request, _metadata, callback) { callback(null, {}); }, {}),
        /token_refresh_unavailable/
    );
    assert.equal(transientClients.wasCleared(), false, 'temporary refresh error must preserve tokens');
    assert.equal(transientClients.location.href, '/messenger', 'temporary refresh error must not redirect to login');

    var invalidClients = loadModule(clientsFile, {
        code: 9,
        metadata: { 'x-error-code': INVALID_REFRESH_TOKEN }
    });
    await assert.rejects(
        invalidClients.context.window.BF.clients.authCall(function (_request, _metadata, callback) { callback(null, {}); }, {}),
        /no_token/
    );
    assert.equal(invalidClients.wasCleared(), true, 'invalid refresh token must clear tokens');
    assert.equal(invalidClients.location.href, '/', 'invalid refresh token must redirect to login');

    var authFile = path.join(__dirname, '../wwwroot/js/app/auth.js');
    var transientAuth = loadModule(authFile, { code: 2, message: 'transport closed' });
    assert.equal(await transientAuth.context.window.BF.auth.refreshToken(), null);
    assert.equal(transientAuth.wasCleared(), false, 'login-page refresh must preserve tokens on a temporary error');

    var invalidAuth = loadModule(authFile, {
        code: 9,
        metadata: { 'x-error-code': INVALID_REFRESH_TOKEN }
    });
    assert.equal(await invalidAuth.context.window.BF.auth.refreshToken(), null);
    assert.equal(invalidAuth.wasCleared(), true, 'login-page refresh must clear an invalid refresh token');

    console.log('PASS: refresh errors preserve sessions unless the server declares the refresh token invalid');
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
