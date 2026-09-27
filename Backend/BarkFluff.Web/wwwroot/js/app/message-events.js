/**
 * Live message events: incoming, read, edited, deleted and pinned messages (realtime) plus the local
 * edit / delete entry points and the "delete message" confirmation dialog.
 * Requires: BF.api, BF.i18n, BF.realtime, BF.utils, BF.feed, BF.composer, BF.markRead, BF.messages, BF.sound
 * Exposes: BF.messageEvents
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var messagesArea;
    var messagesInner;
    var scrollToBottomBtn;
    var deleteOverlay;
    var deleteOk;

    function handleNewMessage(chatId, msg) {
        var messages = deps.getMessages();
        var chats = deps.getChats();
        var myUserId = deps.getMyUserId();
        var reconciledPending = deps.reconcilePendingUpload(chatId, msg);
        if (
            !reconciledPending &&
            chatId === deps.getCurrentChatId() &&
            messages.some(function (m) {
                return m.id === msg.id;
            })
        ) {
            return;
        }

        var chatIdx = chats.findIndex(function (c) {
            return c.id === chatId;
        });
        var chatTitle = '';
        if (chatIdx >= 0) {
            var chat = chats[chatIdx];
            chatTitle = chat.title || '';
            chat.lastMessage = msg;
            if (chatId !== deps.getCurrentChatId() && msg.senderId !== myUserId) {
                chat.countUnread = (chat.countUnread || 0) + 1;
            }
            chats.splice(chatIdx, 1);
            chats.unshift(chat);
            deps.renderChatList();
        } else {
            // Unknown chat — reload the list to pick it up
            deps.loadChats(true);
        }

        // Browser notification for messages from others
        if (msg.senderId !== myUserId) {
            BF.sound.play('chime');
            deps.showNewMessageNotification(chatTitle, msg);
        }

        deps.updateTitleBadge();

        if (chatId === deps.getCurrentChatId() && !reconciledPending) {
            var isAtBottom = messagesArea.scrollHeight - messagesArea.scrollTop - messagesArea.clientHeight < 300;
            if (msg.senderId !== myUserId) BF.feed.announceIncoming(msg);
            messages.push(msg);
            deps.appendMessageToView(msg).then(function () {
                if (isAtBottom) {
                    deps.scrollToBottom();
                    if (scrollToBottomBtn) scrollToBottomBtn.classList.remove('visible');
                } else {
                    if (scrollToBottomBtn) scrollToBottomBtn.classList.add('visible');
                    BF.feed.incrementNewBelow();
                }
                // Auto-mark as read if message is visible (user is at bottom)
                if (isAtBottom && msg.senderId !== myUserId) BF.markRead.markSoon(msg.id);
            });
        }
    }

    function handleMessageRead(chatId, messageId, readBy) {
        var myUserId = deps.getMyUserId();
        // Update the message's readBy in the active chat view
        if (chatId === deps.getCurrentChatId()) {
            var msg = deps.getMessages().find(function (m) {
                return m.id === messageId;
            });
            if (msg) {
                msg.readBy = readBy;
                // Update check-mark indicator (single = delivered, double = read by others)
                var el = messagesArea.querySelector('.msg-status[data-msg-id="' + messageId + '"]');
                if (el) {
                    var rc = readBy.filter(function (id) {
                        return id !== myUserId;
                    }).length;
                    BF.messages.updateMessageStatus(el, rc > 0);
                }
            }
        }

        // Update unread count in chat list
        var chat = deps.getChats().find(function (c) {
            return c.id === chatId;
        });
        if (chat) {
            if (readBy.includes(myUserId)) {
                // We read a message — if this chat is open, all visible are read
                if (chatId === deps.getCurrentChatId()) {
                    chat.countUnread = 0;
                } else {
                    chat.countUnread = Math.max(0, (chat.countUnread || 0) - 1);
                }
            }
            deps.renderChatList();
            deps.updateTitleBadge();
        }
    }

    function applyEdit(chatId, updatedMsg) {
        if (!updatedMsg) return;
        var ch = deps.getChats().find(function (x) {
            return x.id === chatId;
        });
        if (ch && ch.lastMessage && ch.lastMessage.id === updatedMsg.id) {
            ch.lastMessage = updatedMsg;
            deps.renderChatList();
        }
        if (chatId !== deps.getCurrentChatId()) return;
        var messages = deps.getMessages();
        var idx = messages.findIndex(function (m) {
            return m.id === updatedMsg.id;
        });
        if (idx < 0) return;
        messages[idx] = updatedMsg;
        var oldEl = messagesInner.querySelector('.msg-group[data-msg-id="' + updatedMsg.id + '"]');
        if (!oldEl) return;
        deps.buildMessageViewElement(updatedMsg).then(function (newEl) {
            newEl.dataset.date = oldEl.dataset.date;
            BF.feed.replaceElement(oldEl, newEl);
        });
    }

    function applyDelete(chatId, messageId) {
        if (messageId == null) return;
        var msgIdNum = Number(messageId);
        console.log('[messageEvents] applyDelete', {
            chatId: chatId,
            messageId: messageId,
            currentChatId: deps.getCurrentChatId()
        });
        BF.composer.onMessageDeleted(msgIdNum);

        // messageId глобально уникален: ищем и удаляем во всех текущих структурах,
        // не привязываясь к chatId-сравнению (на случай расхождения форматов id).
        var messages = deps.getMessages();
        var idx = messages.findIndex(function (m) {
            return Number(m.id) === msgIdNum;
        });
        if (idx >= 0) messages.splice(idx, 1);
        if (idx >= 0) deps.renderMessages();

        // Обновляем lastMessage чат-листа для всех чатов, где это сообщение последнее.
        var anyChatTouched = false;
        deps.getChats().forEach(function (c) {
            if (c.lastMessage && Number(c.lastMessage.id) === msgIdNum) anyChatTouched = true;
        });
        if (anyChatTouched) deps.loadChats(true);

        if (BF.pinned && BF.pinned.applyMessageDeleted) BF.pinned.applyMessageDeleted(msgIdNum);
    }

    function requestDelete(messageId) {
        if (!deleteOverlay || !messageId) return;
        BF.utils.openOverlay(deleteOverlay);
        deleteOk.onclick = function () {
            deleteOk.disabled = true;
            BF.api
                .deleteMessage(messageId)
                .then(function () {
                    applyDelete(deps.getCurrentChatId(), messageId);
                })
                .catch(function () {
                    deps.showToast(BF.i18n.t('error.deleteMessage'), true);
                })
                .finally(function () {
                    deleteOk.disabled = false;
                    BF.utils.closeOverlay(deleteOverlay);
                    deleteOk.onclick = null;
                });
        };
    }

    function init(options) {
        deps = options;
        messagesArea = document.querySelector('#messagesArea');
        messagesInner = document.querySelector('#messagesInner');
        scrollToBottomBtn = document.querySelector('#scrollToBottomBtn');
        deleteOverlay = document.querySelector('#deleteMsgConfirmOverlay');
        deleteOk = document.querySelector('#deleteMsgOk');
        var deleteCancel = document.querySelector('#deleteMsgCancel');

        // Delete confirm cancel
        if (deleteCancel) {
            deleteCancel.addEventListener('click', function () {
                if (deleteOverlay) BF.utils.closeOverlay(deleteOverlay);
                if (deleteOk) deleteOk.onclick = null;
            });
        }
        if (deleteOverlay) {
            deleteOverlay.addEventListener('click', function (e) {
                if (e.target === deleteOverlay) {
                    BF.utils.closeOverlay(deleteOverlay);
                    if (deleteOk) deleteOk.onclick = null;
                }
            });
        }

        BF.realtime.on('new_message', function (data) {
            handleNewMessage(data.chatId, data.message);
        });
        BF.realtime.on('message_read', function (data) {
            handleMessageRead(data.chatId, data.messageId, data.readBy);
        });
        BF.realtime.on('message_edited', function (data) {
            applyEdit(data.chatId, data.message);
        });
        BF.realtime.on('message_deleted', function (data) {
            applyDelete(data.chatId, data.messageId);
        });
        BF.realtime.on('message_pinned', function (data) {
            if (BF.pinned && BF.pinned.applyPinnedEvent) BF.pinned.applyPinnedEvent(data);
        });
        BF.realtime.on('message_unpinned', function (data) {
            if (BF.pinned && BF.pinned.applyUnpinnedEvent) BF.pinned.applyUnpinnedEvent(data);
        });
        BF.realtime.on('all_messages_unpinned', function (data) {
            if (BF.pinned && BF.pinned.applyAllUnpinnedEvent) BF.pinned.applyAllUnpinnedEvent(data);
        });
    }

    window.BF.messageEvents = {
        init: init,
        applyEdit: applyEdit,
        applyDelete: applyDelete,
        requestDelete: requestDelete
    };
})();
