/**
 * Forward-message dialog (#forwardOverlay): pick target chats, add a comment and send the message(s) as a forward.
 * Requires: BF.api, BF.i18n, BF.utils
 * Exposes: BF.forward
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var forwardOverlay;
    var forwardCloseBtn;
    var forwardChatListEl;
    var forwardCommentEl;
    var forwardSendBtn;
    var forwardCounterEl;
    var forwardSelection = new Set();

    function $(selector) {
        return document.querySelector(selector);
    }

    function chatAvatarMarkup(chat) {
        var initial = (chat.title || '?')[0].toUpperCase();
        if (chat.picture) return '<img src="' + BF.utils.escapeHtml(chat.picture) + '" alt="">';
        return initial;
    }

    function updateForwardCounter() {
        if (!forwardCounterEl) return;
        var n = forwardSelection.size;
        if (n === 0) forwardCounterEl.textContent = BF.i18n.t('forward.noChatsSelected');
        else forwardCounterEl.textContent = BF.i18n.t('forward.selected', { count: n });
        if (forwardSendBtn) forwardSendBtn.disabled = n === 0;
    }

    // Пересылка пересланного отправляет оригиналы, а не снапшот. Оригиналов может быть
    // несколько, поэтому возвращаем список: иначе пересылка пачки потеряла бы всё, кроме первого.
    function resolveSourceIds(msg, fallbackId) {
        if (!msg || !msg.content || !msg.content.attachments) return [fallbackId];
        var ids = [];
        var forwards = [];
        for (var i = 0; i < msg.content.attachments.length; i++) {
            var a = msg.content.attachments[i];
            var t = a.type;
            if ((t === 'FORWARDED_MESSAGE' || t === 8 || t === '8') && a.forwardedMessage) {
                forwards.push(a.forwardedMessage);
            }
        }
        forwards.sort(function (x, y) {
            return (x.order || 0) - (y.order || 0);
        });
        for (var j = 0; j < forwards.length; j++) {
            if (forwards[j].originalMessageId) ids.push(forwards[j].originalMessageId);
        }
        return ids.length > 0 ? ids : [fallbackId];
    }

    function open(msg, fallbackId) {
        var sourceMessageIds = resolveSourceIds(msg, fallbackId);
        if (!forwardOverlay || !sourceMessageIds || sourceMessageIds.length === 0) return;
        forwardSelection = new Set();
        if (forwardCommentEl) forwardCommentEl.value = '';
        forwardChatListEl.innerHTML = '';

        deps.getChats().forEach(function (chat) {
            var item = document.createElement('div');
            item.className = 'forward-chat-item';
            item.dataset.chatId = chat.id;
            item.innerHTML =
                '<div class="fwd-avatar">' +
                chatAvatarMarkup(chat) +
                '</div>' +
                '<div class="fwd-name">' +
                BF.utils.escapeHtml(chat.title || BF.i18n.t('common.chat')) +
                '</div>' +
                '<div class="fwd-check">&#10003;</div>';
            item.addEventListener('click', function () {
                var id = chat.id;
                if (forwardSelection.has(id)) {
                    forwardSelection.delete(id);
                    item.classList.remove('selected');
                } else {
                    forwardSelection.add(id);
                    item.classList.add('selected');
                }
                updateForwardCounter();
            });
            forwardChatListEl.appendChild(item);
        });
        updateForwardCounter();

        BF.utils.openOverlay(forwardOverlay);
        forwardSendBtn.onclick = function () {
            submit(sourceMessageIds);
        };
    }

    function close() {
        if (!forwardOverlay) return;
        BF.utils.closeOverlay(forwardOverlay);
        forwardSelection = new Set();
        if (forwardSendBtn) forwardSendBtn.onclick = null;
    }

    function submit(sourceMessageIds) {
        if (forwardSelection.size === 0 || !sourceMessageIds || sourceMessageIds.length === 0) return;
        var comment = forwardCommentEl ? forwardCommentEl.value.trim() : '';
        var ids = Array.from(forwardSelection);
        forwardSendBtn.disabled = true;
        var originalLabel = forwardSendBtn.textContent;
        forwardSendBtn.textContent = BF.i18n.t('forward.sending');

        var chain = ids.reduce(function (p, chatId) {
            return p.then(function () {
                return BF.api
                    .sendMessage({
                        chatId: chatId,
                        text: comment || null,
                        forwardedMessageIds: sourceMessageIds
                    })
                    .catch(function () {});
            });
        }, Promise.resolve());

        chain.then(function () {
            forwardSendBtn.disabled = false;
            forwardSendBtn.textContent = originalLabel;
            close();
            deps.showToast(BF.i18n.tp('forward.done', ids.length), false);
        });
    }

    function init(options) {
        deps = options;
        forwardOverlay = $('#forwardOverlay');
        forwardCloseBtn = $('#forwardClose');
        forwardChatListEl = $('#forwardChatList');
        forwardCommentEl = $('#forwardComment');
        forwardSendBtn = $('#forwardSendBtn');
        forwardCounterEl = $('#forwardCounter');

        if (forwardCloseBtn) forwardCloseBtn.addEventListener('click', close);
        if (forwardOverlay) {
            forwardOverlay.addEventListener('click', function (e) {
                if (e.target === forwardOverlay) close();
            });
        }
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && forwardOverlay && forwardOverlay.classList.contains('visible')) close();
        });
    }

    window.BF.forward = { init: init, open: open, resolveSourceIds: resolveSourceIds };
})();
