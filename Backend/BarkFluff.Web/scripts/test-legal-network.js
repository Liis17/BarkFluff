const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function main() {
    const unaryCalls = [];
    class AcceptLegalConsentRequest {
        setRevision(value) { this.revision = value; }
    }
    class UsersApiClient {
        acceptLegalConsent(_request, _metadata, callback) { callback(); }
    }
    const context = {
        Promise,
        setTimeout,
        fetch,
        document: { cookie: 'bf_legal_accepted=revision-1' },
        window: {
            barkfluff: { UsersApiClient },
            proto: { barkfluff: { users: { AcceptLegalConsentRequest } } },
            BF: {
                node: { origin: () => 'https://node.test' },
                tokens: { getAccessToken: () => 'access-token' },
                metadata: { build: (token) => ({ token }) },
                network: {
                    unary(method, request, metadata, policy) {
                        unaryCalls.push({ method, request, metadata, policy });
                        return Promise.resolve({});
                    }
                }
            }
        }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/legal.js'), 'utf8'),
        context
    );

    await context.BF.legal.flushConsent();

    assert.equal(unaryCalls.length, 1, 'legal consent must use the bounded unary transport');
    assert.equal(unaryCalls[0].request.revision, 'revision-1');
    assert.equal(unaryCalls[0].metadata.token, 'access-token');
    assert.equal(unaryCalls[0].policy.attemptTimeoutMs, 1500);
    assert.equal(unaryCalls[0].policy.maxAttempts, 1);
    console.log('PASS: legal consent uses a bounded cancellable unary call');
}

main().catch((error) => {
    console.error('FAIL: ' + error.stack);
    process.exitCode = 1;
});
