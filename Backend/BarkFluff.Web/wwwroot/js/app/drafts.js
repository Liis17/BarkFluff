/* Server-synchronised drafts with a durable local outbox. */
(function () {
    'use strict';

    window.BF = window.BF || {};
    var userId = null;
    var entries = {};
    var debounceTimers = {};
    var retryTimers = {};
    var retryDelays = {};
    var inFlight = {};

    function key() { return BF.node.key('bf_chat_drafts_' + userId); }

    function save() {
        if (userId == null) return false;
        try {
            localStorage.setItem(key(), JSON.stringify(entries));
            return true;
        } catch (_) {
            return false;
        }
    }

    function hasDraftContent(entry) {
        return !!(entry && ((entry.text || '').trim() || entry.replyToMessageId));
    }

    function hasContent(entry) {
        return !!(entry && !entry.deleted && hasDraftContent(entry));
    }

    function init(id) {
        userId = id;
        try { entries = JSON.parse(localStorage.getItem(key()) || '{}') || {}; } catch (_) { entries = {}; }
        window.addEventListener('online', flushAll);
        window.addEventListener('pagehide', save);
        flushAll();
    }

    function has(chatId) { return hasContent(entries[chatId]); }

    function scheduleDebounce(chatId, delay) {
        if (debounceTimers[chatId]) clearTimeout(debounceTimers[chatId]);
        debounceTimers[chatId] = setTimeout(function () {
            delete debounceTimers[chatId];
            flush(chatId);
        }, delay);
    }

    function scheduleRetry(chatId) {
        if (retryTimers[chatId]) return;
        var delay = retryDelays[chatId] || 2000;
        retryDelays[chatId] = Math.min(delay * 2, 30000);
        retryTimers[chatId] = setTimeout(function () {
            delete retryTimers[chatId];
            flush(chatId);
        }, delay);
    }

    function resetRetry(chatId) {
        retryDelays[chatId] = 2000;
        if (retryTimers[chatId]) {
            clearTimeout(retryTimers[chatId]);
            delete retryTimers[chatId];
        }
    }

    function set(chatId, text, replyToMessageId) {
        var entry = entries[chatId] || {};
        entry.text = text || '';
        entry.replyToMessageId = replyToMessageId || 0;
        entry.generation = (entry.generation || 0) + 1;
        entry.dirty = true;
        entry.deleted = !hasDraftContent(entry);
        entries[chatId] = entry;
        save();
        scheduleDebounce(chatId, 2000);
    }

    function flush(chatId) {
        if (debounceTimers[chatId]) {
            clearTimeout(debounceTimers[chatId]);
            delete debounceTimers[chatId];
        }
        if (inFlight[chatId]) return inFlight[chatId];

        var entry = entries[chatId];
        if (!entry || !entry.dirty) return Promise.resolve(entry || null);
        var generation = entry.generation;
        var request;

        if (entry.deleted) {
            if (!entry.revision) {
                delete entries[chatId];
                save();
                return Promise.resolve(null);
            }
            request = BF.api.deleteChatDraft(chatId, entry.revision);
        } else {
            request = BF.api.upsertChatDraft(chatId, entry.text, entry.replyToMessageId);
        }

        var failed = false;
        var task = request.then(function (data) {
            if (entries[chatId] !== entry) return entries[chatId] || null;
            if (data && data.draft && data.draft.revision) entry.revision = data.draft.revision;
            if (entry.generation !== generation) {
                entry.dirty = true;
                save();
                return entry;
            }
            resetRetry(chatId);
            if (entry.deleted) {
                delete entries[chatId];
                save();
                return null;
            }
            entry.dirty = false;
            save();
            return entry;
        }).catch(function () {
            failed = true;
            if (entries[chatId] === entry) {
                entry.dirty = true;
                save();
                scheduleRetry(chatId);
            }
            return entry;
        }).finally(function () {
            if (inFlight[chatId] === task) delete inFlight[chatId];
            var current = entries[chatId];
            if (!failed && current && current.dirty && !debounceTimers[chatId] && !retryTimers[chatId]) {
                scheduleDebounce(chatId, 0);
            }
        });
        inFlight[chatId] = task;
        return task;
    }

    function flushAll() { return Promise.all(Object.keys(entries).map(flush)); }

    function load(chatId) {
        var local = entries[chatId];
        if (local && local.dirty) {
            flush(chatId);
            return Promise.resolve(local);
        }
        return BF.api.getChatDraft(chatId).then(function (data) {
            if (!data || !data.draft) return local || null;
            entries[chatId] = {
                text: data.draft.text || '',
                replyToMessageId: data.draft.replyToMessageId || 0,
                revision: data.draft.revision || '',
                generation: local ? local.generation || 0 : 0,
                dirty: false,
                deleted: false
            };
            save();
            return entries[chatId];
        }).catch(function () { return local || null; });
    }

    function snapshot(chatId) {
        var entry = entries[chatId];
        return entry ? {
            generation: entry.generation || 0,
            revision: entry.revision || '',
            text: entry.text || '',
            replyToMessageId: entry.replyToMessageId || 0
        } : null;
    }

    function clearSent(chatId, sent) {
        var entry = entries[chatId];
        if (!entry || !sent || entry.generation !== sent.generation) return;
        entry.generation = (entry.generation || 0) + 1;
        entry.deleted = true;
        entry.dirty = true;
        save();
        flush(chatId);
    }

    function get(chatId) { return entries[chatId] || null; }

    window.BF.drafts = {
        init: init,
        has: has,
        get: get,
        snapshot: snapshot,
        set: set,
        flush: flush,
        load: load,
        clearSent: clearSent
    };
})();
