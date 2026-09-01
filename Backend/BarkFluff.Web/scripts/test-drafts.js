const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function deferred() {
    let resolve;
    let reject;
    const promise = new Promise((ok, fail) => { resolve = ok; reject = fail; });
    return { promise, resolve, reject };
}

async function tick() {
    await Promise.resolve();
    await Promise.resolve();
}

async function main() {
    const saved = new Map();
    const timers = [];
    const requests = [];
    const deleteRequests = [];
    const context = {
        Promise,
        JSON,
        localStorage: {
            getItem(key) { return saved.get(key) || null; },
            setItem(key, value) { saved.set(key, String(value)); }
        },
        setTimeout(callback, delay) {
            const timer = { callback, delay, cleared: false };
            timers.push(timer);
            return timer;
        },
        clearTimeout(timer) { if (timer) timer.cleared = true; },
        window: {
            addEventListener() {},
            BF: {
                node: { key: (name) => `${name}@node` },
                api: {
                    upsertChatDraft(chatId, text, replyId) {
                        const call = deferred();
                        requests.push({ chatId, text, replyId, call });
                        return call.promise;
                    },
                    deleteChatDraft() {
                        const call = deferred();
                        deleteRequests.push(call);
                        return call.promise;
                    },
                    getChatDraft() { return Promise.resolve({ draft: null }); }
                }
            }
        }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/drafts.js'), 'utf8'),
        context
    );

    const drafts = context.BF.drafts;
    drafts.init(42);
    drafts.set('chat', 'first', 0);
    const firstFlush = drafts.flush('chat');
    assert.equal(requests.length, 1);
    assert.equal(drafts.get('chat').dirty, true);
    assert.equal(JSON.parse(saved.get('bf_chat_drafts_42@node')).chat.dirty, true);

    drafts.set('chat', 'second', 0);
    assert.equal(drafts.flush('chat'), firstFlush);
    assert.equal(requests.length, 1, 'one in-flight request per chat');
    requests[0].call.resolve({ draft: { revision: 'r1' } });
    await firstFlush;
    assert.equal(drafts.get('chat').dirty, true, 'older ACK must not clean newer generation');

    const secondFlush = drafts.flush('chat');
    assert.equal(requests.length, 2);
    requests[1].call.resolve({ draft: { revision: 'r2' } });
    await secondFlush;
    assert.equal(drafts.get('chat').dirty, false);

    const sent = drafts.snapshot('chat');
    drafts.clearSent('chat', sent);
    deleteRequests[0].reject(new Error('offline'));
    await tick();
    assert.equal(drafts.get('chat').dirty, true, 'failed delete must remain queued');
    assert.equal(drafts.get('chat').deleted, true);
    const deleteRetry = timers.find((timer) => !timer.cleared && timer.delay === 2000);
    assert.ok(deleteRetry, 'failed delete must schedule a background retry');
    deleteRetry.cleared = true;
    deleteRetry.callback();
    await tick();
    assert.equal(deleteRequests.length, 2);
    deleteRequests[1].resolve({ deleted: true });
    await tick();
    assert.equal(drafts.get('chat'), null, 'successful retry removes the matching draft');

    drafts.set('failed', 'offline', 0);
    const failed = drafts.flush('failed');
    requests[2].call.reject(new Error('offline'));
    await failed;
    await tick();
    assert.equal(drafts.get('failed').dirty, true);
    assert.ok(timers.some((timer) => !timer.cleared && timer.delay === 2000));

    console.log('PASS: drafts stay dirty until matching ACK, serialize one in-flight sync, and retry in background');
}

main().catch((error) => {
    console.error('FAIL: ' + error.stack);
    process.exitCode = 1;
});
