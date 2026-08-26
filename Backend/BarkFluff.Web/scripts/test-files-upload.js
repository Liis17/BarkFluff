const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class FakeTarget {
    constructor() { this.listeners = {}; }
    addEventListener(name, callback) { (this.listeners[name] ||= []).push(callback); }
    emit(name, event = {}) { (this.listeners[name] || []).forEach((callback) => callback(event)); }
}

class FakeXhr extends FakeTarget {
    static requests = [];
    constructor() {
        super();
        this.upload = new FakeTarget();
        this.status = 0;
        this.responseText = '';
        this.aborted = false;
        FakeXhr.requests.push(this);
    }
    open(method, url) { this.method = method; this.url = url; }
    send(body) { this.body = body; }
    abort() { this.aborted = true; this.emit('abort'); }
    respond(status, body) {
        this.status = status;
        this.responseText = JSON.stringify(body);
        this.emit('load');
    }
}

function loadFiles() {
    FakeXhr.requests = [];
    const context = {
        AbortController,
        Error,
        FormData: class { append() {} },
        Map,
        Promise,
        URL,
        XMLHttpRequest: FakeXhr,
        clearTimeout,
        setTimeout,
        window: {
            BF: {
                api: {
                    getUploadUrl(_type, operationId) {
                        return Promise.resolve({ fileId: operationId || 'reserved' });
                    }
                },
                node: {
                    origin: () => 'https://node.test',
                    proxied: () => false,
                    meta: () => ({})
                }
            }
        }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/files.js'), 'utf8'), context);
    return context.BF.files;
}

const file = { name: 'file.bin', size: 3, type: 'application/octet-stream' };

async function wait(ms) { await new Promise((resolve) => setTimeout(resolve, ms)); }

async function main() {
    let files = loadFiles();
    assert.equal(files.matchesPendingUpload({
        name: 'file.bin',
        size: 3,
        type: 'application/octet-stream',
        uploadType: 5
    }, file), true);
    assert.equal(files.matchesPendingUpload({
        name: 'other.bin',
        size: 3,
        type: 'application/octet-stream',
        uploadType: 5
    }, file), false, 'a different file of the same upload type is rejected');

    const wall = files.uploadFile(file, 5, null, { operationId: 'wall', wallTimeoutMs: 10, stallTimeoutMs: 100 });
    await assert.rejects(wall, (error) => error.kind === 'timeout' && error.timeout === 'wall');
    assert.equal(FakeXhr.requests[0].aborted, true);

    files = loadFiles();
    const stalled = files.uploadFile(file, 5, null, { operationId: 'stall', wallTimeoutMs: 100, stallTimeoutMs: 10 });
    await Promise.resolve();
    FakeXhr.requests[0].upload.emit('progress', { lengthComputable: true, loaded: 1, total: 3 });
    await assert.rejects(stalled, (error) => error.kind === 'timeout' && error.timeout === 'stall');

    files = loadFiles();
    let settled = false;
    const completedBytes = files.uploadFile(file, 5, null, { operationId: 'complete', wallTimeoutMs: 100, stallTimeoutMs: 10 });
    completedBytes.finally(() => { settled = true; });
    await Promise.resolve();
    FakeXhr.requests[0].upload.emit('progress', { lengthComputable: true, loaded: 3, total: 3 });
    await wait(20);
    assert.equal(settled, false, 'stall timer is disabled after all bytes were sent');
    FakeXhr.requests[0].respond(200, { fileId: 'actual' });
    assert.equal(await completedBytes, 'actual');

    files = loadFiles();
    const group = new AbortController();
    const first = files.uploadFile(file, 5, null, { operationId: 'a', signal: group.signal });
    const second = files.uploadFile(file, 5, null, { operationId: 'b', signal: group.signal });
    await Promise.resolve();
    group.abort();
    await assert.rejects(first, (error) => error.kind === 'cancelled');
    await assert.rejects(second, (error) => error.kind === 'cancelled');
    assert.ok(FakeXhr.requests.every((request) => request.aborted));

    files = loadFiles();
    const retry = files.retryUpload(file, 5, { operationId: 'retry-op', reservedFileId: 'reserved' });
    assert.equal(FakeXhr.requests[0].method, 'GET');
    FakeXhr.requests[0].respond(200, { fileId: 'reserved', state: 'pending', retryAfterSeconds: 0 });
    await Promise.resolve();
    assert.equal(FakeXhr.requests[1].method, 'POST');
    FakeXhr.requests[1].respond(200, { fileId: 'actual' });
    assert.equal(await retry, 'actual');

    files = loadFiles();
    const alreadyDone = files.retryUpload(file, 5, { operationId: 'done-op', reservedFileId: 'reserved' });
    FakeXhr.requests[0].respond(200, { fileId: 'deduplicated', state: 'completed', retryAfterSeconds: 0 });
    assert.equal(await alreadyDone, 'deduplicated');
    assert.equal(FakeXhr.requests.length, 1, 'completed status never re-POSTs the file');

    files = loadFiles();
    const processing = files.retryUpload(file, 5, { operationId: 'busy-op', reservedFileId: 'reserved' });
    FakeXhr.requests[0].respond(409, { fileId: 'reserved', state: 'processing', retryAfterSeconds: 4 });
    await assert.rejects(processing, (error) =>
        error.kind === 'http' && error.state === 'processing' && error.retryAfterSeconds === 4);
    assert.equal(FakeXhr.requests.length, 1, 'processing status never re-POSTs the file');

    console.log('PASS: upload wall/stall bounds, group cancellation, and status-before-retry');
}

main().catch((error) => {
    console.error('FAIL: ' + error.stack);
    process.exitCode = 1;
});
