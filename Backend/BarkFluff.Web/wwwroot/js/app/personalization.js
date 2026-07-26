/**
 * Chat personalization — local (per-device) cosmetic settings + applied background.
 * Mirrors Android GlobalParam / macOS @AppStorage: stored in localStorage,
 * applied to the real chat via CSS variables on :root, plus a backdrop layer.
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
        bgId:   'bf_pers_bg_file_id'
    };

    var DEFAULTS = {
        radius: 16,
        blurOn: false,
        blurR:  8,
        dim:    30,
        bgId:   ''
    };

    var listeners = [];
    var resolvedBgUrl = '';

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
        setCss('--chat-bg-blur', (blurOn ? blurR : 0) + 'px');
        // Затемнение нужно только поверх фоновой картинки; без неё — 0, иначе фон чата темнеет зря.
        var dimAlpha = resolvedBgUrl ? (Math.max(0, Math.min(100, dim)) / 100) : 0;
        setCss('--chat-bg-dim-alpha', dimAlpha.toFixed(3));
        setCss('--chat-bg-image', resolvedBgUrl ? ('url("' + resolvedBgUrl + '")') : 'none');

        listeners.forEach(function (cb) { try { cb(); } catch (e) {} });
    }

    function resolveBgUrl(fileId) {
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
            var u = urls && urls[0];
            resolvedBgUrl = u ? (u.url || u.previewUrl || '') : '';
            applyAll();
            return resolvedBgUrl;
        }).catch(function () {
            resolvedBgUrl = '';
            applyAll();
            return '';
        });
    }

    function init() {
        // Apply cached values immediately so UI doesn't flash defaults.
        applyAll();
        var bgId = readStr(KEYS.bgId, DEFAULTS.bgId);
        if (bgId) resolveBgUrl(bgId);

        // Reconcile with server: drop bgId if it's no longer in the user's collection.
        if (BF.api && BF.api.getPersonalization) {
            BF.api.getPersonalization().then(function (data) {
                var pers = (data && data.personalization) || {};
                var ids = pers.chatBackgroundFileIds || [];
                var cur = readStr(KEYS.bgId, '');
                if (cur && ids.indexOf(cur) < 0) {
                    localStorage.removeItem(KEYS.bgId);
                    resolveBgUrl('');
                }
            }).catch(function () {});
        }
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
    function getBackgroundFileId() { return readStr(KEYS.bgId, DEFAULTS.bgId); }
    function setBackgroundFileId(fileId) {
        if (fileId) localStorage.setItem(KEYS.bgId, fileId);
        else localStorage.removeItem(KEYS.bgId);
        return resolveBgUrl(fileId || '');
    }
    function getResolvedBackgroundUrl() { return resolvedBgUrl; }

    /** Стереть все локальные настройки персонализации — вызывается при логауте. */
    function clearAll() {
        Object.keys(KEYS).forEach(function (k) { localStorage.removeItem(KEYS[k]); });
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
        getBackgroundFileId: getBackgroundFileId,
        setBackgroundFileId: setBackgroundFileId,
        getResolvedBackgroundUrl: getResolvedBackgroundUrl,
        clearAll: clearAll,
        onChange: onChange,
        DEFAULTS: DEFAULTS
    };
})();
