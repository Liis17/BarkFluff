/**
 * Opening a chat from outside the messenger: the bf_open_chat cookie (public user page), the ?chat= URL
 * parameter and push notifications. A link to a chat that is not in the loaded list first reloads the list once.
 * Requires: BF.api
 * Exposes: BF.deepLink
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var pendingPushChatId = null;
    var refreshedPushChatId = null;

    function getCookie(name) {
        var m = document.cookie.match('(?:^|; )' + name.replace(/([.$?*|{}()[\]\\/+^])/g, '\\$1') + '=([^;]*)');
        return m ? decodeURIComponent(m[1]) : null;
    }

    function deleteOpenChatCookie() {
        var base = 'bf_open_chat=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT';
        document.cookie = base;
        if (/(^|\.)barkfluff\.com$/i.test(location.hostname)) {
            document.cookie = base + '; domain=.barkfluff.com';
        }
    }

    // Если на странице пользователя (barkfluff.com/<username>) нажали «Написать в браузере»,
    // там в cookie bf_open_chat записан username. Находим пользователя -> chatId -> открываем чат.
    // Логика повторяет Android DeepLinkActivity: SearchUsers -> точное совпадение -> GetPersonChatId.
    function openFromCookie() {
        var uname = getCookie('bf_open_chat');
        if (!uname) return;
        deleteOpenChatCookie(); // одноразово: сразу удаляем
        uname = uname.trim();
        if (!uname) return;

        BF.api
            .searchUsers(uname, 0, 20)
            .then(function (data) {
                var list = (data && data.users) || [];
                var target = null;
                for (var i = 0; i < list.length; i++) {
                    if ((list[i].username || '').toLowerCase() === uname.toLowerCase()) {
                        target = list[i];
                        break;
                    }
                }
                if (!target) return; // точного совпадения нет — как в Android, ничего не открываем
                return BF.api.getPersonChatId(target.id).then(function (d) {
                    if (d && d.chatId) deps.openChat(d.chatId);
                });
            })
            .catch(function (err) {
                console.error('maybeOpenChatFromCookie failed:', err);
            });
    }

    function openFromPush(chatId) {
        if (!chatId) return;
        var known = deps.getChats().some(function (chat) {
            return String(chat.id) === String(chatId);
        });
        if (!known) {
            pendingPushChatId = chatId;
            // A push may point to a chat outside the initial page of the list.
            // Refresh it once before leaving the link pending.
            if (refreshedPushChatId !== String(chatId)) {
                refreshedPushChatId = String(chatId);
                deps.loadChats(true)
                    .then(function () {
                        if (pendingPushChatId) openFromPush(pendingPushChatId);
                    })
                    .catch(function (err) {
                        console.error('Could not load push target chat:', err);
                    });
            }
            return;
        }
        pendingPushChatId = null;
        refreshedPushChatId = null;
        deps.openChat(chatId);
    }

    // ?chat=<id> (клик по push, переход по ссылке): параметр снимается из адреса, чат открывается.
    function openFromUrl() {
        var url = new URL(window.location.href);
        var chatId = url.searchParams.get('chat');
        if (!chatId) return;
        url.searchParams.delete('chat');
        url.searchParams.delete('call');
        window.history.replaceState({}, '', url.pathname + url.search + url.hash);
        openFromPush(chatId);
    }

    function openPending() {
        if (pendingPushChatId) openFromPush(pendingPushChatId);
    }

    function init(options) {
        deps = options;
    }

    window.BF.deepLink = {
        init: init,
        openFromCookie: openFromCookie,
        openFromUrl: openFromUrl,
        openFromPush: openFromPush,
        openPending: openPending
    };
})();
