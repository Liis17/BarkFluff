/* Server-synchronised drafts with a local durable outbox. */
(function () {
    'use strict';

    window.BF = window.BF || {};
    var userId = null;
    var entries = {};
    var timers = {};

    function key() { return BF.node.key('bf_chat_drafts_' + userId); }
    function save() { if (userId != null) localStorage.setItem(key(), JSON.stringify(entries)); }
    function hasContent(entry) { return !!(entry && !entry.deleted && ((entry.text || '').trim() || entry.replyToMessageId)); }

    function init(id) {
        userId = id;
        try { entries = JSON.parse(localStorage.getItem(key()) || '{}') || {}; } catch (_) { entries = {}; }
        window.addEventListener('online', flushAll);
        window.addEventListener('pagehide', save);
        flushAll();
    }

    function has(chatId) { return hasContent(entries[chatId]); }

    function set(chatId, text, replyToMessageId) {
        var entry = entries[chatId] || {};
        entry.text = text || '';
        entry.replyToMessageId = replyToMessageId || 0;
        entry.generation = (entry.generation || 0) + 1;
        entry.dirty = true;
        entries[chatId] = entry;
        entry.deleted = !hasContent(entry);
        save();
        if (timers[chatId]) clearTimeout(timers[chatId]);
        timers[chatId] = setTimeout(function () { flush(chatId); }, 2000);
    }

    function flush(chatId) {
        if (timers[chatId]) { clearTimeout(timers[chatId]); delete timers[chatId]; }
        var entry = entries[chatId];
        if (!entry || !entry.dirty) return Promise.resolve(entry || null);
        var generation = entry.generation;
        if (entry.deleted) {
            if (!entry.revision) { delete entries[chatId]; save(); return Promise.resolve(null); }
            entry.dirty = false;
            save();
            return BF.api.deleteChatDraft(chatId, entry.revision).then(function () {
                if (entries[chatId] === entry && entry.generation === generation) { delete entries[chatId]; save(); }
                return null;
            }).catch(function () { if (entries[chatId] === entry) { entry.dirty = true; save(); } return entry; });
        }
        var text = entry.text;
        var replyToMessageId = entry.replyToMessageId;
        entry.dirty = false;
        save();
        return BF.api.upsertChatDraft(chatId, text, replyToMessageId).then(function (data) {
            if (entries[chatId] !== entry) return entries[chatId];
            if (entry.generation !== generation) {
                entry.dirty = true;
                save();
                flush(chatId);
                return entry;
            }
            if (data && data.draft) {
                entry.revision = data.draft.revision;
                entry.dirty = false;
                save();
            }
            return entry;
        }).catch(function () {
            if (entries[chatId] === entry) { entry.dirty = true; save(); }
            return entry;
        });
    }

    function flushAll() { return Promise.all(Object.keys(entries).map(flush)); }

    function load(chatId) {
        var local = entries[chatId];
        if (local && local.dirty) { flush(chatId); return Promise.resolve(local); }
        return BF.api.getChatDraft(chatId).then(function (data) {
            if (!data || !data.draft) return local || null;
            entries[chatId] = {
                text: data.draft.text || '', replyToMessageId: data.draft.replyToMessageId || 0,
                revision: data.draft.revision || '', dirty: false
            };
            save();
            return entries[chatId];
        }).catch(function () { return local || null; });
    }

    function snapshot(chatId) {
        var entry = entries[chatId];
        return entry ? { generation: entry.generation, revision: entry.revision || '' } : null;
    }

    function clearSent(chatId, sent) {
        var entry = entries[chatId];
        if (!entry || !sent || entry.generation !== sent.generation) return;
        if (!entry.revision) {
            flush(chatId).then(function () { clearSent(chatId, sent); });
            return;
        }
        BF.api.deleteChatDraft(chatId, entry.revision).then(function () {
            if (entries[chatId] === entry && entry.generation === sent.generation) { delete entries[chatId]; save(); }
        }).catch(function () {});
    }

    function get(chatId) { return entries[chatId] || null; }

    window.BF.drafts = { init: init, has: has, get: get, snapshot: snapshot, set: set, flush: flush, load: load, clearSent: clearSent };
})();
