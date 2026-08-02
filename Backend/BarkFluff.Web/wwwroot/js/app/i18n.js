/**
 * i18n — локализация интерфейса: выбор языка (cookie -> браузер), словари, применение к DOM.
 * Подключается первым скриптом в <head>, до остальных модулей.
 * Exposes: BF.i18n
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var COOKIE = 'bf_lang';
    var BASE_LANG = 'ru';                 // исходный язык разметки, fallback для отсутствующих ключей
    var DEFAULT_LANG = 'en';              // если язык браузера не поддерживается
    var PENDING_CLASS = 'i18n-pending';   // скрывает body, пока словарь не применён
    var REVEAL_TIMEOUT = 3000;

    var LANGS = [
        { code: 'ru', name: 'Русский', englishName: 'Russian' },
        { code: 'en', name: 'English', englishName: 'English' },
        { code: 'es', name: 'Español', englishName: 'Spanish' },
        { code: 'de', name: 'Deutsch', englishName: 'German' },
        { code: 'zh-Hans', name: '简体中文', englishName: 'Chinese (Simplified)' }
    ];

    var bundles = {};        // lang -> { key: text }
    var currentLang = BASE_LANG;
    var pluralRules = null;
    var revealed = false;
    var listeners = [];

    // ========== ВЫБОР ЯЗЫКА ==========

    function isSupported(code) {
        return LANGS.some(function (l) { return l.code === code; });
    }

    // 'ru-RU' -> 'ru', 'zh-CN' / 'zh-Hans-CN' -> 'zh-Hans'. Традиционный китайский не поддерживается.
    function normalize(tag) {
        if (!tag) return null;
        var lower = String(tag).toLowerCase();
        if (lower.indexOf('zh') === 0) {
            if (lower.indexOf('hant') !== -1 || lower.indexOf('tw') !== -1 ||
                lower.indexOf('hk') !== -1 || lower.indexOf('mo') !== -1) return null;
            return 'zh-Hans';
        }
        var primary = lower.split('-')[0];
        return isSupported(primary) ? primary : null;
    }

    function readCookie() {
        var m = document.cookie.match('(?:^|; )' + COOKIE + '=([^;]*)');
        return m ? decodeURIComponent(m[1]) : null;
    }

    function writeCookie(code) {
        document.cookie = COOKIE + '=' + encodeURIComponent(code) +
            '; path=/; max-age=31536000; SameSite=Lax';
    }

    function detect() {
        var saved = readCookie();
        if (saved && isSupported(saved)) return saved;
        var tags = navigator.languages && navigator.languages.length
            ? navigator.languages
            : [navigator.language];
        for (var i = 0; i < tags.length; i++) {
            var code = normalize(tags[i]);
            if (code) return code;
        }
        return DEFAULT_LANG;
    }

    // ========== ЗАГРУЗКА СЛОВАРЕЙ ==========

    function loadBundle(lang) {
        if (bundles[lang]) return Promise.resolve(bundles[lang]);
        return fetch('/js/i18n/' + lang + '.json')
            .then(function (r) { return r.ok ? r.json() : {}; })
            .catch(function () { return {}; })
            .then(function (data) {
                bundles[lang] = data || {};
                return bundles[lang];
            });
    }

    // Нужный язык + базовый (ru) как fallback для ещё не переведённых ключей.
    function loadFor(lang) {
        var tasks = [loadBundle(lang)];
        if (lang !== BASE_LANG) tasks.push(loadBundle(BASE_LANG));
        return Promise.all(tasks);
    }

    // ========== ПЕРЕВОД ==========

    function lookup(key) {
        var own = bundles[currentLang];
        if (own && own[key] != null) return own[key];
        var base = bundles[BASE_LANG];
        if (base && base[key] != null) return base[key];
        return null;
    }

    function format(text, params) {
        if (!params) return text;
        return text.replace(/\{(\w+)\}/g, function (match, name) {
            return params[name] != null ? String(params[name]) : match;
        });
    }

    function t(key, params) {
        var text = lookup(key);
        return text == null ? key : format(text, params);
    }

    // Множественное число: ключи key.one / key.few / key.many / key.other (набор форм зависит от языка).
    function tp(key, count, params) {
        if (!pluralRules) pluralRules = new Intl.PluralRules(currentLang);
        var form = pluralRules.select(count);
        var text = lookup(key + '.' + form);
        if (text == null) text = lookup(key + '.other');
        if (text == null) return key;
        var merged = { count: count };
        if (params) {
            for (var name in params) {
                if (Object.prototype.hasOwnProperty.call(params, name)) merged[name] = params[name];
            }
        }
        return format(text, merged);
    }

    // ========== ПРИМЕНЕНИЕ К DOM ==========

    var ATTRS = [
        { data: 'data-i18n-placeholder', attr: 'placeholder' },
        { data: 'data-i18n-title', attr: 'title' },
        { data: 'data-i18n-aria-label', attr: 'aria-label' },
        { data: 'data-i18n-value', attr: 'value' },
        { data: 'data-i18n-alt', attr: 'alt' },
        { data: 'data-i18n-content', attr: 'content' }
    ];

    function applyTo(el) {
        var key = el.getAttribute('data-i18n');
        if (key) el.textContent = t(key);
        var html = el.getAttribute('data-i18n-html');
        if (html) el.innerHTML = t(html);
        ATTRS.forEach(function (item) {
            var attrKey = el.getAttribute(item.data);
            if (attrKey) el.setAttribute(item.attr, t(attrKey));
        });
    }

    function apply(root) {
        var scope = root || document;
        var selector = '[data-i18n],[data-i18n-html],' +
            ATTRS.map(function (item) { return '[' + item.data + ']'; }).join(',');
        if (scope.nodeType === 1 && scope.matches && scope.matches(selector)) applyTo(scope);
        var nodes = scope.querySelectorAll(selector);
        for (var i = 0; i < nodes.length; i++) applyTo(nodes[i]);
    }

    function reveal() {
        if (revealed) return;
        revealed = true;
        document.documentElement.classList.remove(PENDING_CLASS);
    }

    function applyAll() {
        document.documentElement.setAttribute('lang', currentLang);
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', function () { apply(); reveal(); });
        } else {
            apply();
            reveal();
        }
    }

    // ========== ПЕРЕКЛЮЧЕНИЕ ==========

    function onChange(cb) {
        listeners.push(cb);
    }

    function emitChange() {
        var event;
        try {
            event = new CustomEvent('bf:langchange', { detail: { lang: currentLang } });
        } catch (e) {
            event = document.createEvent('CustomEvent');
            event.initCustomEvent('bf:langchange', false, false, { lang: currentLang });
        }
        document.dispatchEvent(event);
        listeners.forEach(function (cb) {
            try { cb(currentLang); } catch (err) { /* один подписчик не должен ломать остальные */ }
        });
    }

    function setLang(code) {
        if (!isSupported(code) || code === currentLang) return Promise.resolve();
        return loadFor(code).then(function () {
            currentLang = code;
            pluralRules = null;
            writeCookie(code);
            document.documentElement.setAttribute('lang', code);
            apply();
            emitChange();
        });
    }

    // ========== СТАРТ ==========

    document.documentElement.classList.add(PENDING_CLASS);
    setTimeout(reveal, REVEAL_TIMEOUT);   // страховка: не оставлять страницу скрытой при сбое загрузки

    currentLang = detect();
    var ready = loadFor(currentLang).then(applyAll);

    window.BF.i18n = {
        ready: ready,
        langs: function () { return LANGS.slice(); },
        current: function () { return currentLang; },
        setLang: setLang,
        onChange: onChange,
        apply: apply,
        t: t,
        tp: tp
    };
})();
