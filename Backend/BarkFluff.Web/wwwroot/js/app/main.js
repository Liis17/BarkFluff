/**
 * Messenger page bootstrap — chat list, message area, search, profile, file upload.
 * Orchestrates BF.api, BF.realtime, BF.messages, BF.files, BF.utils, BF.tokens.
 * Loaded on /messenger page.
 */
(function () {
    'use strict';

    // --- Auth gate ---
    if (!BF.tokens.get()) { window.location.href = '/'; return; }

    var u = BF.utils;

    // --- My user ID from JWT ---
    var myUserId = null;
    var payload = u.parseJwtPayload(BF.tokens.getAccessToken());
    if (payload) myUserId = Number(payload['x-user-id']);

    // --- User cache ---
    var userCache = new Map();
    var userRequests = new Map();

    function getUser(userId) {
        if (userCache.has(userId)) return Promise.resolve(userCache.get(userId));
        if (userRequests.has(userId)) return userRequests.get(userId);
        var request = BF.api.getUser(userId).then(function (d) {
            if (d && d.user) { userCache.set(userId, d.user); return d.user; }
            return null;
        });
        userRequests.set(userId, request);
        request.then(function () { userRequests.delete(userId); }, function () { userRequests.delete(userId); });
        return request;
    }

    // --- State ---
    var chats = [];
    var currentChatId = null;
    var currentChatInfo = null;
    var currentChatType = 0; // ChatType: 0=REGULAR, 1=PRIVATE
    var currentChatPeerIsBot = false;
    var messages = [];
    // --- DOM refs ---
    var $ = function (sel) { return document.querySelector(sel); };
    var chatHeader = $('#chatHeader');
    var chatHeaderAvatar = $('#chatHeaderAvatar');
    var chatHeaderName = $('#chatHeaderName');
    var chatHeaderStatus = $('#chatHeaderStatus');
    var chatEmpty = $('#chatEmpty');
    var messagesArea = $('#messagesArea');
    var messagesInner = $('#messagesInner');
    var loadingMessages = $('#loadingMessages');
    var inputBar = $('#inputBar');
    var soonToastEl = $('#soonToast');

    // Settings and confirm overlays are managed by BF.settings module

    var groupMediaContent = $('#groupMediaContent');

    BF.mediaViewer.init({
        getCurrentChatId: function () { return currentChatId; }
    });
    var showMediaOverlay = BF.mediaViewer.show;

    function botBadgeMarkup() {
        var label = u.escapeHtml(BF.i18n.t('common.bot'));
        return '<span class="bot-badge" role="img" aria-label="' + label + '" title="' + label + '">' +
            BF.icons.html('bots') + '</span>';
    }

    // Кнопки звонков в шапке чата и в профиле.
    BF.chatCalls.init({
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatInfo: function () { return currentChatInfo; },
        getPeerIsBot: function () { return currentChatPeerIsBot; },
        getMyUserId: function () { return myUserId; }
    });
    var setChatCallButtonsVisible = BF.chatCalls.setChatButtonsVisible;
    var setProfileCallButtonsVisible = BF.chatCalls.setProfileButtonsVisible;

    // ========== TAB TITLE, FAVICON, NOTIFICATIONS ==========

    BF.attention.init({
        getChats: function () { return chats; },
        getCurrentChatId: function () { return currentChatId; }
    });
    var chatTabTitle = BF.attention.chatTabTitle;
    var updateTitleBadge = BF.attention.updateTitleBadge;
    var resetChatTabContext = BF.attention.resetChatTabContext;
    var setChatTabContext = BF.attention.setChatTabContext;
    var showNewMessageNotification = BF.attention.showNewMessageNotification;

    // ========== CHAT LIST ==========

    BF.chatList.init({
        getChats: function () { return chats; },
        setChats: function (value) { chats = value; },
        getCurrentChatId: function () { return currentChatId; },
        getMyUserId: function () { return myUserId; },
        getCachedUser: function (userId) { return userCache.get(userId); },
        getUser: getUser,
        isUserOnline: BF.presence.isOnline,
        collectOnlineUserIds: BF.presence.collectUserIds,
        updateTitleBadge: updateTitleBadge,
        openChat: openChat,
        botBadgeMarkup: botBadgeMarkup
    });
    var loadChats = BF.chatList.load;
    var renderChatList = BF.chatList.render;
    var refreshChatListQuiet = BF.chatList.refreshQuiet;

    // ========== PRESENCE (online statuses, typing indicator) ==========

    BF.presence.init({
        getChats: function () { return chats; },
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatInfo: function () { return currentChatInfo; },
        getPeerIsBot: function () { return currentChatPeerIsBot; },
        getMyUserId: function () { return myUserId; },
        getCachedUser: function (userId) { return userCache.get(userId); },
        getUser: getUser
    });

    // ========== OPEN CHAT ==========

    function updateOpenChatUrl(chatId) {
        var url = new URL(window.location.href);
        url.searchParams.set('chat', chatId);
        url.searchParams.delete('call');
        window.history.replaceState({}, '', url.pathname + url.search + url.hash);
    }

    function openChat(chatId) {
        if (chatId === currentChatId) return;
        if (currentChatId && currentChatType === 0 && BF.drafts) BF.drafts.flush(currentChatId);
        stopTypingSend(true);
        BF.presence.resetTyping();

        var chatMeta = chats.find(function (c) { return c.id === chatId; });
        if (chatMeta && chatMeta.chatType === 1) { openPrivateChat(chatMeta); return; }

        if (BF.pinned && BF.pinned.openForChat) BF.pinned.openForChat(chatId);

        currentChatId = chatId;
        updateOpenChatUrl(chatId);
        if (BF.personalization) BF.personalization.applyForChat(chatId);
        BF.realtime.subscribeTyping(chatId);
        currentChatInfo = null;
        currentChatType = chatMeta ? chatMeta.chatType : 0;
        currentChatPeerIsBot = false;
        messages = [];
        BF.feed.reset();
        clearPendingReply(false);
        clearPendingEdit();
        closeContextMenu();
        chatEmpty.style.display = 'none';
        chatHeader.classList.add('visible');
        messagesArea.parentElement.classList.add('visible');
        messagesArea.classList.add('visible');
        messagesInner.innerHTML = '';
        inputBar.classList.add('visible');
        inputBar.classList.remove('private-chat');
        loadingMessages.classList.add('visible');
        chatHeaderStatus.hidden = false;
        chatHeaderStatus.textContent = '';
        chatHeaderStatus.classList.remove('online');
        setChatCallButtonsVisible(true);

        // Reset unread count for the opened chat
        var openedChat = chats.find(function (c) { return c.id === chatId; });
        if (openedChat && openedChat.countUnread > 0) {
            openedChat.countUnread = 0;
            updateTitleBadge();
        }

        renderChatList();

        BF.api.getChatInfo(chatId).then(function (info) {
            if (!info || info.error) { loadingMessages.classList.remove('visible'); return; }
            currentChatInfo = info;

            chatHeaderName.textContent = info.title || BF.i18n.t('common.chat');
            if (info.picture) chatHeaderAvatar.innerHTML = '<img src="' + u.escapeHtml(info.picture) + '" alt="">';
            else chatHeaderAvatar.textContent = (info.title || '?')[0].toUpperCase();

            if (!info.isGroupChat && info.membersId && info.membersId.length > 0) {
                var peerId = info.membersId.find(function (id) { return id !== myUserId; });
                if (peerId) {
                    getUser(peerId).then(function (peer) {
                        if (chatId !== currentChatId || !peer) return;
                        var fav = peer.profilePicturePreview || peer.profilePicture || info.picture || null;
                        setChatTabContext(chatTabTitle(peer), fav);

                        currentChatPeerIsBot = !!peer.isBot;
                        setChatCallButtonsVisible(!currentChatPeerIsBot);
                        if (currentChatPeerIsBot) {
                            chatHeaderStatus.hidden = true;
                            return;
                        }

                        BF.presence.subscribeFor([peerId]);
                        // Fetch current online status via unary RPC to show immediately
                        BF.api.getOnlineStatus([peerId]).then(function (data) {
                            if (data && data.statuses && data.statuses.length > 0) {
                                var s = data.statuses[0];
                                BF.presence.handleStatus(s.userId, s.status, s.lastSeen);
                            }
                        }).catch(function () {});
                    }).catch(function () {
                        loadingMessages.classList.remove('visible');
                        showToast(BF.i18n.t('common.loadError'), true);
                    });
                }
            } else {
                chatHeaderStatus.textContent = BF.i18n.tp('group.memberCount', info.membersId ? info.membersId.length : 0);
                chatHeaderStatus.classList.remove('online');
                chatHeaderStatus.hidden = false;
                setChatCallButtonsVisible(true);
                // Для группового чата — сбрасываем кастомный контекст вкладки
                resetChatTabContext();
            }

            var fromId = info.firstUnreadMessageId || 0;
            return BF.api.listMessages(chatId, fromId, 30, 10);
        }).then(function (data) {
            loadingMessages.classList.remove('visible');
            if (data && data.messages) {
                messages = data.messages;
                BF.sendQueue.mergePendingUploadsIntoMessages(chatId);
                var unreadId = currentChatInfo && currentChatInfo.firstUnreadMessageId;
                renderMessages().then(function () { settleScroll(unreadId); });
                BF.markRead.schedule();
                restoreChatDraft(chatId);
            }
        }).catch(function () { loadingMessages.classList.remove('visible'); });
    }

    // ========== MESSAGE FEED ==========

    BF.feed.init({
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatType: function () { return currentChatType; },
        getCurrentChatInfo: function () { return currentChatInfo; },
        getMyUserId: function () { return myUserId; },
        getMessages: function () { return messages; },
        setMessages: function (value) { messages = value; },
        getUser: getUser,
        showMediaOverlay: showMediaOverlay,
        mergePendingUploads: BF.sendQueue.mergePendingUploadsIntoMessages,
        onPendingCancel: BF.sendQueue.cancelPendingSend,
        onPendingRetry: BF.sendQueue.retryPendingSend,
        decryptPrivateBatch: BF.privateChatUI.decryptMessages,
        showToast: showToast
    });
    var renderMessages = BF.feed.render;
    var appendMessageToView = BF.feed.append;
    var scrollToBottom = BF.feed.scrollToBottom;
    var settleScroll = BF.feed.settleScroll;
    var scrollToMessage = BF.feed.scrollToMessage;
    var buildMessageViewElement = BF.feed.buildElement;

    // ========== SEND QUEUE (pending sends, uploads, attach) ==========

    BF.sendQueue.init({
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatType: function () { return currentChatType; },
        getMyUserId: function () { return myUserId; },
        getChats: function () { return chats; },
        getMessages: function () { return messages; },
        renderChatList: renderChatList,
        showToast: showToast
    });

    // ========== COMPOSER ==========

    BF.composer.init({
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatType: function () { return currentChatType; },
        getMessages: function () { return messages; },
        getMyUserId: function () { return myUserId; },
        getUser: getUser,
        renderChatList: renderChatList,
        onSubmit: BF.sendQueue.sendMessage,
        onSendFiles: BF.sendQueue.sendMessageWithFiles
    });
    var stopTypingSend = BF.composer.stopTyping;
    var setPendingReply = BF.composer.setReply;
    var clearPendingReply = BF.composer.clearReply;
    var setPendingEdit = BF.composer.setEdit;
    var clearPendingEdit = BF.composer.clearEdit;
    var restoreChatDraft = BF.composer.restoreDraft;

    // ========== MARK AS READ ==========

    BF.markRead.init({
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatType: function () { return currentChatType; },
        getMessages: function () { return messages; },
        getMyUserId: function () { return myUserId; },
        showToast: showToast
    });

    // ========== MESSAGE EVENTS (incoming, read, edited, deleted, pinned) ==========

    BF.messageEvents.init({
        getChats: function () { return chats; },
        getCurrentChatId: function () { return currentChatId; },
        getMyUserId: function () { return myUserId; },
        getMessages: function () { return messages; },
        reconcilePendingUpload: BF.sendQueue.reconcilePendingUpload,
        renderChatList: renderChatList,
        loadChats: loadChats,
        showNewMessageNotification: showNewMessageNotification,
        updateTitleBadge: updateTitleBadge,
        appendMessageToView: appendMessageToView,
        scrollToBottom: scrollToBottom,
        renderMessages: renderMessages,
        buildMessageViewElement: buildMessageViewElement,
        showToast: showToast
    });
    var applyMessageEdit = BF.messageEvents.applyEdit;
    var applyMessageDelete = BF.messageEvents.applyDelete;

    // ========== CONNECTION STATUS, RESYNC, RETURN TO THE TAB ==========

    BF.connection.init({
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatType: function () { return currentChatType; },
        getMyUserId: function () { return myUserId; },
        getMessages: function () { return messages; },
        setMessages: function (value) { messages = value; },
        setCurrentChatInfo: function (value) { currentChatInfo = value; },
        refreshChatList: refreshChatListQuiet,
        reloadPrivateChat: function () { return reloadCurrentPrivateChat(); },
        mergePendingUploads: BF.sendQueue.mergePendingUploadsIntoMessages,
        reconcilePendingUpload: BF.sendQueue.reconcilePendingUpload,
        renderMessages: renderMessages,
        appendMessageToView: appendMessageToView,
        scrollToBottom: scrollToBottom,
        applyMessageDelete: applyMessageDelete,
        applyMessageEdit: applyMessageEdit
    });

    // ========== SEARCH ==========

    BF.search.init({
        getChats: function () { return chats; },
        openChat: openChat
    });

    // ========== PROFILE OVERLAY ==========

    BF.chatMedia.init({
        getCurrentChatId: function () { return currentChatId; },
        showMediaOverlay: showMediaOverlay
    });
    BF.chatBackground.init();
    BF.profile.init({
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatInfo: function () { return currentChatInfo; },
        isUserOnline: BF.presence.isOnline,
        getOnlineEntry: BF.presence.getEntry,
        botBadgeMarkup: botBadgeMarkup,
        setCallButtonsVisible: setProfileCallButtonsVisible,
        showToast: showToast
    });

    var groupMediaPanels = BF.chatMedia.createPanels(groupMediaContent);

    BF.groupInfo.init({
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatInfo: function () { return currentChatInfo; },
        getMyUserId: function () { return myUserId; },
        getChats: function () { return chats; },
        getUser: getUser,
        renderChatList: renderChatList,
        showToast: groupToast,
        escapeHtml: u.escapeHtml,
        chatHeaderName: chatHeaderName,
        chatHeaderAvatar: chatHeaderAvatar,
        groupMediaPanels: groupMediaPanels,
        setMediaTabActive: BF.chatMedia.setTabActive,
        renderChatMedia: BF.chatMedia.render,
        openChatBackgroundSelector: BF.chatBackground.open
    });
    var openGroupInfo = BF.groupInfo.open;

    function onChatHeaderClick() {
        if (!currentChatInfo) return;
        if (currentChatInfo.isGroupChat) { openGroupInfo(); return; }
        var peerId = (currentChatInfo.membersId || []).find(function (id) { return id !== myUserId; });
        if (peerId) BF.profile.open(peerId);
    }
    chatHeaderAvatar.addEventListener('click', onChatHeaderClick);
    chatHeaderName.addEventListener('click', onChatHeaderClick);

    // ========== GROUP INFO PANEL ==========

    function showToast(text, isError) {
        if (!soonToastEl) return;
        if (showToast._t) clearTimeout(showToast._t);
        soonToastEl.textContent = text;
        soonToastEl.classList.toggle('error', !!isError);
        soonToastEl.classList.add('visible');
        showToast._t = setTimeout(function () {
            soonToastEl.classList.remove('visible');
            soonToastEl.classList.remove('error');
            soonToastEl.textContent = BF.i18n.t('common.comingSoon');
        }, 1800);
    }

    function groupToast(text) {
        showToast(text, false);
    }

    // ========== SETTINGS MODAL ==========

    BF.settings.init({ myUserId: myUserId });
    BF.attach.init();
    BF.sidebarResize.init();
    if (BF.pendingSends) {
        BF.pendingSends.init(myUserId);
        BF.sendQueue.restorePendingSends();
    }
    if (BF.drafts) BF.drafts.init(myUserId);
    if (BF.imageEditor) BF.imageEditor.init();
    $('#navChats').addEventListener('click', function () { /* already on chats page */ });
    $('#navSettings').addEventListener('click', function () { BF.settings.open(); });

    // ========== STICKER PICKER ==========

    // Отправленный стикер: в ленту (если чат всё ещё открыт) и наверх списка чатов.
    function onStickerSent(sentChatId, msg) {
        if (sentChatId === currentChatId && !messages.some(function (m) { return m.id === msg.id; })) {
            messages.push(msg);
            appendMessageToView(msg).then(scrollToBottom);
        }
        var chatIdx = chats.findIndex(function (c) { return c.id === sentChatId; });
        if (chatIdx >= 0) {
            var chat = chats[chatIdx];
            chat.lastMessage = msg;
            chats.splice(chatIdx, 1);
            chats.unshift(chat);
            renderChatList();
        }
    }

    BF.stickerPicker.init({
        getMyUserId: function () { return myUserId; },
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatType: function () { return currentChatType; },
        onSent: onStickerSent,
        showToast: showToast
    });

    // ========== FORWARD DIALOG ==========

    BF.forward.init({
        getChats: function () { return chats; },
        showToast: showToast
    });

    // ========== MESSAGE CONTEXT MENU ==========

    BF.messageMenu.init({
        getCurrentChatType: function () { return currentChatType; },
        getMessages: function () { return messages; },
        setReply: setPendingReply,
        setEdit: setPendingEdit,
        forward: BF.forward.open,
        requestDelete: BF.messageEvents.requestDelete,
        showToast: showToast
    });
    var closeContextMenu = BF.messageMenu.close;

    // ========== PROACTIVE TOKEN REFRESH ==========

    setInterval(function () {
        if (BF.tokens.isAccessExpired()) {
            BF.clients.refreshToken().then(function (token) {
                if (token) BF.realtime.reconnect();
            });
        }
    }, 60000);

    // ========== DEEP-LINK (cookie, ?chat=, push) ==========

    BF.deepLink.init({
        getChats: function () { return chats; },
        loadChats: loadChats,
        openChat: openChat
    });

    // ========== INIT ==========

    if (BF.push && BF.push.init) BF.push.init();

    BF.privateChatUI.init({
        getCurrentChatId: function () { return currentChatId; },
        setCurrentChatId: function (chatId) { currentChatId = chatId; },
        getCurrentChatType: function () { return currentChatType; },
        setCurrentChatType: function (chatType) { currentChatType = chatType; },
        setCurrentChatInfo: function (chatInfo) { currentChatInfo = chatInfo; },
        setCurrentChatPeerIsBot: function (isBot) { currentChatPeerIsBot = isBot; },
        getMyUserId: function () { return myUserId; },
        getChats: function () { return chats; },
        getMessages: function () { return messages; },
        setMessages: function (value) { messages = value; BF.feed.clearNewerGap(true); },
        setNoMoreOlder: BF.feed.setNoMoreOlder,
        stopTypingSend: stopTypingSend,
        updateOpenChatUrl: updateOpenChatUrl,
        clearPendingReply: clearPendingReply,
        clearPendingEdit: clearPendingEdit,
        closeContextMenu: closeContextMenu,
        setChatCallButtonsVisible: setChatCallButtonsVisible,
        resetChatTabContext: resetChatTabContext,
        setChatTabContext: setChatTabContext,
        chatTabTitle: chatTabTitle,
        getUser: getUser,
        escapeHtml: u.escapeHtml,
        renderChatList: renderChatList,
        updateTitleBadge: updateTitleBadge,
        renderMessages: renderMessages,
        scrollToBottom: scrollToBottom,
        appendMessageToView: appendMessageToView,
        loadChats: loadChats,
        showNewMessageNotification: showNewMessageNotification,
        announceIncoming: BF.feed.announceIncoming
    });
    var openPrivateChat = BF.privateChatUI.open;
    var reloadCurrentPrivateChat = BF.privateChatUI.reload;

    window.addEventListener('bf-pwa-update', function () {
        if (window.confirm(BF.i18n.t('pwa.updateAvailable'))) BF.push.applyUpdate();
    });

    if (BF.pinned && BF.pinned.init) {
        BF.pinned.init({
            getMyUserId: function () { return myUserId; },
            getCurrentChatInfo: function () { return currentChatInfo; },
            getUser: getUser,
            showMediaOverlay: showMediaOverlay,
            scrollToMessage: scrollToMessage
        });
    }

    // Первый рендер — только после загрузки словаря, иначе список успеет отрисоваться на ключах.
    BF.i18n.ready.then(function () {
        if (BF.folders && BF.folders.init) {
            BF.folders.setOnChange(function () { renderChatList(); });
            return BF.folders.init().then(function () {
                return loadChats(true);
            });
        }
        return loadChats(true);
    }).then(updateTitleBadge).then(BF.deepLink.openFromCookie).then(BF.deepLink.openFromUrl).then(BF.deepLink.openPending);

    // Смена языка в настройках — перерисовать динамические части интерфейса.
    BF.i18n.onChange(function () {
        renderChatList();
        updateTitleBadge();
        if (BF.folders && BF.folders.renderTabs) BF.folders.renderTabs();
        if (currentChatId) {
            if (currentChatType === 1) {
                var chat = chats.find(function (c) { return c.id === currentChatId; });
                if (chat) openPrivateChat(chat);
            } else {
                openChat(currentChatId);
            }
        }
    });

    if (navigator.serviceWorker) {
        navigator.serviceWorker.addEventListener('message', function (event) {
            var data = event.data || {};
            if (data.type === 'bf-push-open') BF.deepLink.openFromPush(data.chatId);
        });
    }

    if (BF.newchat && BF.newchat.init) {
        BF.newchat.init({
            openChat: openChat,
            getMyUserId: function () { return myUserId; },
            upsertChat: function (chat) {
                var idx = chats.findIndex(function (c) { return c.id === chat.id; });
                if (idx >= 0) chats[idx] = chat; else chats.unshift(chat);
                renderChatList();
                openChat(chat.id);
                if (window.__mobileShowChat) window.__mobileShowChat();
            }
        });
    }

    if (BF.cmdPalette && BF.cmdPalette.init) {
        BF.cmdPalette.init({
            getChats: function () { return chats; },
            openChat: function (chatId) {
                openChat(chatId);
                if (window.__mobileShowChat) window.__mobileShowChat();
            }
        });
    }

    if (BF.stickerPack && BF.stickerPack.init) {
        BF.stickerPack.init({ onStickerSend: BF.stickerPicker.send });
    }

    BF.realtime.startAll();
    if (BF.calls && BF.calls.start) BF.calls.start();

    // Метаданные ноды (в т.ч. отдельный файловый адрес): на самой ноде экрана выбора
    // не было, а адрес мог и поменяться. Промах не мешает — файлы пойдут по адресам Files.
    BF.node.refreshMeta();

    if (BF.personalization && BF.personalization.init) BF.personalization.init();

})();
