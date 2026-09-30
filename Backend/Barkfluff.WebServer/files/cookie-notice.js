/*
 * Информационное уведомление об использовании cookie.
 * Подключается строкой <script src="/assets/cookie-notice.js" defer> на каждой странице сайта:
 * общего layout у html/ нет, поэтому баннер живёт отдельным самодостаточным файлом.
 *
 * Отказаться нельзя: сайт ставит только cookie, без которых не работают чат поддержки
 * (barkfluff_chat_id) и переход в веб-клиент (bf_open_chat). Аналитики и рекламы нет,
 * поэтому категорий и тумблеров тоже нет — см. раздел 10 Политики конфиденциальности.
 */
(function () {
    'use strict';

    /* Версия уведомления. Меняется, если изменился состав cookie — тогда баннер показывается заново. */
    var VERSION = '1';
    var COOKIE = 'bf_cookie_notice';

    var S = {
        ru: {
            text: 'Мы используем cookie только для работы сайта: чат поддержки и переход в веб-версию. Аналитики и рекламы нет.',
            more: 'Подробнее',
            ok: 'Понятно'
        },
        en: {
            text: 'We use cookies only to run the site: the support chat and switching to the web app. No analytics, no ads.',
            more: 'Learn more',
            ok: 'Got it'
        }
    };

    function accepted() {
        return document.cookie.indexOf(COOKIE + '=' + VERSION) !== -1;
    }

    function remember() {
        /* Домен общий с web.barkfluff.com, чтобы уведомление не всплыло там повторно.
           Условие то же, что у bf_open_chat в userpage.html. */
        var base = COOKIE + '=' + VERSION + '; path=/; max-age=31536000; SameSite=Lax';
        document.cookie = location.hostname.indexOf('barkfluff.com') !== -1
            ? base + '; domain=.barkfluff.com'
            : base;
    }

    function currentLang() {
        var explicit = document.documentElement.lang || localStorage.getItem('bf_lang');
        if (explicit) return explicit === 'ru' ? 'ru' : 'en';
        return (navigator.language || '').indexOf('ru') === 0 ? 'ru' : 'en';
    }

    if (accepted()) return;

    var style = document.createElement('style');
    style.textContent = [
        '#bfCookieNotice{position:fixed;left:16px;right:16px;bottom:16px;z-index:9999;',
        'display:flex;flex-wrap:wrap;gap:12px 20px;align-items:center;justify-content:space-between;',
        'width:min(920px,calc(100% - 32px));margin:0 auto;padding:14px 18px;',
        'border:1px solid var(--line,rgba(255,240,220,.14));border-radius:14px;',
        'background:var(--panel,var(--bg-2,#241310));box-shadow:0 18px 40px -18px rgba(0,0,0,.75);',
        'color:var(--text,var(--ink,#fef6ea));font-family:inherit;font-size:14px;line-height:1.5;',
        'opacity:0;transform:translateY(8px);transition:opacity .3s ease,transform .3s ease}',
        '#bfCookieNotice.bf-shown{opacity:1;transform:none}',
        '#bfCookieNotice p{margin:0;flex:1 1 320px;color:var(--muted,var(--ink-dim,#c9a893))}',
        '#bfCookieNotice a{color:var(--brand,#ff9a4c);text-decoration:none;white-space:nowrap}',
        '#bfCookieNotice a:hover{text-decoration:underline}',
        '#bfCookieNoticeOk{flex:0 0 auto;padding:9px 20px;border:0;border-radius:10px;cursor:pointer;',
        'background:var(--brand,#ff9a4c);color:#1c0d0a;font:inherit;font-weight:600}',
        '#bfCookieNoticeOk:hover{filter:brightness(1.08)}',
        '@media (prefers-reduced-motion:reduce){#bfCookieNotice{transition:none}}'
    ].join('');

    var box = document.createElement('section');
    box.id = 'bfCookieNotice';
    box.setAttribute('role', 'region');
    box.innerHTML = '<p><span id="bfCookieNoticeText"></span> '
        + '<a href="/legal/privacy-policy" id="bfCookieNoticeMore"></a></p>'
        + '<button type="button" id="bfCookieNoticeOk"></button>';

    function render() {
        var s = S[currentLang()];
        box.querySelector('#bfCookieNoticeText').textContent = s.text;
        box.querySelector('#bfCookieNoticeMore').textContent = s.more;
        box.querySelector('#bfCookieNoticeOk').textContent = s.ok;
        box.setAttribute('aria-label', s.text);
    }

    document.head.appendChild(style);
    document.body.appendChild(box);
    render();
    requestAnimationFrame(function () { box.classList.add('bf-shown'); });

    box.querySelector('#bfCookieNoticeOk').addEventListener('click', function () {
        remember();
        box.remove();
    });

    /* Переключатель языка есть не на всех страницах; свой обработчик нужен, потому что
       applyLang главной страницы работает по фиксированному списку id и баннер не знает. */
    var langToggle = document.getElementById('langToggle');
    if (langToggle) {
        langToggle.addEventListener('click', function () {
            requestAnimationFrame(render);
        });
    }
})();
