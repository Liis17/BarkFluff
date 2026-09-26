/**
 * Presence: online statuses of the interlocutors (online dots in the chat list, "online" / "last seen" in the chat
 * header), the subscription to their status stream and the incoming "typing…" indicator of the open chat.
 * Requires: BF.i18n, BF.realtime, BF.utils
 * Exposes: BF.presence
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var chatHeaderStatus;
    var onlineSubscribedUserIds = new Set();
    var onlineStatuses = new Map();
    var typingUsers = new Map(); // userId -> timeout handle

    function isOnline(userId) {
        var entry = onlineStatuses.get(userId);
        return entry ? BF.utils.isStatusOnline(entry.status) : false;
    }

    function getEntry(userId) {
        return onlineStatuses.get(userId);
    }

    function updateChatHeaderOnline(userId) {
        if (typingUsers.size > 0) return;
        var entry = onlineStatuses.get(userId);
        var online = entry ? BF.utils.isStatusOnline(entry.status) : false;
        if (online) {
            chatHeaderStatus.textContent = BF.i18n.t('status.online');
        } else {
            chatHeaderStatus.textContent = BF.utils.formatLastSeen(entry ? entry.lastSeen : null);
        }
        chatHeaderStatus.classList.toggle('online', online);
    }

    function handleStatus(userId, status, lastSeen) {
        onlineStatuses.set(userId, { status: status, lastSeen: lastSeen });
        var online = BF.utils.isStatusOnline(status);
        document.querySelectorAll('.online-dot[data-online-user="' + userId + '"]').forEach(function (dot) {
            dot.classList.toggle('visible', online);
        });
        var chatInfo = deps.getCurrentChatInfo();
        if (chatInfo && !chatInfo.isGroupChat && !deps.getPeerIsBot()) {
            var myUserId = deps.getMyUserId();
            var peerId = (chatInfo.membersId || []).find(function (id) {
                return id !== myUserId;
            });
            if (peerId === userId) updateChatHeaderOnline(userId);
        }
    }

    function renderTypingIndicator() {
        var chatInfo = deps.getCurrentChatInfo();
        if (!deps.getCurrentChatId() || !chatInfo) return;

        if (typingUsers.size === 0) {
            if (chatInfo.isGroupChat) {
                chatHeaderStatus.textContent = BF.i18n.tp(
                    'group.memberCount',
                    chatInfo.membersId ? chatInfo.membersId.length : 0
                );
                chatHeaderStatus.classList.remove('online');
            } else {
                var myUserId = deps.getMyUserId();
                var peerId = (chatInfo.membersId || []).find(function (id) {
                    return id !== myUserId;
                });
                if (peerId) updateChatHeaderOnline(peerId);
            }
            return;
        }

        if (chatInfo.isGroupChat) {
            var names = Array.from(typingUsers.keys())
                .slice(0, 3)
                .map(function (id) {
                    var user = deps.getCachedUser(id);
                    if (!user) return BF.i18n.t('common.someone');
                    return (user.firstName || '').split(' ')[0] || user.username || BF.i18n.t('common.someone');
                });
            chatHeaderStatus.textContent = BF.i18n.t(
                typingUsers.size > 1 ? 'status.typing.many' : 'status.typing.named',
                { names: names.join(', ') }
            );
        } else {
            chatHeaderStatus.textContent = BF.i18n.t('status.typing');
        }
    }

    function onTyping(data) {
        var currentChatId = deps.getCurrentChatId();
        if (!currentChatId || String(data.chatId).toLowerCase() !== String(currentChatId).toLowerCase()) return;
        if (data.userId === deps.getMyUserId()) return;
        var old = typingUsers.get(data.userId);
        if (old) clearTimeout(old);
        if (data.action === 2) {
            typingUsers.delete(data.userId);
        } else {
            typingUsers.set(
                data.userId,
                setTimeout(function () {
                    typingUsers.delete(data.userId);
                    renderTypingIndicator();
                }, 6000)
            );
            var chatInfo = deps.getCurrentChatInfo();
            if (chatInfo && chatInfo.isGroupChat) {
                deps.getUser(data.userId).then(renderTypingIndicator);
            }
        }
        renderTypingIndicator();
    }

    // Смена чата: «печатает» прошлого чата не должно пережить переключение.
    function resetTyping() {
        typingUsers.forEach(function (timeoutHandle) {
            clearTimeout(timeoutHandle);
        });
        typingUsers.clear();
    }

    // Подписка на онлайн собеседников всех личных чатов списка (без себя и ботов).
    function collectUserIds() {
        var myUserId = deps.getMyUserId();
        var ids = new Set();
        deps.getChats().forEach(function (chat) {
            if (!chat.isGroupChat && chat.members) {
                chat.members.forEach(function (m) {
                    var user = deps.getCachedUser(m.userId);
                    if (m.userId !== myUserId && !(user && user.isBot)) ids.add(m.userId);
                });
            }
        });
        var changed = ids.size !== onlineSubscribedUserIds.size;
        if (!changed) {
            ids.forEach(function (id) {
                if (!onlineSubscribedUserIds.has(id)) changed = true;
            });
        }
        if (changed) {
            onlineSubscribedUserIds = ids;
            BF.realtime.changeOnlineSubscription(Array.from(onlineSubscribedUserIds));
        }
    }

    function subscribeFor(userIds) {
        var changed = false;
        userIds.forEach(function (id) {
            var user = deps.getCachedUser(id);
            if (!(user && user.isBot) && !onlineSubscribedUserIds.has(id)) {
                onlineSubscribedUserIds.add(id);
                changed = true;
            }
        });
        if (changed) BF.realtime.changeOnlineSubscription(Array.from(onlineSubscribedUserIds));
    }

    function init(options) {
        deps = options;
        chatHeaderStatus = document.querySelector('#chatHeaderStatus');

        BF.realtime.on('online_status', function (data) {
            handleStatus(data.userId, data.status, data.lastSeen);
        });
        BF.realtime.on('typing', onTyping);
    }

    window.BF.presence = {
        init: init,
        isOnline: isOnline,
        getEntry: getEntry,
        handleStatus: handleStatus,
        resetTyping: resetTyping,
        collectUserIds: collectUserIds,
        subscribeFor: subscribeFor
    };
})();
