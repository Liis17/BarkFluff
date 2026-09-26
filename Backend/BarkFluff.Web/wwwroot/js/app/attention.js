/**
 * Attracting attention outside the page content: the tab title with the unread counter, the favicon
 * (the peer's round avatar in a private chat) and browser notifications about new messages.
 * Requires: BF.i18n, BF.utils
 * Exposes: BF.attention
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;

    // null → название приложения из словаря (пересчитывается, т.к. зависит от языка)
    var baseTitle = null;

    var faviconEl = null;
    var defaultFaviconHref = '/favicon.ico';
    var faviconRequestId = 0;

    function defaultBaseTitle() {
        return BF.i18n.t('app.title');
    }

    // «Чат • Имя Фамилия» — заголовок вкладки личного чата.
    function chatTabTitle(user) {
        var name = ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || BF.i18n.t('common.user');
        return BF.i18n.t('tab.chatWith', { name: name });
    }

    function applyFavicon(href) {
        if (!faviconEl) return;
        faviconEl.setAttribute('href', href || defaultFaviconHref);
        if (href) faviconEl.removeAttribute('type');
        else faviconEl.setAttribute('type', 'image/x-icon');
    }

    function setFavicon(href) {
        var requestId = ++faviconRequestId;
        if (!href) {
            applyFavicon(null);
            return;
        }

        var image = new Image();
        image.crossOrigin = 'anonymous';
        image.onload = function () {
            if (requestId !== faviconRequestId) return;
            try {
                var sourceSize = Math.min(image.naturalWidth, image.naturalHeight);
                var canvas = document.createElement('canvas');
                canvas.width = 64;
                canvas.height = 64;
                var context = canvas.getContext('2d');
                if (!sourceSize || !context) throw new Error('invalid_avatar');
                context.beginPath();
                context.arc(32, 32, 32, 0, Math.PI * 2);
                context.clip();
                context.drawImage(
                    image,
                    (image.naturalWidth - sourceSize) / 2,
                    (image.naturalHeight - sourceSize) / 2,
                    sourceSize,
                    sourceSize,
                    0,
                    0,
                    64,
                    64
                );
                applyFavicon(canvas.toDataURL('image/png'));
            } catch (e) {
                applyFavicon(href);
            }
        };
        image.onerror = function () {
            if (requestId === faviconRequestId) applyFavicon(href);
        };
        image.src = href;
    }

    function updateTitleBadge() {
        var total = 0;
        deps.getChats().forEach(function (c) {
            total += c.countUnread || 0;
        });
        var base = baseTitle || defaultBaseTitle();
        document.title = total > 0 ? '(' + (total > 99 ? '99+' : total) + ') ' + base : base;
    }

    function resetChatTabContext() {
        baseTitle = null;
        setFavicon(null);
        updateTitleBadge();
    }

    function setChatTabContext(title, faviconHref) {
        baseTitle = title || null;
        setFavicon(faviconHref || null);
        updateTitleBadge();
    }

    function showNewMessageNotification(chatTitle, msg) {
        if (!('Notification' in window) || Notification.permission !== 'granted') return;
        if (document.visibilityState !== 'visible') return;
        if (document.visibilityState === 'visible' && msg.chatId === deps.getCurrentChatId()) return;

        var body = '';
        if (msg.content && msg.content.text) body = BF.utils.truncate(msg.content.text, 80);
        else if (msg.content && msg.content.attachments && msg.content.attachments.length > 0) {
            body = BF.utils.attachmentEmoji(msg.content.attachments[0].type);
        }

        try {
            var n = new Notification(chatTitle || BF.i18n.t('notification.newMessage'), {
                body: body,
                tag: 'bf-msg-' + (msg.id || Date.now()),
                renotify: true
            });
            n.onclick = function () {
                window.focus();
                n.close();
            };
            setTimeout(function () {
                n.close();
            }, 5000);
        } catch (e) {
            /* ignore mobile/permission errors */
        }
    }

    function init(options) {
        deps = options;
        faviconEl = document.getElementById('favicon');
        defaultFaviconHref = faviconEl ? faviconEl.getAttribute('href') : '/favicon.ico';
    }

    window.BF.attention = {
        init: init,
        chatTabTitle: chatTabTitle,
        updateTitleBadge: updateTitleBadge,
        resetChatTabContext: resetChatTabContext,
        setChatTabContext: setChatTabContext,
        showNewMessageNotification: showNewMessageNotification
    };
})();
