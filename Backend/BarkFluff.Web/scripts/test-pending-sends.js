const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadStore(storage) {
    const context = {
        JSON,
        Date,
        localStorage: storage,
        window: { BF: { node: { key: (name) => `${name}@node` } } }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/pending-sends.js'), 'utf8'),
        context
    );
    context.BF.pendingSends.init(42);
    return context.BF.pendingSends;
}

function memoryStorage() {
    const values = new Map();
    return {
        getItem(key) { return values.has(key) ? values.get(key) : null; },
        setItem(key, value) { values.set(key, String(value)); },
        removeItem(key) { values.delete(key); }
    };
}

function snapshot() {
    return {
        operationId: 'send-op',
        chatId: 'chat-1',
        generation: 3,
        text: 'hello',
        caption: 'caption',
        replyToMessageId: 12,
        fileIds: ['file-1'],
        uploads: [{
            operationId: 'upload-op',
            reservedFileId: 'reserved-1',
            file: { shouldNotPersist: true },
            name: 'photo.jpg',
            size: 100,
            type: 'image/jpeg',
            uploadType: 2,
            state: 'pending'
        }]
    };
}

function main() {
    const storage = memoryStorage();
    let store = loadStore(storage);
    assert.equal(store.put(snapshot()), true);

    store = loadStore(storage);
    const restored = store.get('send-op');
    assert.equal(restored.text, 'hello');
    assert.deepEqual(Array.from(restored.fileIds), ['file-1']);
    assert.equal(restored.uploads[0].reservedFileId, 'reserved-1');
    assert.equal('file' in restored.uploads[0], false);

    const failing = loadStore({
        getItem() { return null; },
        setItem() { throw new Error('quota'); },
        removeItem() {}
    });
    assert.equal(failing.put(snapshot()), false);
    assert.equal(failing.get('send-op'), null);

    console.log('PASS: pending sends survive reload, reject failed persistence, and never serialize File');
}

main();
