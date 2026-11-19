const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Minimal in-memory IndexedDB: one object store with out-of-line keys, transactions that complete
// asynchronously and abort when a write exceeds the configured quota (as QuotaExceededError does).
function fakeIndexedDB(options) {
    options = options || {};
    const databases = new Map();

    function recordSize(value) {
        return (Array.isArray(value) ? value : []).filter(Boolean).reduce((sum, item) => sum + item.blob.size, 0);
    }

    function openDb(name) {
        if (!databases.has(name)) databases.set(name, { stores: new Map(), version: 0 });
        const data = databases.get(name);
        return {
            data,
            createObjectStore(store) {
                data.stores.set(store, new Map());
            },
            transaction(store, mode) {
                const rows = data.stores.get(store);
                const pending = new Map(rows);
                let failed = false;
                const tx = {};
                const objectStore = {
                    put(value, key) {
                        assert.equal(mode, 'readwrite');
                        if (options.quota != null && recordSize(value) > options.quota) failed = true;
                        pending.set(key, value);
                    },
                    delete(key) {
                        assert.equal(mode, 'readwrite');
                        pending.delete(key);
                    },
                    get(key) {
                        const request = {};
                        setImmediate(() => {
                            request.result = pending.get(key);
                            if (request.onsuccess) request.onsuccess();
                        });
                        return request;
                    },
                    getAllKeys() {
                        const request = {};
                        setImmediate(() => {
                            request.result = Array.from(pending.keys());
                            if (request.onsuccess) request.onsuccess();
                        });
                        return request;
                    }
                };
                tx.objectStore = () => objectStore;
                // Let request callbacks queued in this turn run first, then commit or abort.
                setImmediate(() =>
                    setImmediate(() => {
                        if (failed) return tx.onabort && tx.onabort();
                        rows.clear();
                        pending.forEach((value, key) => rows.set(key, value));
                        if (tx.oncomplete) tx.oncomplete();
                    })
                );
                return tx;
            }
        };
    }

    return {
        databases,
        open(name, version) {
            const request = {};
            setImmediate(() => {
                const db = openDb(name);
                request.result = db;
                if (db.data.version < version) {
                    db.data.version = version;
                    if (request.onupgradeneeded) request.onupgradeneeded();
                }
                if (request.onsuccess) request.onsuccess();
            });
            return request;
        }
    };
}

function loadStore(indexedDB, userId) {
    const context = {
        File,
        Promise,
        indexedDB,
        window: { BF: { node: { key: (name) => `${name}@node` } } }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/pending-files.js'), 'utf8'), context);
    context.BF.pendingFiles.init(userId === undefined ? 42 : userId);
    return context.BF.pendingFiles;
}

function makeFile(name, size, type) {
    return new File([new Uint8Array(size)], name, { type: type || 'image/png', lastModified: 1000 });
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('stored files survive a reload and come back as Files by upload index', async function () {
        const idb = fakeIndexedDB();
        let store = loadStore(idb);
        assert.equal(await store.put('send-1', [makeFile('a.png', 10), makeFile('b.txt', 5, 'text/plain')]), true);

        store = loadStore(idb);
        const files = await store.get('send-1');
        assert.equal(files.length, 2);
        assert.ok(files[0] instanceof File);
        assert.equal(files[0].name, 'a.png');
        assert.equal(files[0].size, 10);
        assert.equal(files[0].type, 'image/png');
        assert.equal(files[1].name, 'b.txt');
        assert.deepEqual(Array.from(idb.databases.keys()), ['bf_pending_files_42@node']);
    });

    await test('files over the per-file or per-send limit keep a null slot', async function () {
        const store = loadStore(fakeIndexedDB());
        const mb = 1024 * 1024;
        await store.put('send-1', [
            makeFile('big.mov', 21 * mb, 'video/quicktime'),
            makeFile('a.png', 10),
            makeFile('c.bin', 20 * mb, ''),
            makeFile('d.bin', 20 * mb, ''),
            makeFile('e.bin', 20 * mb, '')
        ]);
        const files = await store.get('send-1');
        assert.deepEqual(
            files.map((f) => f && f.name),
            [null, 'a.png', 'c.bin', 'd.bin', null]
        );
    });

    await test('a send with only large files stores nothing', async function () {
        const store = loadStore(fakeIndexedDB());
        assert.equal(await store.put('send-1', [makeFile('big.mov', 21 * 1024 * 1024)]), false);
        assert.equal(await store.get('send-1'), null);
    });

    await test('an exhausted quota aborts the write and resolves false instead of rejecting', async function () {
        const store = loadStore(fakeIndexedDB({ quota: 5 }));
        assert.equal(await store.put('send-1', [makeFile('a.png', 10)]), false);
        assert.equal(await store.get('send-1'), null);
    });

    await test('remove issued right after put is applied after it', async function () {
        const store = loadStore(fakeIndexedDB());
        const put = store.put('send-1', [makeFile('a.png', 10)]);
        const removed = store.remove('send-1');
        assert.equal(await put, true);
        assert.equal(await removed, true);
        assert.equal(await store.get('send-1'), null);
    });

    await test('prune drops files of sends that no longer exist', async function () {
        const store = loadStore(fakeIndexedDB());
        await store.put('keep', [makeFile('a.png', 10)]);
        await store.put('orphan', [makeFile('b.png', 10)]);
        assert.equal(await store.prune(['keep']), true);
        assert.notEqual(await store.get('keep'), null);
        assert.equal(await store.get('orphan'), null);
    });

    await test('without IndexedDB every call resolves to a no-op result', async function () {
        const store = loadStore(undefined);
        assert.equal(await store.put('send-1', [makeFile('a.png', 10)]), false);
        assert.equal(await store.get('send-1'), null);
        assert.equal(await store.remove('send-1'), false);
        assert.equal(await store.prune([]), false);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
