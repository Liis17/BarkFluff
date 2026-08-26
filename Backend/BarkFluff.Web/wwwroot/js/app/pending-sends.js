/* Durable metadata outbox for user-initiated sends. Binary File/Blob data is never stored. */
(function () {
    'use strict';

    window.BF = window.BF || {};
    var userId = null;
    var entries = {};

    function storageKey() {
        return BF.node.key('bf_pending_sends_' + userId);
    }

    function init(id) {
        userId = id;
        try {
            entries = JSON.parse(localStorage.getItem(storageKey()) || '{}') || {};
        } catch (_) {
            entries = {};
        }
    }

    function cleanUpload(upload) {
        return {
            operationId: upload.operationId || '',
            reservedFileId: upload.reservedFileId || '',
            resultFileId: upload.resultFileId || '',
            name: upload.name || '',
            size: Number(upload.size) || 0,
            type: upload.type || '',
            uploadType: Number(upload.uploadType) || 0,
            state: upload.state || 'pending'
        };
    }

    function clean(entry) {
        return {
            operationId: entry.operationId,
            chatId: entry.chatId,
            generation: Number(entry.generation) || 0,
            text: entry.text || '',
            caption: entry.caption || '',
            replyToMessageId: Number(entry.replyToMessageId) || 0,
            fileIds: Array.isArray(entry.fileIds) ? entry.fileIds.slice() : [],
            uploads: Array.isArray(entry.uploads) ? entry.uploads.map(cleanUpload) : [],
            state: entry.state || 'uploading',
            createdAt: Number(entry.createdAt) || Date.now(),
            updatedAt: Date.now()
        };
    }

    function persist(next) {
        try {
            localStorage.setItem(storageKey(), JSON.stringify(next));
            entries = next;
            return true;
        } catch (_) {
            return false;
        }
    }

    function put(entry) {
        if (!entry || !entry.operationId || userId == null) return false;
        var next = Object.assign({}, entries);
        next[entry.operationId] = clean(entry);
        return persist(next);
    }

    function update(operationId, patch) {
        var current = entries[operationId];
        if (!current) return false;
        return put(Object.assign({}, current, patch || {}, { operationId: operationId }));
    }

    function remove(operationId) {
        if (!entries[operationId]) return true;
        var next = Object.assign({}, entries);
        delete next[operationId];
        return persist(next);
    }

    function get(operationId) {
        return entries[operationId] || null;
    }

    function all() {
        return Object.keys(entries).map(function (operationId) { return entries[operationId]; });
    }

    window.BF.pendingSends = {
        init: init,
        put: put,
        update: update,
        remove: remove,
        get: get,
        all: all
    };
})();
