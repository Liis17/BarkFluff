/**
 * Pinned messages module — top bar (Telegram-style 1/N), full-list modal,
 * realtime sync via 'message_pinned'/'message_unpinned'/'all_messages_unpinned'.
 *
 * Requires: BF.api, BF.messages, BF.utils, BF.realtime
 * Exposes: BF.pinned
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var u = BF.utils;

    // --- State (для активного чата) ---
    var currentChatId = null;
    var byMessageId = new Map();    // messageId(number) → PinnedMessageInfo
    var sorted = [];                // sorted by pinnedAt DESC (newest first)
    var totalCount = 0;
    var barIndex = 0;

    // --- External helpers (provided by main.js via init) ---
    var getMyUserId = function () { return null; };
    var getCurrentChatInfo = function () { return null; };
    var getUserFn = function (id) { return Promise.resolve(null); };
    var showMediaOverlay = function () {};
    var scrollToMessageFn = function () {};

    // --- DOM refs (resolved at init) ---
    var pinnedBar = null;
    var pinnedBarCounter = null;
    var pinnedBarPreview = null;
    var pinnedBarAuthor = null;
    var pinnedBarText = null;
    var pinnedBarOpenBtn = null;
    var pinnedListOverlay = null;
    var pinnedListInner = null;
    var pinnedListCloseBtn = null;
    var pinnedListUnpinAllBtn = null;
    var pinnedListCounter = null;

    // --- Init ---

    function init(opts) {
        opts = opts || {};
        if (typeof opts.getMyUserId === 'function') getMyUserId = opts.getMyUserId;
        if (typeof opts.getCurrentChatInfo === 'function') getCurrentChatInfo = opts.getCurrentChatInfo;
        if (typeof opts.getUser === 'function') getUserFn = opts.getUser;
        if (typeof opts.showMediaOverlay === 'function') showMediaOverlay = opts.showMediaOverlay;
        if (typeof opts.scrollToMessage === 'function') scrollToMessageFn = opts.scrollToMessage;

        pinnedBar          = document.getElementById('pinnedBar');
        pinnedBarCounter   = document.getElementById('pinnedBarCounter');
        pinnedBarPreview   = document.getElementById('pinnedBarPreview');
        pinnedBarAuthor    = document.getElementById('pinnedBarAuthor');
        pinnedBarText      = document.getElementById('pinnedBarText');
        pinnedBarOpenBtn   = document.getElementById('pinnedBarOpen');
        pinnedListOverlay  = document.getElementById('pinnedListOverlay');
        pinnedListInner    = document.getElementById('pinnedListInner');
        pinnedListCloseBtn = document.getElementById('pinnedListClose');
        pinnedListUnpinAllBtn = document.getElementById('pinnedListUnpinAll');
        pinnedListCounter  = document.getElementById('pinnedListCounter');

        wireBar();
        wireListModal();
    }

    // --- Helpers ---

    function rebuildSorted() {
        sorted = Array.from(byMessageId.values());
        sorted.sort(function (a, b) {
            return (b.pinnedAt || 0) - (a.pinnedAt || 0);
        });
        totalCount = sorted.length;
        if (barIndex >= sorted.length) barIndex = 0;
    }

    function setForChat(chatId, list) {
        currentChatId = chatId;
        byMessageId = new Map();
        (list || []).forEach(function (info) {
            if (info && info.message && info.message.id != null) {
                byMessageId.set(Number(info.message.id), info);
            }
        });
        rebuildSorted();
        barIndex = 0;
        renderBar();
    }

    function clear() {
        byMessageId = new Map();
        sorted = [];
        totalCount = 0;
        barIndex = 0;
        renderBar();
    }

    // --- Public ---

    function openForChat(chatId) {
        currentChatId = chatId;
        // Покажем плашку только когда подгрузится список — иначе мигание.
        BF.api.listPinnedMessages(chatId, 0, 50).then(function (data) {
            if (currentChatId !== chatId) return; // юзер уже открыл другой чат
            setForChat(chatId, data && data.pinned ? data.pinned : []);
        }).catch(function (e) {
            console.error('[pinned] listPinnedMessages failed', e);
            if (currentChatId === chatId) setForChat(chatId, []);
        });
    }

    function closeForChat() {
        currentChatId = null;
        clear();
        if (pinnedListOverlay) pinnedListOverlay.classList.remove('visible');
    }

    function isPinned(messageId) {
        return byMessageId.has(Number(messageId));
    }

    function pin(messageId) {
        if (!currentChatId) return Promise.resolve();
        return BF.api.pinMessage(currentChatId, messageId).then(function (resp) {
            if (resp && resp.pinned && resp.pinned.message) {
                byMessageId.set(Number(resp.pinned.message.id), resp.pinned);
                rebuildSorted();
                // Показать новый закреп первым.
                barIndex = 0;
                renderBar();
                if (isListModalOpen()) renderList();
            }
        }).catch(function (err) {
            // TooManyPinnedMessagesException = F7E1A4B8-2C9D-4F3A-B6E7-8D5C1A0F9B23
            var code = err && err.errorCode ? String(err.errorCode).toUpperCase() : '';
            if (code === 'F7E1A4B8-2C9D-4F3A-B6E7-8D5C1A0F9B23') {
                alert('Достигнут лимит закреплённых сообщений (100). Открепите старые, чтобы закрепить новое.');
            } else {
                console.error('[pinned] pinMessage failed', err);
            }
        });
    }

    function unpin(messageId) {
        if (!currentChatId) return Promise.resolve();
        var midNum = Number(messageId);
        return BF.api.unpinMessage(currentChatId, messageId).then(function () {
            byMessageId.delete(midNum);
            rebuildSorted();
            renderBar();
            if (isListModalOpen()) renderList();
        }).catch(function (e) {
            console.error('[pinned] unpinMessage failed', e);
        });
    }

    function unpinAll() {
        if (!currentChatId) return Promise.resolve();
        return BF.api.unpinAll(currentChatId).then(function () {
            clear();
            if (pinnedListOverlay) pinnedListOverlay.classList.remove('visible');
        }).catch(function (e) {
            console.error('[pinned] unpinAll failed', e);
        });
    }

    // --- Realtime handlers ---

    function applyPinnedEvent(data) {
        if (!data || data.chatId !== currentChatId) return;
        var midNum = Number(data.messageId);
        if (byMessageId.has(midNum)) {
            // Уже закреплён локально (idempotent).
            return;
        }
        // Подгружаем полное сообщение через listPinnedMessages с фильтрацией —
        // проще всего вытащить весь список (он же ограничен 100).
        BF.api.listPinnedMessages(currentChatId, 0, 50).then(function (resp) {
            if (!currentChatId) return;
            byMessageId = new Map();
            (resp && resp.pinned ? resp.pinned : []).forEach(function (info) {
                if (info && info.message && info.message.id != null) {
                    byMessageId.set(Number(info.message.id), info);
                }
            });
            rebuildSorted();
            barIndex = 0;
            renderBar();
            if (isListModalOpen()) renderList();
        }).catch(function () {});
    }

    function applyUnpinnedEvent(data) {
        if (!data || data.chatId !== currentChatId) return;
        var midNum = Number(data.messageId);
        if (!byMessageId.has(midNum)) return;
        byMessageId.delete(midNum);
        rebuildSorted();
        renderBar();
        if (isListModalOpen()) renderList();
    }

    function applyAllUnpinnedEvent(data) {
        if (!data || data.chatId !== currentChatId) return;
        clear();
        if (pinnedListOverlay) pinnedListOverlay.classList.remove('visible');
    }

    function applyMessageDeleted(messageId) {
        var midNum = Number(messageId);
        if (!byMessageId.has(midNum)) return;
        byMessageId.delete(midNum);
        rebuildSorted();
        renderBar();
        if (isListModalOpen()) renderList();
    }

    // --- Bar render ---

    function renderBar() {
        if (!pinnedBar) return;
        if (totalCount === 0) {
            pinnedBar.classList.remove('visible');
            pinnedBar.classList.add('hidden');
            return;
        }
        pinnedBar.classList.remove('hidden');
        pinnedBar.classList.add('visible');

        var info = sorted[barIndex] || sorted[0];
        if (!info) return;

        if (pinnedBarCounter) {
            pinnedBarCounter.textContent = (barIndex + 1) + '/' + totalCount;
            pinnedBarCounter.style.display = totalCount > 1 ? '' : 'none';
        }

        var msg = info.message || {};
        var text = (msg.content && msg.content.text) || '';
        if (!text) {
            var atts = (msg.content && msg.content.attachments) || [];
            if (atts.length > 0) text = u.attachmentEmoji(atts[0].type) + ' Вложение';
        }
        if (pinnedBarText) pinnedBarText.textContent = u.truncate(text, 80);

        if (pinnedBarAuthor) {
            pinnedBarAuthor.textContent = 'Закреплённое';
            if (msg.senderId) {
                getUserFn(msg.senderId).then(function (sender) {
                    if (!sender) return;
                    var name = ((sender.firstName || '') + ' ' + (sender.lastName || '')).trim() || sender.username || '';
                    if (pinnedBarAuthor) pinnedBarAuthor.textContent = name || 'Закреплённое';
                }).catch(function () {});
            }
        }
    }

    function wireBar() {
        if (!pinnedBar) return;
        if (pinnedBarPreview) {
            pinnedBarPreview.addEventListener('click', function (e) {
                e.stopPropagation();
                if (totalCount === 0) return;
                // Скролл к текущему пину
                var info = sorted[barIndex];
                if (info && info.message && info.message.id != null) {
                    scrollToMessageFn(info.message.id);
                }
                // Перевод на следующий по кругу для следующего клика
                if (totalCount > 1) {
                    barIndex = (barIndex + 1) % totalCount;
                    renderBar();
                }
            });
        }
        if (pinnedBarOpenBtn) {
            pinnedBarOpenBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                openListModal();
            });
        }
    }

    // --- List modal ---

    function isListModalOpen() {
        return pinnedListOverlay && pinnedListOverlay.classList.contains('visible');
    }

    function openListModal() {
        if (!pinnedListOverlay) return;
        pinnedListOverlay.classList.add('visible');
        renderList();
    }

    function closeListModal() {
        if (pinnedListOverlay) pinnedListOverlay.classList.remove('visible');
    }

    function renderList() {
        if (!pinnedListInner) return;
        pinnedListInner.innerHTML = '';
        if (pinnedListCounter) pinnedListCounter.textContent = totalCount > 0 ? String(totalCount) : '';
        if (pinnedListUnpinAllBtn) pinnedListUnpinAllBtn.style.display = totalCount > 0 ? '' : 'none';

        if (totalCount === 0) {
            var empty = document.createElement('div');
            empty.className = 'pinned-list-empty';
            empty.textContent = 'Нет закреплённых сообщений';
            pinnedListInner.appendChild(empty);
            return;
        }

        var info = getCurrentChatInfo() || {};
        var isGroup = !!info.isGroupChat;
        var myId = getMyUserId();

        var chain = Promise.resolve();
        sorted.forEach(function (pi) {
            chain = chain.then(function () {
                if (!pi.message) return;
                return BF.messages.buildMessageElement(
                    pi.message,
                    myId,
                    isGroup,
                    getUserFn,
                    showMediaOverlay,
                    { knownMessageIds: new Set(), onReplyClick: function (id) { closeListModal(); scrollToMessageFn(id); } }
                ).then(function (msgEl) {
                    var item = document.createElement('div');
                    item.className = 'pinned-list-item';

                    var meta = document.createElement('div');
                    meta.className = 'pinned-list-meta';
                    var pinnedTime = pi.pinnedAt
                        ? new Date(pi.pinnedAt).toLocaleString('ru-RU', {
                            day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'
                        })
                        : '';
                    meta.textContent = 'Закреплено · ' + pinnedTime;
                    if (pi.pinnerUserId) {
                        getUserFn(pi.pinnerUserId).then(function (sender) {
                            if (!sender || !meta.isConnected) return;
                            var name = ((sender.firstName || '') + ' ' + (sender.lastName || '')).trim() || sender.username || '';
                            if (name) meta.textContent = 'Закрепил ' + name + ' · ' + pinnedTime;
                        }).catch(function () {});
                    }

                    msgEl.addEventListener('click', function (e) {
                        // Не реагируем на клики по интерактивным детям (видео/изображения/ссылки/audio)
                        if (e.target.closest('img, video, a, button, .audio-play-btn, .attach-doc')) return;
                        if (pi.message && pi.message.id != null) {
                            closeListModal();
                            scrollToMessageFn(pi.message.id);
                        }
                    });

                    item.appendChild(msgEl);
                    item.appendChild(meta);
                    pinnedListInner.appendChild(item);
                });
            });
        });
    }

    function wireListModal() {
        if (!pinnedListOverlay) return;
        if (pinnedListCloseBtn) pinnedListCloseBtn.addEventListener('click', closeListModal);
        pinnedListOverlay.addEventListener('click', function (e) {
            if (e.target === pinnedListOverlay) closeListModal();
        });
        if (pinnedListUnpinAllBtn) {
            pinnedListUnpinAllBtn.addEventListener('click', function () {
                if (totalCount === 0) return;
                if (!confirm('Открепить все сообщения в чате?')) return;
                pinnedListUnpinAllBtn.disabled = true;
                unpinAll().then(function () {
                    pinnedListUnpinAllBtn.disabled = false;
                });
            });
        }
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && isListModalOpen()) closeListModal();
        });
    }

    // --- Public exports ---

    window.BF.pinned = {
        init: init,
        openForChat: openForChat,
        closeForChat: closeForChat,
        isPinned: isPinned,
        pin: pin,
        unpin: unpin,
        unpinAll: unpinAll,
        applyPinnedEvent: applyPinnedEvent,
        applyUnpinnedEvent: applyUnpinnedEvent,
        applyAllUnpinnedEvent: applyAllUnpinnedEvent,
        applyMessageDeleted: applyMessageDeleted,
        openListModal: openListModal
    };
})();
