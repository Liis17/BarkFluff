/**
 * Marking messages of the open chat as read: a debounced batch (`schedule` on open, `markSoon` for live
 * messages) and scroll-based marking of the messages that enter the visible part of the feed.
 * Requires: BF.api, BF.i18n
 * Exposes: BF.markRead
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var messagesArea;
    var markReadTimer = null;
    var markReadPending = new Set();
    var scrollTimer = null;

    function flush() {
        if (markReadPending.size === 0) return;
        var ids = Array.from(markReadPending);
        markReadPending.clear();
        BF.api.markAsRead(ids).catch(function () {
            deps.showToast(BF.i18n.t('error.markRead'), true);
        });
    }

    // Открытие чата: через секунду помечаем все непрочитанные чужие сообщения загруженного окна.
    function schedule() {
        if (markReadTimer) clearTimeout(markReadTimer);
        markReadTimer = setTimeout(flush, 1000);
        if (!deps.getCurrentChatId()) return;
        var myUserId = deps.getMyUserId();
        deps.getMessages().forEach(function (msg) {
            if (msg.senderId !== myUserId && !(msg.readBy || []).includes(myUserId)) markReadPending.add(msg.id);
        });
    }

    // Сообщения уже на экране (живое сообщение у нижнего края, догрузка после resync).
    function markSoon(ids) {
        [].concat(ids).forEach(function (id) {
            markReadPending.add(id);
        });
        if (markReadTimer) clearTimeout(markReadTimer);
        markReadTimer = setTimeout(flush, 500);
    }

    function markVisible() {
        if (!deps.getCurrentChatId() || deps.getCurrentChatType() === 1) return;
        var myUserId = deps.getMyUserId();
        var messages = deps.getMessages();
        var changed = false;
        var areaRect = messagesArea.getBoundingClientRect();

        // data-msg-id стоит на .msg-group (не на .msg-bubble); у системных плашек и
        // локальных pending-сообщений id не числовой либо это не чужое сообщение — они отсеиваются ниже.
        messagesArea.querySelectorAll('.msg-group').forEach(function (el) {
            if (el.classList.contains('msg-system')) return;
            var msgId = Number(el.dataset.msgId);
            if (!msgId) return;
            var msg = messages.find(function (m) {
                return m.id === msgId;
            });
            if (!msg || msg.senderId === myUserId) return;
            if ((msg.readBy || []).includes(myUserId)) return;

            var rect = el.getBoundingClientRect();
            // Consider the message visible if any part overlaps the messages area
            if (rect.bottom > areaRect.top && rect.top < areaRect.bottom) {
                markReadPending.add(msgId);
                changed = true;
            }
        });

        if (changed) {
            if (markReadTimer) clearTimeout(markReadTimer);
            markReadTimer = setTimeout(flush, 500);
        }
    }

    function init(options) {
        deps = options;
        messagesArea = document.querySelector('#messagesArea');
        messagesArea.addEventListener('scroll', function () {
            if (scrollTimer) return;
            scrollTimer = setTimeout(function () {
                scrollTimer = null;
                markVisible();
            }, 300);
        });
    }

    window.BF.markRead = { init: init, schedule: schedule, markSoon: markSoon };
})();
