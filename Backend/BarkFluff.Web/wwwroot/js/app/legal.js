/**
 * Согласие с документами и уведомление об использовании cookie (index.html).
 * Повторяет схему Android (LegalConsentBottomSheet + GlobalParam.acceptedLegalRevision):
 * хранится не флаг, а редакция документа, поэтому после его обновления согласие
 * запрашивается заново.
 *
 * Requires: BF.utils (renderMarkdown), BF.metadata, BF.tokens, barkfluff.bundle.js
 * Exposes: BF.legal
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var COOKIE = 'bf_legal_accepted';
    var DOCS = {
        terms: { file: '/legal/TERMS_OF_SERVICE.ru.md', titleKey: 'legal.terms' },
        privacy: { file: '/legal/PRIVACY_POLICY.ru.md', titleKey: 'legal.privacy' }
    };

    /* Редакция читается из русского оригинала соглашения: остальные локали — переводы,
       и смена языка не должна выглядеть новой редакцией (как в LegalDocsRepository). */
    var HEADER_LINES = 8;
    var REVISION_LINE = /^\*\*.+?\*\*\s*(.+?)\s*$/;

    /** Предел ожидания записи согласия на сервере перед переходом в мессенджер. */
    var FLUSH_TIMEOUT = 1500;

    var revision = '';
    var cache = {};
    var overlay, body, title, tabs;

    // ─────────────── документы ───────────────

    function load(doc) {
        if (cache[doc]) return Promise.resolve(cache[doc]);
        return fetch(DOCS[doc].file).then(function (r) {
            if (!r.ok) throw new Error('legal_fetch_failed');
            return r.text();
        }).then(function (text) {
            cache[doc] = text;
            return text;
        });
    }

    function parseRevision(text) {
        var lines = text.replace(/\r\n?/g, '\n').split('\n').slice(0, HEADER_LINES);
        var found = '';
        lines.forEach(function (line) {
            var m = REVISION_LINE.exec(line);
            if (m) found = m[1];
        });
        return found;
    }

    // ─────────────── cookie ───────────────

    function readCookie() {
        var m = document.cookie.match(/(?:^|;\s*)bf_legal_accepted=([^;]*)/);
        return m ? decodeURIComponent(m[1]) : '';
    }

    function isAccepted() {
        var saved = readCookie();
        if (!saved) return false;
        // Редакцию ещё не прочитали (или документ недоступен) — не требуем согласия повторно.
        return !revision || saved === revision;
    }

    function accept() {
        document.cookie = COOKIE + '=' + encodeURIComponent(revision || 'unknown')
            + '; path=/; max-age=31536000; SameSite=Lax';
        flushConsent();
    }

    // ─────────────── фиксация на сервере ───────────────

    /**
     * Пишет принятую редакцию в профиль. Вызывается после появления токена: до входа
     * вызвать RPC нечем, поэтому серверная запись всегда идёт следом за согласием,
     * а не перед ним.
     *
     * Промис резолвится всегда и не дольше FLUSH_TIMEOUT: вызывающий редиректит на
     * мессенджер сразу после него, и медленная сеть не должна задерживать вход.
     * Ошибка тоже не критична — согласие уже зафиксировано в cookie.
     */
    function flushConsent() {
        var saved = readCookie();
        var token = BF.tokens && BF.tokens.getAccessToken && BF.tokens.getAccessToken();
        if (!token || !saved) return Promise.resolve();

        var usrPb = window.proto && window.proto.barkfluff && window.proto.barkfluff.users;
        if (!usrPb || !usrPb.AcceptLegalConsentRequest) return Promise.resolve();

        var req = new usrPb.AcceptLegalConsentRequest();
        req.setRevision(saved);

        return new Promise(function (resolve) {
            var done = false;
            var finish = function () { if (!done) { done = true; resolve(); } };
            setTimeout(finish, FLUSH_TIMEOUT);
            new window.barkfluff.UsersApiClient(BF.node.origin())
                .acceptLegalConsent(req, BF.metadata.build(token), finish);
        });
    }

    // ─────────────── модалка чтения ───────────────

    function cacheNodes() {
        if (overlay) return;
        overlay = document.getElementById('legalOverlay');
        body = document.getElementById('legalBody');
        title = document.getElementById('legalTitle');
        tabs = overlay.querySelectorAll('.legal-tab');
    }

    function open(doc) {
        cacheNodes();
        title.textContent = BF.i18n.t(DOCS[doc].titleKey);
        tabs.forEach(function (t) {
            t.classList.toggle('active', t.getAttribute('data-doc') === doc);
        });
        body.textContent = BF.i18n.t('common.loadingShort');
        overlay.classList.add('visible');

        load(doc).then(function (text) {
            body.innerHTML = BF.utils.renderMarkdown(text);
            body.scrollTop = 0;
        }).catch(function () {
            body.textContent = BF.i18n.t('legal.loadError');
        });
    }

    function close() {
        cacheNodes();
        overlay.classList.remove('visible');
    }

    function bindOverlay() {
        cacheNodes();
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) close();
        });
        document.getElementById('legalClose').addEventListener('click', close);
        tabs.forEach(function (t) {
            t.addEventListener('click', function () { open(t.getAttribute('data-doc')); });
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && overlay.classList.contains('visible')) close();
        });
        /* Названия документов в строке согласия — тоже вход в читалку. */
        document.querySelectorAll('.legal-link').forEach(function (link) {
            link.addEventListener('click', function () { open(link.getAttribute('data-doc')); });
        });
    }

    // ─────────────── cookie-уведомление ───────────────

    /* Имя и значение совпадают с баннером сайта (files/cookie-notice.js): он ставит cookie
       на .barkfluff.com, поэтому принявшему на barkfluff.com здесь ничего не покажется. */
    var NOTICE_COOKIE = 'bf_cookie_notice';
    var NOTICE_VERSION = '1';

    function initCookieNotice() {
        if (document.cookie.indexOf(NOTICE_COOKIE + '=' + NOTICE_VERSION) !== -1) return;

        var notice = document.getElementById('cookieNotice');
        notice.hidden = false;
        document.getElementById('cookieNoticeOk').addEventListener('click', function () {
            var base = NOTICE_COOKIE + '=' + NOTICE_VERSION + '; path=/; max-age=31536000; SameSite=Lax';
            document.cookie = location.hostname.indexOf('barkfluff.com') !== -1
                ? base + '; domain=.barkfluff.com'
                : base;
            notice.hidden = true;
        });
    }

    /** Читает редакцию соглашения. Резолвится всегда: недоступный документ не должен запирать вход. */
    function init() {
        bindOverlay();
        initCookieNotice();
        return load('terms').then(function (text) {
            revision = parseRevision(text);
        }).catch(function () {
            revision = '';
        });
    }

    window.BF.legal = {
        init: init,
        isAccepted: isAccepted,
        accept: accept,
        flushConsent: flushConsent,
        open: open
    };
})();
