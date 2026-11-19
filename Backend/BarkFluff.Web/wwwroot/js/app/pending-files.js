/**
 * Binary outbox for small attachments of pending sends: keeps the selected File bytes in IndexedDB until the
 * send settles, so a reload can resume the upload without asking the user to pick the file again. Files over
 * the size limits, a full quota or a missing IndexedDB fall back to manual re-selection.
 * Every call resolves (false/null on failure) and never rejects.
 * Requires: BF.node
 * Exposes: BF.pendingFiles
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var MAX_FILE_BYTES = 20 * 1024 * 1024;
    var MAX_SEND_BYTES = 50 * 1024 * 1024;
    var STORE = 'files';
    var dbPromise = null;

    function init(userId) {
        dbPromise = null;
        if (typeof indexedDB === 'undefined' || !indexedDB || userId == null) return;
        dbPromise = new Promise(function (resolve) {
            try {
                var request = indexedDB.open(BF.node.key('bf_pending_files_' + userId), 1);
                request.onupgradeneeded = function () {
                    request.result.createObjectStore(STORE);
                };
                request.onsuccess = function () {
                    resolve(request.result);
                };
                request.onerror = function () {
                    resolve(null);
                };
            } catch (_) {
                resolve(null);
            }
        });
    }

    // Resolves { value } once the transaction commits, or null if it fails or aborts (QuotaExceededError
    // arrives as a transaction abort). Transactions are created in call order, so remove() never overtakes
    // an earlier put().
    function run(mode, action) {
        if (!dbPromise) return Promise.resolve(null);
        return dbPromise.then(function (db) {
            if (!db) return null;
            return new Promise(function (resolve) {
                var result = { value: null };
                try {
                    var tx = db.transaction(STORE, mode);
                    tx.oncomplete = function () {
                        resolve(result);
                    };
                    tx.onabort = tx.onerror = function () {
                        resolve(null);
                    };
                    action(tx.objectStore(STORE), result);
                } catch (_) {
                    resolve(null);
                }
            });
        });
    }

    /** Stores the small files of a send by their upload index; larger ones are kept as null slots. */
    function put(operationId, files) {
        var total = 0;
        var stored = 0;
        var record = (files || []).map(function (file) {
            if (!file || file.size > MAX_FILE_BYTES || total + file.size > MAX_SEND_BYTES) return null;
            total += file.size;
            stored++;
            return { blob: file, name: file.name, type: file.type, lastModified: file.lastModified };
        });
        if (!operationId || stored === 0) return Promise.resolve(false);
        return run('readwrite', function (store) {
            store.put(record, operationId);
        }).then(function (done) {
            return !!done;
        });
    }

    /** Returns an array of File|null by upload index, or null when nothing is stored. */
    function get(operationId) {
        return run('readonly', function (store, result) {
            var request = store.get(operationId);
            request.onsuccess = function () {
                result.value = request.result || null;
            };
        }).then(function (done) {
            if (!done || !Array.isArray(done.value)) return null;
            return done.value.map(function (item) {
                if (!item || !item.blob) return null;
                return new File([item.blob], item.name, { type: item.type, lastModified: item.lastModified });
            });
        });
    }

    function remove(operationId) {
        return run('readwrite', function (store) {
            store.delete(operationId);
        }).then(function (done) {
            return !!done;
        });
    }

    /** Deletes files left behind by sends that no longer exist in BF.pendingSends. */
    function prune(keepOperationIds) {
        return run('readwrite', function (store) {
            var request = store.getAllKeys();
            request.onsuccess = function () {
                request.result.forEach(function (key) {
                    if (keepOperationIds.indexOf(key) < 0) store.delete(key);
                });
            };
        }).then(function (done) {
            return !!done;
        });
    }

    window.BF.pendingFiles = {
        init: init,
        put: put,
        get: get,
        remove: remove,
        prune: prune
    };
})();
