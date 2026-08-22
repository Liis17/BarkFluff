/**
 * Chat personalization — local cosmetic settings plus synced background selection.
 *
 * Requires: BF.api, BF.files
 * Exposes:  BF.personalization
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var KEYS = {
        radius: 'bf_pers_bubble_radius',
        blurOn: 'bf_pers_bg_blur_enabled',
        blurR:  'bf_pers_bg_blur_radius',
        dim:    'bf_pers_bg_dim',
        stickerSize: 'bf_pers_sticker_size'
    };

    var DEFAULTS = {
        radius: 16,
        blurOn: false,
        blurR:  8,
        dim:    30,
        stickerSize: 160
    };

    var listeners = [];
    var resolvedBgUrl = '';
    var globalBackgroundFileId = '';
    var chatBackgroundFileIds = new Map();
    var activeChatId = '';
    var activeBackgroundFileId = '';
    var resolveVersion = 0;
    var lastResolvedAt = 0;

    /**
     * Temp-ссылки сервера живут ограниченное время, поэтому URL фона нельзя
     * резолвить один раз: в долго открытой вкладке ссылка протухнет, браузер
     * пере-запросит картинку и получит 404 — фон пропадёт. Пере-резолвим
     * не чаще, чем раз в столько миллисекунд (совпадает с TTL кеша BF.files,
     * поэтому getFileUrls вернёт свежую ссылку).
     */
    var BG_URL_REFRESH_MS = 30 * 60 * 1000;

    function readInt(key, def) {
        var v = localStorage.getItem(key);
        var n = parseInt(v, 10);
        return isFinite(n) ? n : def;
    }
    function readBool(key, def) {
        var v = localStorage.getItem(key);
        if (v === null) return def;
        return v === '1';
    }
    function readStr(key, def) {
        var v = localStorage.getItem(key);
        return v === null ? def : v;
    }

    function setCss(name, value) {
        document.documentElement.style.setProperty(name, value);
    }

    function applyAll() {
        var radius = readInt(KEYS.radius, DEFAULTS.radius);
        var blurOn = readBool(KEYS.blurOn, DEFAULTS.blurOn);
        var blurR  = readInt(KEYS.blurR,  DEFAULTS.blurR);
        var dim    = readInt(KEYS.dim,    DEFAULTS.dim);

        setCss('--msg-bubble-radius', radius + 'px');
        setCss('--sticker-size', readInt(KEYS.stickerSize, DEFAULTS.stickerSize) + 'px');
        setCss('--chat-bg-blur', (blurOn ? blurR : 0) + 'px');
        // Затемнение нужно только поверх фоновой картинки; без неё — 0, иначе фон чата темнеет зря.
        var dimAlpha = resolvedBgUrl ? (Math.max(0, Math.min(100, dim)) / 100) : 0;
        setCss('--chat-bg-dim-alpha', dimAlpha.toFixed(3));
        setCss('--chat-bg-image', resolvedBgUrl ? ('url("' + resolvedBgUrl + '")') : 'none');

        listeners.forEach(function (cb) { try { cb(); } catch (e) {} });
    }

    function resolveBgUrl(fileId) {
        var version = ++resolveVersion;
        if (!fileId) {
            resolvedBgUrl = '';
            applyAll();
            return Promise.resolve('');
        }
        if (!window.BF || !BF.files || !BF.files.getFileUrls) {
            resolvedBgUrl = '';
            applyAll();
            return Promise.resolve('');
        }
        return BF.files.getFileUrls([fileId]).then(function (urls) {
            lastResolvedAt = Date.now();
            if (version !== resolveVersion) return '';
            var u = urls && urls[0];
            resolvedBgUrl = u ? (u.url || u.previewUrl || '') : '';
            applyAll();
            return resolvedBgUrl;
        }).catch(function () {
            lastResolvedAt = Date.now();
            if (version !== resolveVersion) return '';
            resolvedBgUrl = '';
            applyAll();
            return '';
        });
    }

    /** Пере-резолвить активный фон, если его URL мог протухнуть. */
    function refreshActiveBackgroundIfStale() {
        if (!activeBackgroundFileId) return;
        if (Date.now() - lastResolvedAt < BG_URL_REFRESH_MS) return;
        resolveBgUrl(activeBackgroundFileId);
    }

    function init() {
        // Background choice is server-owned; deliberately ignore the legacy local key.
        applyAll();
        // Периодический и «по пробуждении вкладки» пере-резолв фона: temp-ссылки
        // протухают, а CSS-фон не умеет сообщать об ошибке загрузки.
        setInterval(refreshActiveBackgroundIfStale, 60 * 1000);
        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'visible') refreshActiveBackgroundIfStale();
        });
        return reloadSettings();
    }

    function getRadius() { return readInt(KEYS.radius, DEFAULTS.radius); }
    function setRadius(v) {
        localStorage.setItem(KEYS.radius, String(v));
        applyAll();
    }
    function getBlurEnabled() { return readBool(KEYS.blurOn, DEFAULTS.blurOn); }
    function setBlurEnabled(v) {
        localStorage.setItem(KEYS.blurOn, v ? '1' : '0');
        applyAll();
    }
    function getBlurRadius() { return readInt(KEYS.blurR, DEFAULTS.blurR); }
    function setBlurRadius(v) {
        localStorage.setItem(KEYS.blurR, String(v));
        applyAll();
    }
    function getDim() { return readInt(KEYS.dim, DEFAULTS.dim); }
    function setDim(v) {
        localStorage.setItem(KEYS.dim, String(v));
        applyAll();
    }
    function getStickerSize() { return readInt(KEYS.stickerSize, DEFAULTS.stickerSize); }
    function setStickerSize(v) {
        localStorage.setItem(KEYS.stickerSize, String(v));
        applyAll();
    }
    function applyForChat(chatId) {
        activeChatId = chatId || '';
        activeBackgroundFileId = chatBackgroundFileIds.get(activeChatId) || globalBackgroundFileId;
        return resolveBgUrl(activeBackgroundFileId);
    }
    function getBackgroundFileId() { return globalBackgroundFileId; }
    function getChatBackgroundFileId(chatId) {
        return chatBackgroundFileIds.get(chatId || '') || '';
    }
    function setBackgroundFileId(fileId) {
        return BF.api.setGlobalChatBackground(fileId || '').then(function () {
            globalBackgroundFileId = fileId || '';
            return applyForChat(activeChatId);
        });
    }
    function setChatBackgroundFileId(chatId, fileId) {
        return BF.api.setChatBackground(chatId, fileId || '').then(function () {
            if (fileId) chatBackgroundFileIds.set(chatId, fileId);
            else chatBackgroundFileIds.delete(chatId);
            if (activeChatId === chatId) return applyForChat(chatId);
        });
    }
    function reloadSettings() {
        if (!BF.api || !BF.api.getUserSettings) return Promise.resolve();
        return BF.api.getUserSettings().then(function (data) {
            var settings = (data && data.settings) || {};
            globalBackgroundFileId = settings.globalChatBackgroundFileId || '';
            chatBackgroundFileIds.clear();
            (settings.chatBackgrounds || []).forEach(function (item) {
                if (item.chatId && item.chatBackgroundFileId) {
                    chatBackgroundFileIds.set(item.chatId, item.chatBackgroundFileId);
                }
            });
            return applyForChat(activeChatId);
        });
    }
    function getResolvedBackgroundUrl() { return resolvedBgUrl; }

    /** Стереть все локальные настройки персонализации — вызывается при логауте. */
    function clearAll() {
        Object.keys(KEYS).forEach(function (k) { localStorage.removeItem(KEYS[k]); });
        globalBackgroundFileId = '';
        chatBackgroundFileIds.clear();
        activeChatId = '';
        activeBackgroundFileId = '';
        resolveBgUrl('');
        applyAll();
    }

    function onChange(cb) {
        if (typeof cb === 'function') listeners.push(cb);
        return function () {
            var i = listeners.indexOf(cb);
            if (i >= 0) listeners.splice(i, 1);
        };
    }

    window.BF.personalization = {
        init: init,
        applyAll: applyAll,
        getRadius: getRadius, setRadius: setRadius,
        getBlurEnabled: getBlurEnabled, setBlurEnabled: setBlurEnabled,
        getBlurRadius: getBlurRadius, setBlurRadius: setBlurRadius,
        getDim: getDim, setDim: setDim,
        getStickerSize: getStickerSize, setStickerSize: setStickerSize,
        getBackgroundFileId: getBackgroundFileId,
        setBackgroundFileId: setBackgroundFileId,
        getChatBackgroundFileId: getChatBackgroundFileId,
        setChatBackgroundFileId: setChatBackgroundFileId,
        applyForChat: applyForChat,
        reloadSettings: reloadSettings,
        getResolvedBackgroundUrl: getResolvedBackgroundUrl,
        clearAll: clearAll,
        onChange: onChange,
        DEFAULTS: DEFAULTS
    };
})();
