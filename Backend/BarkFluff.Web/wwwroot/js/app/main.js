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
    var chatListUserIdsLoaded = new Set();

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
    var MAX_MESSAGES = 200; // скользящее окно ленты: сколько сообщений держим в DOM
    var messages = [];
    var isLoadingOlder = false;
    var noMoreOlder = false;
    var isLoadingNewer = false;
    var hasNewerGap = false;      // хвост буфера обрезан — окно не доходит до конца чата
    var isJumpingToTail = false;
    var isJumpingToMessage = false;
    var resyncSeparatorId = null; // id первого сообщения после resync-пропуска (разделитель «Новые сообщения»)
    var markReadTimer = null;
    var markReadPending = new Set();
    var onlineSubscribedUserIds = new Set();
    var onlineStatuses = new Map();
    var typingUsers = new Map();      // userId -> timeout handle
    var typingSendActive = false;
    var typingLastInputAt = 0;
    var typingSendTimer = null;
    var chatListOffset = 0;
    var chatListTotal = 0;
    var chatListLoading = false;
    var chatListRequest = null;
    var pendingUploads = new Map(); // local message id -> optimistic upload state
    var pendingFileSelectionEntry = null;
    var GENERIC_MESSAGE_TYPE = 1;
    var IMAGE_UPLOAD_TYPE = 2;
    var GIF_UPLOAD_TYPE = 4;

    function newOperationId() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') return window.crypto.randomUUID();
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0;
            return (c === 'x' ? r : (r & 3 | 8)).toString(16);
        });
    }

    // Reply / Forward / Context menu state
    var pendingReply = null;
    var pendingEdit = null; // { messageId, originalText }
    var contextMenuTarget = null;
    var forwardSelection = new Set();
    var cmenuShownAt = 0;
    var mqlMobile = window.matchMedia('(max-width: 768px), (pointer: coarse)');

    // --- DOM refs ---
    var $ = function (sel) { return document.querySelector(sel); };
    var chatListEl = $('#chatList');
    var searchInput = $('#searchInput');
    var searchResults = $('#searchResults');
    var chatHeader = $('#chatHeader');
    var chatHeaderAvatar = $('#chatHeaderAvatar');
    var chatHeaderName = $('#chatHeaderName');
    var chatHeaderStatus = $('#chatHeaderStatus');
    var chatEmpty = $('#chatEmpty');
    var messagesArea = $('#messagesArea');
    var messagesInner = $('#messagesInner');
    var loadingMessages = $('#loadingMessages');
    var inputBar = $('#inputBar');
    var messageInput = $('#messageInput');
    var sendBtn = $('#sendBtn');
    var attachBtn = $('#attachBtn');
    var fileInput = $('#fileInput');
    // Scroll-to-bottom button
    var scrollToBottomBtn = $('#scrollToBottomBtn');
    var scrollBadge = scrollToBottomBtn ? scrollToBottomBtn.querySelector('.scroll-badge') : null;
    var newMessagesBelowCount = 0;

    // Reply / Forward / Context menu DOM refs
    var msgContextMenu = $('#msgContextMenu');
    var replyPreviewBar = $('#replyPreviewBar');
    var rpbAuthor = $('#rpbAuthor');
    var rpbText = $('#rpbText');
    var rpbCloseBtn = $('#rpbClose');
    var editPreviewBar = $('#editPreviewBar');
    var epbText = $('#epbText');
    var epbCloseBtn = $('#epbClose');
    var deleteMsgConfirmOverlay = $('#deleteMsgConfirmOverlay');
    var deleteMsgCancel = $('#deleteMsgCancel');
    var deleteMsgOk = $('#deleteMsgOk');
    var forwardOverlay = $('#forwardOverlay');
    var forwardCloseBtn = $('#forwardClose');
    var forwardChatListEl = $('#forwardChatList');
    var forwardCommentEl = $('#forwardComment');
    var forwardSendBtn = $('#forwardSendBtn');
    var forwardCounterEl = $('#forwardCounter');
    var soonToastEl = $('#soonToast');

    // Settings and confirm overlays are managed by BF.settings module

    // Sticker picker elements
    var stickerBtn = $('#stickerBtn');
    var stickerPicker = $('#stickerPicker');
    var stickerSearch = $('#stickerSearch');
    var stickerPacksBar = $('#stickerPacksBar');
    var stickerGrid = $('#stickerGrid');

    // Profile elements
    var profileOverlay = $('#profileOverlay');
    var profileClose = $('#profileClose');
    var profilePoster = $('#profilePoster');
    var profileAvatar = $('#profileAvatar');
    var profileName = $('#profileName');
    var profileUsername = $('#profileUsername');
    var profileStatus = $('#profileStatus');
    var profileBio = $('#profileBio');
    var profileBadges = $('#profileBadges');
    var profileRegDate = $('#profileRegDate');
    var profileMediaContent = $('#profileMediaContent');
    var currentProfileUserId = null;

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

    function setChatCallButtonsVisible(visible) {
        ['btnCallAudio', 'btnCallVideo'].forEach(function (id) {
            var button = document.getElementById(id);
            if (button) button.hidden = !visible;
        });
    }

    function setProfileCallButtonsVisible(visible) {
        ['profileCallAudioBtn', 'profileCallVideoBtn'].forEach(function (id) {
            var button = document.getElementById(id);
            if (button) button.hidden = !visible;
        });
    }

    function chatTabTitle(user) {
        var name = ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || BF.i18n.t('common.user');
        return BF.i18n.t('tab.chatWith', { name: name });
    }

    // ========== CHAT LIST ==========

    function loadChats(reset) {
        if (chatListLoading) return chatListRequest || Promise.resolve(false);
        if (!reset && chats.length >= chatListTotal && chatListTotal > 0) return Promise.resolve();

        chatListLoading = true;
        if (reset) { chatListOffset = 0; chats = []; }

        var request = BF.api.listChats(chatListOffset, 50).then(function (data) {
            if (!data || !data.chats) return false;
            chatListTotal = data.totalCount;
            chats = reset ? data.chats : chats.concat(data.chats);
            chats.sort(function (a, b) {
                var bt = (b.lastMessage && b.lastMessage.sentAt) || b.lastActivityAt || 0;
                var at = (a.lastMessage && a.lastMessage.sentAt) || a.lastActivityAt || 0;
                return bt - at;
            });
            chatListOffset = chats.length;
            renderChatList();
            collectOnlineUserIds();
            loadChatListUsers();
            return true;
        }).catch(function () {
            return false;
        }).then(function (result) {
            if (chatListRequest === request) {
                chatListLoading = false;
                chatListRequest = null;
            }
            return result;
        });
        chatListRequest = request;
        return request;
    }

    function loadChatListUsers() {
        var userIds = [];
        chats.forEach(function (chat) {
            if (chat.isGroupChat || !chat.members) return;
            chat.members.forEach(function (member) {
                if (member.userId !== myUserId && !userCache.has(member.userId) && !chatListUserIdsLoaded.has(member.userId)) {
                    chatListUserIdsLoaded.add(member.userId);
                    userIds.push(member.userId);
                }
            });
        });
        if (userIds.length === 0) return;

        var chain = Promise.resolve();
        for (var i = 0; i < userIds.length; i += 5) {
            (function (batch) {
                chain = chain.then(function () { return Promise.all(batch.map(getUser)); });
            })(userIds.slice(i, i + 5));
        }
        chain.then(function () {
            renderChatList();
            collectOnlineUserIds();
        }).catch(function () {
            userIds.forEach(function (id) { chatListUserIdsLoaded.delete(id); });
        });
    }

    function renderChatList() {
        if (BF.folders && BF.folders.renderTabs) BF.folders.renderTabs(chats);
        chatListEl.innerHTML = '';
        var visibleChats = (BF.folders && BF.folders.filterChats) ? BF.folders.filterChats(chats) : chats;
        visibleChats.forEach(function (chat) {
            var el = document.createElement('div');
            el.className = 'chat-item' + (chat.id === currentChatId ? ' active' : '');
            el.dataset.chatId = chat.id;
            el.tabIndex = 0;
            el.setAttribute('role', 'button');
            el.setAttribute('aria-label', chat.title || BF.i18n.t('common.chat'));

            var avatarInitial = (chat.title || '?')[0].toUpperCase();
            var avatarHtml = chat.picture
                ? '<img src="' + u.escapeHtml(chat.picture) + '" alt="">'
                : avatarInitial;

            var isPrivate = chat.chatType === 1;
            var lm = chat.lastMessage;
            var hasDraft = chat.chatType === 0 && ((chat.hasDraft === true) || (BF.drafts && BF.drafts.has(chat.id)));
            var previewHtml = '';
            if (hasDraft) {
                previewHtml = '<span class="preview-draft">' + u.escapeHtml(BF.i18n.t('chatlist.draft')) + '</span>';
            } else if (isPrivate) {
                // Содержимое зашифровано — сервер (и превью) его не знает.
                if (chat.privateInviteState === 0) {
                    previewHtml = chat.privateInviterUserId === myUserId
                        ? u.escapeHtml(BF.i18n.t('privatechat.waitingPeer'))
                        : '<span class="preview-private-invite">' + u.escapeHtml(BF.i18n.t('privatechat.invite')) + '</span>';
                } else if (chat.privateInviteState === 2) {
                    previewHtml = u.escapeHtml(BF.i18n.t('privatechat.inviteRejected'));
                } else {
                    previewHtml = u.escapeHtml(BF.i18n.t('privatechat.encrypted'));
                }
            } else if (lm) {
                var text = (lm.content && lm.content.text) || '';
                var ac = (lm.content && lm.content.attachments && lm.content.attachments.length) || 0;
                var plainText = u.markdownToPlainText(text);
                var plainTextPreview = plainText
                    ? '<span class="preview-text">' + u.escapeHtml(u.truncate(plainText, 50)) + '</span>'
                    : '';
                if (lm.type === 2 || lm.type === 'SYSTEM') {
                    previewHtml = u.callPreviewHtml(text, lm.senderId === myUserId) || plainTextPreview;
                } else if (text) {
                    previewHtml = plainTextPreview;
                } else if (ac > 0) {
                    previewHtml = u.attachmentPreviewHtml(lm.content.attachments[0].type);
                }
            }

            var timeTs = (lm && lm.sentAt) || (isPrivate ? chat.lastActivityAt : null);
            var time = timeTs ? u.formatChatListTime(timeTs) : '';
            var unread = chat.countUnread || 0;
            var unreadText = unread > 99 ? '99+' : unread;

            var peerUserId = null;
            if (!chat.isGroupChat && chat.members && chat.members.length > 0) {
                var peer = chat.members.find(function (m) { return m.userId !== myUserId; });
                if (peer) peerUserId = peer.userId;
            }
            var peerUser = peerUserId ? userCache.get(peerUserId) : null;
            var isBot = !!(peerUser && peerUser.isBot);
            var onlineDot = peerUser && !isBot
                ? '<div class="online-dot' + (peerUserId && isUserOnline(peerUserId) ? ' visible' : '') + '" data-online-user="' + (peerUserId || '') + '"></div>'
                : '';

            el.innerHTML =
                '<div class="chat-avatar">' + avatarHtml +
                onlineDot + '</div>' +
                '<div class="chat-info"><div class="chat-info-top">' +
                '<span class="chat-name">' + (isPrivate ? '<span class="chat-lock" title="' + u.escapeHtml(BF.i18n.t('newchat.mode.private')) + '">' + BF.icons.html('security') + '</span>' : '') + u.escapeHtml(chat.title || BF.i18n.t('common.chat')) + (isBot ? botBadgeMarkup() : '') + '</span>' +
                '<span class="chat-time">' + time + '</span></div>' +
                '<div class="chat-info-bottom"><span class="chat-preview">' + previewHtml + '</span>' +
                '<span class="chat-unread' + (unread > 0 ? ' visible' : '') + '">' + unreadText + '</span></div></div>';

            el.addEventListener('click', function () { openChat(chat.id); });
            el.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openChat(chat.id);
                }
            });
            chatListEl.appendChild(el);
        });
    }

    // Тихое фоновое обновление списка чатов (catch-up после реконнекта/возврата на
    // вкладку): тянем первую страницу, сравниваем сигнатуру с текущим состоянием и
    // трогаем DOM только при реальном различии.
    function chatSignature(c) {
        var lm = c.lastMessage;
        return [
            c.id, c.title, c.picture, c.countUnread || 0, c.privateInviteState,
            c.lastActivityAt || 0,
            lm ? (lm.id + '|' + (lm.editedAt || 0) + '|' + ((lm.content && lm.content.text) || '')) : ''
        ].join('');
    }

    function refreshChatListQuiet() {
        if (chatListLoading) return chatListRequest || Promise.resolve(false);
        chatListLoading = true;

        var request = BF.api.listChats(0, 50).then(function (data) {
            if (!data || !data.chats) return false;
            var fetched = data.chats.slice();
            fetched.sort(function (a, b) {
                var bt = (b.lastMessage && b.lastMessage.sentAt) || b.lastActivityAt || 0;
                var at = (a.lastMessage && a.lastMessage.sentAt) || a.lastActivityAt || 0;
                return bt - at;
            });

            var same = data.totalCount === chatListTotal && fetched.length <= chats.length;
            if (same) {
                for (var i = 0; i < fetched.length; i++) {
                    if (chatSignature(fetched[i]) !== chatSignature(chats[i])) { same = false; break; }
                }
            }
            if (same) return true; // ничего не изменилось — DOM не трогаем

            chats = fetched;
            chatListTotal = data.totalCount;
            chatListOffset = chats.length;
            renderChatList();
            collectOnlineUserIds();
            loadChatListUsers();
            updateTitleBadge();
            return true;
        }).catch(function () {
            return false;
        }).then(function (result) {
            if (chatListRequest === request) {
                chatListLoading = false;
                chatListRequest = null;
            }
            return result;
        });
        chatListRequest = request;
        return request;
    }

    chatListEl.addEventListener('scroll', function () {
        if (chatListEl.scrollTop + chatListEl.clientHeight >= chatListEl.scrollHeight - 100) loadChats();
    });

    // ========== TYPING INDICATOR ==========

    function stopTypingSend(sendCancel) {
        if (typingSendTimer) { clearInterval(typingSendTimer); typingSendTimer = null; }
        if (sendCancel && typingSendActive) {
            BF.api.setTypingStatus(currentChatId, false).catch(function () {});
        }
        typingSendActive = false;
    }

    function clearTypingReceiveState() {
        typingUsers.forEach(function (timeoutHandle) { clearTimeout(timeoutHandle); });
        typingUsers.clear();
    }

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
        clearTypingReceiveState();

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
        noMoreOlder = false;
        hasNewerGap = false;
        isLoadingNewer = false;
        isJumpingToTail = false;
        resyncSeparatorId = null;
        clearPendingReply(false);
        clearPendingEdit();
        closeContextMenu();
        if (scrollToBottomBtn) scrollToBottomBtn.classList.remove('visible');
        newMessagesBelowCount = 0;
        updateScrollBadge();
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

                        subscribeOnlineForUsers([peerId]);
                        // Fetch current online status via unary RPC to show immediately
                        BF.api.getOnlineStatus([peerId]).then(function (data) {
                            if (data && data.statuses && data.statuses.length > 0) {
                                var s = data.statuses[0];
                                handleOnlineStatus(s.userId, s.status, s.lastSeen);
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
                mergePendingUploadsIntoMessages(chatId);
                var unreadId = currentChatInfo && currentChatInfo.firstUnreadMessageId;
                renderMessages().then(function () { settleScroll(unreadId); });
                scheduleMarkRead();
                restoreChatDraft(chatId);
            }
        }).catch(function () { loadingMessages.classList.remove('visible'); });
    }

    function restoreChatDraft(chatId) {
        if (!BF.drafts || chatId !== currentChatId) return;
        BF.drafts.load(chatId).then(function (draft) {
            if (!draft || chatId !== currentChatId) return;
            messageInput.value = draft.text || '';
            messageInput.style.height = 'auto';
            messageInput.style.height = Math.min(messageInput.scrollHeight, 120) + 'px';
            if (draft.replyToMessageId) {
                var reply = messages.find(function (m) { return Number(m.id) === Number(draft.replyToMessageId); });
                if (reply) {
                    setPendingReply(reply, false);
                } else {
                    BF.api.listMessages(chatId, draft.replyToMessageId, 1, 1).then(function (data) {
                        if (chatId !== currentChatId || !data || !data.messages) return;
                        var loadedReply = data.messages.find(function (m) { return Number(m.id) === Number(draft.replyToMessageId); });
                        if (loadedReply) setPendingReply(loadedReply, false);
                        else BF.drafts.set(chatId, draft.text || '', 0);
                    }).catch(function () {});
                }
            }
            renderChatList();
        });
    }

    function saveCurrentDraft() {
        if (!currentChatId || currentChatType !== 0 || pendingEdit || !BF.drafts) return;
        BF.drafts.set(currentChatId, messageInput.value, pendingReply ? pendingReply.messageId : 0);
        renderChatList();
    }

    // Скроллит к первому непрочитанному (если есть) либо в самый низ чата.
    function settleScroll(unreadId) {
        function anchor() {
            var el = unreadId && messagesInner.querySelector('[data-msg-id="' + unreadId + '"]');
            if (el) el.scrollIntoView({ block: 'start' });
            else scrollToBottom();
        }
        anchor();
        resettleAfterImages(anchor);
    }

    // Скроллит цель прыжка в центр вьюпорта и подсвечивает её (animation msgHighlight).
    function settleHighlight(id) {
        function anchor() {
            var el = messagesInner.querySelector('[data-msg-id="' + id + '"]');
            if (el) el.scrollIntoView({ block: 'center' });
        }
        anchor();
        var el = messagesInner.querySelector('[data-msg-id="' + id + '"]');
        if (el) {
            el.classList.add('highlight');
            setTimeout(function () { el.classList.remove('highlight'); }, 1500);
        }
        resettleAfterImages(anchor);
    }

    // Повторяет anchor, когда догрузятся картинки сообщений — без этого reflow
    // от картинок сбивает позицию скролла после открытия чата или прыжка к сообщению.
    function resettleAfterImages(anchor) {
        var pending = Array.prototype.filter.call(messagesInner.querySelectorAll('img'), function (im) { return !im.complete; });
        if (pending.length === 0) return;
        var settled = false;
        var remaining = pending.length;
        function settle() {
            if (settled) return;
            settled = true;
            anchor();
        }
        pending.forEach(function (im) {
            im.addEventListener('load', onOneDone);
            im.addEventListener('error', onOneDone);
        });
        function onOneDone() {
            remaining--;
            if (remaining <= 0) settle();
        }
        setTimeout(settle, 1500);
    }

    // ========== RENDER MESSAGES ==========

    function collectFwdAttachments(msg) {
        var atts = (msg.content && msg.content.attachments) || [];
        var inner = [];
        atts.forEach(function (a) {
            if (a.forwardedMessage && a.forwardedMessage.attachments) {
                a.forwardedMessage.attachments.forEach(function (ia) { inner.push(ia); });
            }
        });
        return inner;
    }

    function makeDateSeparator(msgDate) {
        var sep = document.createElement('div');
        sep.className = 'msg-date-separator';
        sep.dataset.date = msgDate;
        sep.innerHTML = '<span>' + u.escapeHtml(msgDate) + '</span>';
        return sep;
    }

    function makeUnreadSeparator(i18nKey) {
        var usep = document.createElement('div');
        usep.className = 'msg-unread-separator';
        usep.dataset.sepKey = i18nKey;
        usep.innerHTML = '<span>' + u.escapeHtml(BF.i18n.t(i18nKey)) + '</span>';
        return usep;
    }

    function prefetchAttachmentUrls(list) {
        var fileIds = [];
        list.forEach(function (msg) {
            ((msg.content && msg.content.attachments) || []).forEach(function (a) {
                if (a.fileId && !BF.files.getCachedFileUrl(a.fileId)) fileIds.push(a.fileId);
            });
            collectFwdAttachments(msg).forEach(function (a) {
                if (a.fileId && !BF.files.getCachedFileUrl(a.fileId)) fileIds.push(a.fileId);
            });
        });
        return fileIds.length > 0 ? BF.files.getFileUrls(fileIds) : Promise.resolve();
    }

    function renderMessages() {
        messagesInner.innerHTML = '';
        return prefetchAttachmentUrls(messages).then(function () {
            var chain = Promise.resolve();
            var lastDate = null;
            // Разделитель непрочитанных: якорь resync-догрузки («Новые сообщения»)
            // важнее первого непрочитанного из chat info. Приватные чаты якорь не
            // ставят (их resync идёт мимо resyncCurrentChatTail) — игнорируем.
            var sepId = currentChatType !== 1 && resyncSeparatorId ? resyncSeparatorId
                : (currentChatInfo && currentChatInfo.firstUnreadMessageId);
            var sepKey = currentChatType !== 1 && resyncSeparatorId ? 'chat.newMessages' : 'chat.unreadMessages';
            messages.forEach(function (msg, index) {
                chain = chain.then(function () {
                    var msgDate = u.formatDate(msg.sentAt);
                    if (msgDate !== lastDate) {
                        lastDate = msgDate;
                        messagesInner.appendChild(makeDateSeparator(msgDate));
                    }
                    if (sepId && Number(msg.id) === Number(sepId)) {
                        messagesInner.appendChild(makeUnreadSeparator(sepKey));
                    }
                    return BF.messages.buildMessageElement(msg, myUserId, getUser, showMediaOverlay, buildMessageOptions(msg, index)).then(function (el) {
                        el.dataset.date = msgDate;
                        messagesInner.appendChild(el);
                    });
                });
            });
            return chain;
        });
    }

    // Дорисовывает подгруженные старые сообщения перед лентой, не перестраивая её целиком.
    // Вызывается после того, как newMsgs уже добавлены в начало массива messages.
    function prependMessages(newMsgs) {
        var firstOldEl = messagesInner.firstElementChild;
        var oldFirstMsg = messages[newMsgs.length] || null;
        var lastDate = null;

        return prefetchAttachmentUrls(newMsgs).then(function () {
            var frag = document.createDocumentFragment();
            var chain = Promise.resolve();
            newMsgs.forEach(function (msg, index) {
                chain = chain.then(function () {
                    var msgDate = u.formatDate(msg.sentAt);
                    if (msgDate !== lastDate) {
                        lastDate = msgDate;
                        frag.appendChild(makeDateSeparator(msgDate));
                    }
                    return BF.messages.buildMessageElement(msg, myUserId, getUser, showMediaOverlay, buildMessageOptions(msg, index)).then(function (el) {
                        el.dataset.date = msgDate;
                        frag.appendChild(el);
                    });
                });
            });
            return chain.then(function () { return frag; });
        }).then(function (frag) {
            // Разделитель даты бывшего первого сообщения теперь дублирует вставленный блок.
            if (lastDate && firstOldEl && firstOldEl.classList.contains('msg-date-separator') &&
                firstOldEl.dataset.date === lastDate) firstOldEl.remove();
            messagesInner.insertBefore(frag, messagesInner.firstChild);

            // Группировка бывшего первого сообщения могла измениться: перед ним появился сосед.
            if (!oldFirstMsg || !canGroupMessages(newMsgs[newMsgs.length - 1], oldFirstMsg)) return;
            return buildMessageViewElement(oldFirstMsg).then(function (replacement) {
                var el = findMessageGroup(oldFirstMsg.id);
                if (!el || !el.isConnected) return;
                replacement.dataset.date = el.dataset.date;
                el.replaceWith(replacement);
            });
        });
    }

    // Скользящее окно: держим в буфере не больше MAX_MESSAGES сообщений.
    // 'tail' — после подгрузки старых, 'head' — после подгрузки новых.
    function trimMessages(side) {
        var extra = messages.length - MAX_MESSAGES;
        if (extra <= 0) return;

        var dropped = side === 'head'
            ? messages.splice(0, extra)
            : messages.splice(messages.length - extra, extra);

        var droppedIds = new Set(dropped.map(function (msg) { return String(msg.id); }));
        Array.prototype.slice.call(messagesInner.querySelectorAll('.msg-group')).forEach(function (node) {
            if (droppedIds.has(String(node.dataset.msgId))) node.remove();
        });
        removeOrphanSeparators();

        // Обрезав голову, снимаем флаг «старее ничего нет»: отрезанное снова можно догрузить.
        if (side === 'head') noMoreOlder = false;
        else hasNewerGap = true;
    }

    function removeOrphanSeparators() {
        Array.prototype.slice.call(messagesInner.querySelectorAll('.msg-date-separator, .msg-unread-separator')).forEach(function (sep) {
            var next = sep.nextElementSibling;
            if (!next || next.classList.contains('msg-date-separator')) {
                if (sep.dataset.sepKey === 'chat.newMessages') resyncSeparatorId = null;
                sep.remove();
            }
        });
    }

    function scrollToBottom() {
        if (hasNewerGap) { jumpToLiveTail(); return; }
        messagesArea.scrollTop = messagesArea.scrollHeight;
    }

    // Возврат к живому хвосту, когда скользящее окно обрезало последние сообщения.
    function jumpToLiveTail() {
        if (isJumpingToTail || isJumpingToMessage || !currentChatId) return;
        isJumpingToTail = true;
        var chatId = currentChatId;

        loadMessagesPage(chatId, 0, 30, 0).then(function (data) {
            if (chatId !== currentChatId || !data || !data.messages) return;
            messages = data.messages;
            mergePendingUploadsIntoMessages(chatId);
            hasNewerGap = false;
            noMoreOlder = false;
            resyncSeparatorId = null; // окно снова на живом хвосте — границы «нового» нет
            return renderMessages().then(function () {
                messagesArea.scrollTop = messagesArea.scrollHeight;
            });
        }).finally(function () { isJumpingToTail = false; });
    }

    function updateScrollBadge() {
        if (!scrollBadge) return;
        scrollBadge.textContent = newMessagesBelowCount > 0 ? String(newMessagesBelowCount) : '';
        scrollBadge.style.display = newMessagesBelowCount > 0 ? 'flex' : 'none';
    }

    function buildMessageViewElement(msg) {
        var atts = (msg.content && msg.content.attachments) || [];
        var fileIds = atts.map(function (a) { return a.fileId; }).filter(function (id) { return id && !BF.files.getCachedFileUrl(id); });
        collectFwdAttachments(msg).forEach(function (a) {
            if (a.fileId && !BF.files.getCachedFileUrl(a.fileId)) fileIds.push(a.fileId);
        });
        var p = fileIds.length > 0 ? BF.files.getFileUrls(fileIds) : Promise.resolve();

        return p.then(function () {
            return BF.messages.buildMessageElement(msg, myUserId, getUser, showMediaOverlay, buildMessageOptions(msg));
        });
    }

    function canGroupMessages(previous, current) {
        if (!previous || !current || previous.type === 2 || previous.type === 'SYSTEM' || current.type === 2 || current.type === 'SYSTEM') return false;
        if (previous.senderId !== current.senderId || !previous.sentAt || !current.sentAt) return false;
        return current.sentAt >= previous.sentAt &&
            current.sentAt - previous.sentAt <= 5 * 60 * 1000 &&
            u.formatDate(previous.sentAt) === u.formatDate(current.sentAt);
    }

    function buildMessageOptions(msg, index) {
        if (index == null) index = messages.indexOf(msg);
        if (index < 0) index = messages.findIndex(function (item) { return item.id === msg.id; });

        var previous = index > 0 ? messages[index - 1] : null;
        var next = index >= 0 && index < messages.length - 1 ? messages[index + 1] : null;
        var groupedWithPrevious = canGroupMessages(previous, msg);
        var showSenderGutter = !!(currentChatInfo && currentChatInfo.isGroupChat) && msg.senderId !== myUserId;
        return {
            onReplyClick: scrollToMessage,
            onPendingCancel: cancelPendingSend,
            onPendingRetry: retryPendingSend,
            groupedWithPrevious: groupedWithPrevious,
            showSenderGutter: showSenderGutter,
            showSenderAvatar: showSenderGutter && !canGroupMessages(msg, next)
        };
    }

    function appendMessageToView(msg, separatorKey) {
        // Хвост буфера обрезан — сообщение лежит за пределами загруженного окна. Не рисуем его
        // и убираем из массива, чтобы тот остался непрерывным: пользователь увидит сообщение,
        // когда вернётся к живому хвосту (кнопка «вниз» или прокрутка).
        if (hasNewerGap) {
            var gapIdx = messages.findIndex(function (m) { return String(m.id) === String(msg.id); });
            if (gapIdx >= 0) messages.splice(gapIdx, 1);
            return Promise.resolve();
        }
        return appendMessageElement(msg, separatorKey);
    }

    function appendMessageElement(msg, separatorKey) {
        var previous = messages.length > 1 ? messages[messages.length - 2] : null;
        var refreshPrevious = previous && currentChatInfo && currentChatInfo.isGroupChat &&
            previous.senderId !== myUserId && canGroupMessages(previous, msg)
            ? buildMessageViewElement(previous).then(function (replacement) {
                var previousEl = findMessageGroup(previous.id);
                if (!previousEl || !previousEl.isConnected) return;
                replacement.dataset.date = previousEl.dataset.date;
                previousEl.replaceWith(replacement);
            })
            : Promise.resolve();

        return refreshPrevious.then(function () {
            return buildMessageViewElement(msg).then(function (el) {
                var msgDate = u.formatDate(msg.sentAt);
                var lastMsgDate = null;
                for (var node = messagesInner.lastElementChild; node; node = node.previousElementSibling) {
                    if (node.dataset && node.dataset.date) { lastMsgDate = node.dataset.date; break; }
                }
                if (msgDate !== lastMsgDate) messagesInner.appendChild(makeDateSeparator(msgDate));
                if (separatorKey) messagesInner.appendChild(makeUnreadSeparator(separatorKey));
                el.dataset.date = msgDate;
                messagesInner.appendChild(el);
            });
        });
    }

    function releasePendingPreviews(entry) {
        if (!entry || !entry.previewUrls) return;
        entry.previewUrls.forEach(function (url) { URL.revokeObjectURL(url); });
        entry.previewUrls = [];
    }

    function findMessageGroup(messageId) {
        return Array.prototype.find.call(messagesInner.querySelectorAll('.msg-group'), function (node) {
            return String(node.dataset.msgId) === String(messageId);
        });
    }

    function removePendingUpload(entry) {
        if (!entry || entry.settled) return;
        entry.settled = true;
        pendingUploads.delete(entry.localId);
        if (BF.pendingSends && entry.operationId) BF.pendingSends.remove(entry.operationId);
        releasePendingPreviews(entry);

        var idx = messages.findIndex(function (m) { return String(m.id) === String(entry.localId); });
        if (idx >= 0) messages.splice(idx, 1);

        var el = findMessageGroup(entry.localId);
        if (el) el.remove();
    }

    function messageFileIds(msg) {
        return ((msg && msg.content && msg.content.attachments) || [])
            .map(function (a) { return a.fileId; })
            .filter(Boolean)
            .map(function (id) { return String(id).toLowerCase(); })
            .sort();
    }

    function pendingUploadMatches(entry, msg) {
        if (entry.operationId && msg && msg.clientOperationId &&
            String(entry.operationId).toLowerCase() === String(msg.clientOperationId).toLowerCase()) {
            return true;
        }
        var serverIds = messageFileIds(msg);
        var pendingIds = entry.fileIds.map(function (id) { return String(id).toLowerCase(); }).sort();
        return pendingIds.length > 0 &&
            pendingIds.length === serverIds.length &&
            pendingIds.every(function (id, index) { return id === serverIds[index]; });
    }

    function mergePendingUploadsIntoMessages(chatId) {
        pendingUploads.forEach(function (entry) {
            if (entry.settled || String(entry.chatId) !== String(chatId)) return;
            var serverMessage = messages.find(function (msg) { return pendingUploadMatches(entry, msg); });
            if (serverMessage) {
                entry.settled = true;
                pendingUploads.delete(entry.localId);
                if (BF.pendingSends && entry.operationId) BF.pendingSends.remove(entry.operationId);
                if (BF.drafts && entry.draftSnapshot) BF.drafts.clearSent(entry.chatId, entry.draftSnapshot);
                releasePendingPreviews(entry);
            } else {
                messages.push(entry.localMessage);
            }
        });
    }

    function findPendingUpload(chatId, msg) {
        if (!msg || msg.senderId !== myUserId) return null;

        var found = null;
        pendingUploads.forEach(function (entry) {
            if (found || entry.settled || String(entry.chatId) !== String(chatId)) return;
            if (pendingUploadMatches(entry, msg)) found = entry;
        });
        return found;
    }

    function replacePendingElement(entry, msg) {
        var oldEl = findMessageGroup(entry.localId);
        if (!oldEl) { releasePendingPreviews(entry); return; }

        buildMessageViewElement(msg).then(function (newEl) {
            newEl.dataset.date = u.formatDate(msg.sentAt);
            if (oldEl.isConnected) oldEl.replaceWith(newEl);
            releasePendingPreviews(entry);
        }).catch(function () {
            renderMessages().then(function () { releasePendingPreviews(entry); });
        });
    }

    function reconcilePendingUpload(chatId, msg, hintedEntry) {
        var entry = hintedEntry && !hintedEntry.settled ? hintedEntry : findPendingUpload(chatId, msg);
        if (!entry) return false;

        entry.settled = true;
        pendingUploads.delete(entry.localId);
        if (BF.pendingSends && entry.operationId) BF.pendingSends.remove(entry.operationId);
        if (BF.drafts && entry.draftSnapshot) BF.drafts.clearSent(entry.chatId, entry.draftSnapshot);

        if (String(chatId) !== String(currentChatId)) {
            releasePendingPreviews(entry);
            return true;
        }

        var localIdx = messages.findIndex(function (m) { return String(m.id) === String(entry.localId); });
        var serverIdx = messages.findIndex(function (m) { return m.id === msg.id; });
        if (serverIdx >= 0) {
            if (localIdx >= 0 && localIdx !== serverIdx) messages.splice(localIdx, 1);
        } else if (localIdx >= 0) {
            messages[localIdx] = msg;
        } else {
            messages.push(msg);
        }

        var wasAtBottom = messagesArea.scrollHeight - messagesArea.scrollTop - messagesArea.clientHeight < 300;
        if (localIdx >= 0 && serverIdx < 0) {
            replacePendingElement(entry, msg);
        } else {
            var oldEl = findMessageGroup(entry.localId);
            if (oldEl) oldEl.remove();
            releasePendingPreviews(entry);
            if (serverIdx < 0) appendMessageToView(msg).then(function () {
                if (wasAtBottom) scrollToBottom();
            });
        }
        if (wasAtBottom && localIdx >= 0 && serverIdx < 0) setTimeout(scrollToBottom, 0);
        return true;
    }

    function pendingSnapshot(entry) {
        return {
            operationId: entry.operationId,
            chatId: entry.chatId,
            generation: entry.draftSnapshot ? entry.draftSnapshot.generation : 0,
            text: entry.text || '',
            caption: entry.caption || '',
            replyToMessageId: entry.replyToMessageId || 0,
            fileIds: entry.fileIds.slice(),
            uploads: entry.uploads.map(function (upload) {
                return {
                    operationId: upload.operationId,
                    reservedFileId: upload.reservedFileId || '',
                    resultFileId: upload.resultFileId || '',
                    name: upload.name,
                    size: upload.size,
                    type: upload.type,
                    uploadType: upload.uploadType,
                    state: upload.state
                };
            }),
            state: entry.localMessage.pendingState,
            createdAt: entry.createdAt
        };
    }

    function persistPendingEntry(entry) {
        return BF.pendingSends && BF.pendingSends.put(pendingSnapshot(entry));
    }

    function refreshPendingElement(entry) {
        if (String(entry.chatId) !== String(currentChatId)) return;
        var oldEl = findMessageGroup(entry.localId);
        if (!oldEl) return;
        buildMessageViewElement(entry.localMessage).then(function (newEl) {
            newEl.dataset.date = oldEl.dataset.date;
            if (oldEl.isConnected) oldEl.replaceWith(newEl);
        });
    }

    function setPendingState(entry, state) {
        entry.localMessage.pendingState = state;
        persistPendingEntry(entry);
        refreshPendingElement(entry);
    }

    function restorePendingSends() {
        if (!BF.pendingSends) return;
        BF.pendingSends.all().forEach(function (stored) {
            var localId = 'pending-send-' + stored.operationId;
            var uploads = (stored.uploads || []).map(function (upload, index) {
                return Object.assign({}, upload, { index: index });
            });
            var attachments = uploads.map(function (upload, index) {
                var isImage = upload.uploadType === IMAGE_UPLOAD_TYPE || upload.uploadType === GIF_UPLOAD_TYPE;
                return {
                    type: isImage ? (upload.uploadType === GIF_UPLOAD_TYPE ? 'GIF' : 'IMAGE') : 'DOCUMENT',
                    fileId: upload.resultFileId || '',
                    fileName: upload.name,
                    attachmentSize: upload.size,
                    localPreviewUrl: '',
                    uploadProgress: upload.state === 'completed' ? 100 : 0,
                    uploadIndex: index,
                    isPending: true
                };
            });
            var localMessage = {
                id: localId,
                senderId: myUserId,
                readBy: [],
                sentAt: stored.createdAt || Date.now(),
                type: GENERIC_MESSAGE_TYPE,
                isPending: true,
                pendingState: uploads.some(function (upload) { return upload.state !== 'completed'; })
                    ? 'waiting-file'
                    : (stored.state === 'failed' || stored.state === 'unknown' ? stored.state : 'unknown'),
                clientOperationId: stored.operationId,
                content: { text: stored.caption || stored.text || '', attachments: attachments }
            };
            pendingUploads.set(localId, {
                localId: localId,
                operationId: stored.operationId,
                chatId: stored.chatId,
                createdAt: stored.createdAt || Date.now(),
                text: stored.text || '',
                caption: stored.caption || '',
                replyToMessageId: stored.replyToMessageId || 0,
                draftSnapshot: { generation: stored.generation || 0 },
                localMessage: localMessage,
                fileIds: (stored.fileIds || []).slice(),
                uploads: uploads,
                runtimeFiles: null,
                previewUrls: [],
                abortController: null,
                retrying: false,
                settled: false
            });
        });
    }

    // Страница ленты: обычный чат или приватный (там батч приходит зашифрованным).
    function loadMessagesPage(chatId, fromMessageId, offsetBefore, offsetAfter) {
        if (currentChatType !== 1) return BF.api.listMessages(chatId, fromMessageId, offsetBefore, offsetAfter);
        return BF.api.listPrivateMessages(chatId, fromMessageId, offsetBefore, offsetAfter).then(function (d) {
            return decryptPrivateBatch(chatId, d && d.messages).then(function (mapped) {
                mapped.sort(function (a, b) { return a.id - b.id; });
                return { messages: mapped };
            });
        });
    }

    // Подгрузка новых сообщений, когда скользящее окно обрезало хвост ленты.
    function loadNewerMessages() {
        if (!hasNewerGap || isLoadingNewer || isJumpingToTail || isJumpingToMessage || !currentChatId || messages.length === 0) return;
        isLoadingNewer = true;
        var pagedChatId = currentChatId;
        var newestId = messages[messages.length - 1].id || 0;

        loadMessagesPage(pagedChatId, newestId, 0, 30).then(function (data) {
            if (pagedChatId !== currentChatId) return;
            var fetched = (data && data.messages) || [];
            var fresh = fetched.filter(function (m) { return !messages.some(function (em) { return em.id === m.id; }); });
            // `api.js` подменяет offsetBefore=0 на 30, поэтому в ответе всегда есть уже
            // загруженные сообщения: конец чата определяем по числу действительно новых.
            if (fresh.length < 30) hasNewerGap = false;
            if (fresh.length === 0) return;

            var chain = Promise.resolve();
            fresh.forEach(function (msg) {
                chain = chain.then(function () {
                    messages.push(msg);
                    // resync-пропуск мог прийти именно этой страницей (окно было в
                    // середине истории) — перед якорным сообщением ставим разделитель.
                    var sepKey = resyncSeparatorId && Number(msg.id) === Number(resyncSeparatorId) ? 'chat.newMessages' : null;
                    return appendMessageElement(msg, sepKey);
                });
            });
            return chain.then(function () { trimMessages('head'); });
        }).finally(function () { isLoadingNewer = false; });
    }

    // Lazy-load older messages
    messagesArea.addEventListener('scroll', function () {
        if (messagesArea.scrollTop < 100 && !isLoadingOlder && !isJumpingToMessage && !noMoreOlder && currentChatId && messages.length > 0) {
            isLoadingOlder = true;
            loadingMessages.classList.add('visible');
            var oldestId = messages[0].id || 0;
            var prevHeight = messagesArea.scrollHeight;
            var pagedChatId = currentChatId;

            loadMessagesPage(pagedChatId, oldestId, 30, 0).then(function (data) {
                if (pagedChatId !== currentChatId) return;
                if (data && data.messages && data.messages.length > 0) {
                    var newMsgs = data.messages.filter(function (m) { return !messages.some(function (em) { return em.id === m.id; }); });
                    if (newMsgs.length === 0) { noMoreOlder = true; }
                    else {
                        messages = newMsgs.concat(messages);
                        return prependMessages(newMsgs).then(function () {
                            messagesArea.scrollTop = messagesArea.scrollHeight - prevHeight;
                            trimMessages('tail');
                        });
                    }
                } else { noMoreOlder = true; }
            }).finally(function () {
                loadingMessages.classList.remove('visible');
                isLoadingOlder = false;
            });
        }
    });

    // ========== SEND MESSAGE ==========

    function createPendingSend(files, asDocuments, text, caption) {
        var operationId = newOperationId();
        var localId = 'pending-send-' + operationId;
        var previewUrls = [];
        var uploads = (files || []).map(function (file, index) {
            var uploadType = BF.files.getUploadFileType(file.type, asDocuments, file.name);
            return {
                index: index,
                operationId: newOperationId(),
                reservedFileId: '',
                resultFileId: '',
                name: file.name,
                size: file.size,
                type: file.type,
                uploadType: uploadType,
                state: 'pending'
            };
        });
        var localAttachments = uploads.map(function (upload) {
            var file = files[upload.index];
            var isImage = !asDocuments && (upload.uploadType === IMAGE_UPLOAD_TYPE || upload.uploadType === GIF_UPLOAD_TYPE);
            var previewUrl = isImage ? URL.createObjectURL(file) : '';
            if (previewUrl) previewUrls.push(previewUrl);
            return {
                type: isImage ? (upload.uploadType === GIF_UPLOAD_TYPE ? 'GIF' : 'IMAGE') : 'DOCUMENT',
                fileId: '',
                fileName: upload.name,
                attachmentSize: upload.size,
                localPreviewUrl: previewUrl,
                uploadProgress: 0,
                uploadIndex: upload.index,
                isPending: true
            };
        });
        var draftSnapshot = BF.drafts ? BF.drafts.snapshot(currentChatId) : null;
        var localMessage = {
            id: localId,
            senderId: myUserId,
            readBy: [],
            sentAt: Date.now(),
            type: GENERIC_MESSAGE_TYPE,
            isPending: true,
            pendingState: uploads.length ? 'uploading' : 'sending',
            clientOperationId: operationId,
            content: { text: caption || text || '', attachments: localAttachments }
        };
        return {
            localId: localId,
            operationId: operationId,
            chatId: currentChatId,
            createdAt: localMessage.sentAt,
            text: text || '',
            caption: caption || '',
            replyToMessageId: pendingReply ? pendingReply.messageId : 0,
            draftSnapshot: draftSnapshot,
            localMessage: localMessage,
            fileIds: [],
            uploads: uploads,
            runtimeFiles: files || [],
            previewUrls: previewUrls,
            abortController: null,
            retrying: false,
            settled: false
        };
    }

    function showPendingSend(entry) {
        pendingUploads.set(entry.localId, entry);
        if (String(entry.chatId) === String(currentChatId)) {
            messages.push(entry.localMessage);
            appendMessageToView(entry.localMessage).then(scrollToBottom);
        }
    }

    function clearComposerForPending() {
        messageInput.value = '';
        messageInput.style.height = 'auto';
        clearPendingReply();
    }

    function dispatchPendingSend(entry) {
        if (entry.settled) return Promise.resolve();
        setPendingState(entry, 'sending');
        return BF.api.sendMessage({
            chatId: entry.chatId,
            text: entry.caption || entry.text || null,
            fileIds: entry.fileIds.length ? entry.fileIds : null,
            replyToMessageId: entry.replyToMessageId,
            clientOperationId: entry.operationId
        }).then(function (resp) {
            if (!resp || !resp.message) throw new Error('send_invalid_response');
            var msg = resp.message;
            reconcilePendingUpload(entry.chatId, msg, entry);
            var chatIdx = chats.findIndex(function (chat) { return chat.id === entry.chatId; });
            if (chatIdx >= 0) {
                var chat = chats[chatIdx];
                chat.lastMessage = msg;
                chats.splice(chatIdx, 1);
                chats.unshift(chat);
                renderChatList();
            }
            BF.sound.play('tick');
        }).catch(function (error) {
            if (!entry.settled) setPendingState(entry, error && error.outcomeUnknown ? 'unknown' : 'failed');
        }).finally(function () {
            sendBtn.disabled = false;
        });
    }

    function uploadPendingFiles(entry, retry) {
        if (entry.settled) return Promise.resolve();
        entry.abortController = new AbortController();
        setPendingState(entry, 'uploading');

        var chain = Promise.resolve();
        entry.uploads.forEach(function (upload, index) {
            chain = chain.then(function () {
                if (upload.state === 'completed' && upload.resultFileId) return;
                var file = entry.runtimeFiles && entry.runtimeFiles[index];
                if (!file) {
                    var missing = new Error('upload_file_missing');
                    missing.kind = 'file-missing';
                    throw missing;
                }
                var progress = function (percent) {
                    entry.localMessage.content.attachments[index].uploadProgress = percent;
                    BF.messages.updateAttachmentProgress(entry.localId, index, percent);
                };
                var options = {
                    operationId: upload.operationId,
                    signal: entry.abortController.signal,
                    onReserved: function (fileId) {
                        upload.reservedFileId = fileId;
                        upload.state = 'processing';
                        persistPendingEntry(entry);
                    }
                };
                var request = retry
                    ? BF.files.retryUpload(file, upload.uploadType, upload, progress, options)
                    : BF.files.uploadFile(file, upload.uploadType, progress, options);
                return request.then(function (fileId) {
                    upload.resultFileId = fileId;
                    upload.state = 'completed';
                    entry.localMessage.content.attachments[index].fileId = fileId;
                    if (entry.fileIds.indexOf(fileId) < 0) entry.fileIds.push(fileId);
                    persistPendingEntry(entry);
                });
            });
        });

        return chain.then(function () {
            entry.abortController = null;
            return dispatchPendingSend(entry);
        }).catch(function (error) {
            entry.abortController = null;
            if (entry.settled) return;
            if (error && error.kind === 'file-missing') setPendingState(entry, 'waiting-file');
            else if (error && (error.state === 'processing' || error.message === 'upload_processing')) setPendingState(entry, 'processing');
            else setPendingState(entry, error && error.outcomeUnknown ? 'unknown' : 'failed');
            sendBtn.disabled = false;
        });
    }

    function runPendingSend(entry, retry) {
        sendBtn.disabled = true;
        return entry.uploads.length ? uploadPendingFiles(entry, retry) : dispatchPendingSend(entry);
    }

    function restorePendingComposer(entry) {
        if (String(entry.chatId) !== String(currentChatId)) return;
        messageInput.value = entry.caption || entry.text || '';
        messageInput.style.height = 'auto';
        messageInput.style.height = Math.min(messageInput.scrollHeight, 120) + 'px';
        if (entry.replyToMessageId) {
            var reply = messages.find(function (message) {
                return Number(message.id) === Number(entry.replyToMessageId);
            });
            if (reply) {
                setPendingReply(reply, false);
            } else {
                pendingReply = {
                    messageId: entry.replyToMessageId,
                    authorName: '',
                    previewText: ''
                };
                renderReplyPreview();
                BF.api.listMessages(entry.chatId, entry.replyToMessageId, 1, 1).then(function (data) {
                    if (!pendingReply || Number(pendingReply.messageId) !== Number(entry.replyToMessageId)) return;
                    var loaded = data && data.messages && data.messages.find(function (message) {
                        return Number(message.id) === Number(entry.replyToMessageId);
                    });
                    if (loaded) setPendingReply(loaded, false);
                }).catch(function () {});
            }
        }
        saveCurrentDraft();
        messageInput.focus();
    }

    function cancelPendingSend(localId) {
        var entry = pendingUploads.get(localId);
        if (!entry || entry.settled || entry.localMessage.pendingState !== 'uploading') return;
        if (entry.abortController) entry.abortController.abort();
        restorePendingComposer(entry);
        removePendingUpload(entry);
        sendBtn.disabled = false;
    }

    function retryPendingSend(localId) {
        var entry = pendingUploads.get(localId);
        if (!entry || entry.settled || entry.retrying) return;
        var hasIncompleteUpload = entry.uploads.some(function (upload) { return upload.state !== 'completed'; });
        if (hasIncompleteUpload && (!entry.runtimeFiles || entry.runtimeFiles.length === 0)) {
            entry.retrying = true;
            var needsFile = false;
            var stillProcessing = false;
            var checks = entry.uploads.reduce(function (chain, upload, index) {
                return chain.then(function () {
                    if (upload.state === 'completed' && upload.resultFileId) return;
                    if (!upload.reservedFileId) {
                        needsFile = true;
                        return;
                    }
                    return BF.files.getUploadStatus(upload.reservedFileId).then(function (status) {
                        if (status.state === 'completed') {
                            upload.resultFileId = status.fileId;
                            upload.state = 'completed';
                            entry.localMessage.content.attachments[index].fileId = status.fileId;
                            if (entry.fileIds.indexOf(status.fileId) < 0) entry.fileIds.push(status.fileId);
                        } else if (status.state === 'processing') {
                            stillProcessing = true;
                        } else {
                            needsFile = true;
                        }
                    });
                });
            }, Promise.resolve());
            checks.then(function () {
                if (entry.settled) return;
                persistPendingEntry(entry);
                if (stillProcessing) {
                    setPendingState(entry, 'processing');
                    return;
                }
                if (needsFile) {
                    setPendingState(entry, 'waiting-file');
                    pendingFileSelectionEntry = entry;
                    if (fileInput) {
                        fileInput.click();
                    }
                    return;
                }
                return runPendingSend(entry, true);
            }).catch(function (error) {
                if (!entry.settled) setPendingState(entry, error && error.outcomeUnknown ? 'unknown' : 'failed');
            }).finally(function () {
                entry.retrying = false;
            });
            return;
        }
        entry.retrying = true;
        runPendingSend(entry, true).finally(function () { entry.retrying = false; });
    }

    function sendMessage() {
        var text = messageInput.value.trim();
        if (!currentChatId) return;
        stopTypingSend(true);

        if (currentChatType === 1) {
            if (text) sendPrivateMessageFlow(text);
            return;
        }

        if (pendingEdit) {
            var editId = pendingEdit.messageId;
            var origMsg = messages.find(function (m) { return m.id === editId; });
            var keepFileIds = [];
            if (origMsg && origMsg.content && origMsg.content.attachments) {
                origMsg.content.attachments.forEach(function (a) {
                    var t = a.type;
                    if (t === 'FORWARDED_MESSAGE' || t === 8 || t === '8') return;
                    if (a.fileId) keepFileIds.push(a.fileId);
                });
            }
            if (!text && keepFileIds.length === 0) return; // нечего сохранять
            sendBtn.disabled = true;
            BF.api.editMessage(editId, text, keepFileIds).then(function (resp) {
                sendBtn.disabled = false;
                if (resp && resp.message) applyMessageEdit(currentChatId, resp.message);
                clearPendingEdit();
            }).catch(function () { sendBtn.disabled = false; });
            return;
        }

        if (!text) return;

        var entry = createPendingSend([], false, text, '');
        if (!persistPendingEntry(entry)) {
            groupToast(BF.i18n.t('error.pendingStorage'));
            return;
        }
        clearComposerForPending();
        showPendingSend(entry);
        runPendingSend(entry, false);
    }

    function sendMessageWithFiles(files, asDocuments, caption) {
        if (pendingEdit) {
            // Во время редактирования attach-flow заблокирован, чтобы не отправить новое сообщение
            // вместо правки исходного. Завершите или отмените редактирование.
            return;
        }
        stopTypingSend(true);
        var text = (caption != null ? caption : messageInput.value).trim();
        var entry = createPendingSend(files, asDocuments, '', text);
        if (!persistPendingEntry(entry)) {
            releasePendingPreviews(entry);
            groupToast(BF.i18n.t('error.pendingStorage'));
            return;
        }
        clearComposerForPending();
        showPendingSend(entry);
        runPendingSend(entry, false);
    }

    function openAttachModal(files) {
        if (!currentChatId || currentChatType === 1) return; // в приватных чатах вложения не поддерживаются
        var prefill = messageInput.value;
        BF.attach.open(files, function (outFiles, asDocuments, caption) {
            // Если пользователь ввёл подпись в модалке — забираем её из неё, а исходный
            // ввод в чате очищаем, чтобы текст не отправился ещё раз отдельным сообщением.
            sendMessageWithFiles(outFiles, asDocuments, caption);
        }, prefill);
    }

    sendBtn.addEventListener('click', sendMessage);
    messageInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); }
    });
    messageInput.addEventListener('input', function () {
        messageInput.style.height = 'auto';
        messageInput.style.height = Math.min(messageInput.scrollHeight, 120) + 'px';
        saveCurrentDraft();

        if (!currentChatId || currentChatType !== 0) return;
        var value = messageInput.value;
        if (value.trim() === '') {
            stopTypingSend(true);
            return;
        }
        typingLastInputAt = Date.now();
        if (!typingSendActive) {
            typingSendActive = true;
            BF.api.setTypingStatus(currentChatId, true).catch(function () {});
            typingSendTimer = setInterval(function () {
                if (Date.now() - typingLastInputAt >= 5000) {
                    stopTypingSend(false);
                } else {
                    BF.api.setTypingStatus(currentChatId, true).catch(function () {});
                }
            }, 4000);
        }
    });

    // ========== FILE UPLOAD ==========

    attachBtn.addEventListener('click', function () {
        pendingFileSelectionEntry = null;
        fileInput.click();
    });

    fileInput.addEventListener('change', function () {
        var files = Array.from(fileInput.files);
        fileInput.value = '';
        var retryEntry = pendingFileSelectionEntry;
        pendingFileSelectionEntry = null;
        if (files.length === 0) return;

        if (retryEntry && !retryEntry.settled) {
            var incomplete = retryEntry.uploads.filter(function (upload) {
                return upload.state !== 'completed';
            });
            var matches = files.length === incomplete.length && files.every(function (file, index) {
                return BF.files.matchesPendingUpload(incomplete[index], file);
            });
            if (!matches) {
                groupToast(BF.i18n.t('error.uploadAttachment'));
                setPendingState(retryEntry, 'waiting-file');
                return;
            }

            retryEntry.runtimeFiles = new Array(retryEntry.uploads.length);
            incomplete.forEach(function (upload, index) {
                var file = files[index];
                retryEntry.runtimeFiles[upload.index] = file;
                var attachment = retryEntry.localMessage.content.attachments[upload.index];
                if (attachment && (upload.uploadType === IMAGE_UPLOAD_TYPE || upload.uploadType === GIF_UPLOAD_TYPE)) {
                    var previewUrl = URL.createObjectURL(file);
                    retryEntry.previewUrls.push(previewUrl);
                    attachment.localPreviewUrl = previewUrl;
                }
            });
            refreshPendingElement(retryEntry);
            retryEntry.retrying = true;
            runPendingSend(retryEntry, true).finally(function () { retryEntry.retrying = false; });
            return;
        }
        openAttachModal(files);
    });

    messageInput.addEventListener('paste', function (e) {
        if (!currentChatId) return;
        var items = e.clipboardData && e.clipboardData.items;
        if (!items) return;
        var files = [];
        for (var i = 0; i < items.length; i++) {
            if (items[i].kind === 'file') {
                var f = items[i].getAsFile();
                if (f) files.push(f);
            }
        }
        if (files.length === 0) return;
        e.preventDefault();
        openAttachModal(files);
    });

    // Глобально блокируем дефолтное открытие файла в браузере при промахе мимо chat-area
    ['dragover', 'drop'].forEach(function (ev) {
        window.addEventListener(ev, function (e) { e.preventDefault(); });
    });

    var chatArea = document.querySelector('.chat-area');
    if (chatArea) {
        var dropOverlay = document.createElement('div');
        dropOverlay.className = 'drop-overlay';
        dropOverlay.textContent = BF.i18n.t('attach.dropHint');
        dropOverlay.setAttribute('aria-hidden', 'true');
        chatArea.appendChild(dropOverlay);

        var dragCounter = 0;
        function isFileDrag(e) {
            return e.dataTransfer && Array.from(e.dataTransfer.types || []).includes('Files');
        }
        chatArea.addEventListener('dragenter', function (e) {
            if (!currentChatId || currentChatType === 1 || !isFileDrag(e)) return;
            dragCounter++;
            chatArea.classList.add('drag-over');
        });
        chatArea.addEventListener('dragover', function (e) {
            if (!currentChatId || !isFileDrag(e)) return;
            e.dataTransfer.dropEffect = 'copy';
        });
        chatArea.addEventListener('dragleave', function () {
            dragCounter--;
            if (dragCounter <= 0) { dragCounter = 0; chatArea.classList.remove('drag-over'); }
        });
        chatArea.addEventListener('drop', function (e) {
            if (!currentChatId) return;
            dragCounter = 0;
            chatArea.classList.remove('drag-over');
            var files = Array.from(e.dataTransfer.files || []);
            if (files.length > 0) openAttachModal(files);
        });
    }

    // ========== MARK AS READ ==========

    function scheduleMarkRead() {
        if (markReadTimer) clearTimeout(markReadTimer);
        markReadTimer = setTimeout(flushMarkRead, 1000);
        if (!currentChatId) return;
        messages.forEach(function (msg) {
            if (msg.senderId !== myUserId && !(msg.readBy || []).includes(myUserId)) markReadPending.add(msg.id);
        });
    }

    function flushMarkRead() {
        if (markReadPending.size === 0) return;
        var ids = Array.from(markReadPending);
        markReadPending.clear();
        BF.api.markAsRead(ids).catch(function () {
            showToast(BF.i18n.t('error.markRead'), true);
        });
    }

    // ========== TITLE UNREAD BADGE ==========

    // null → название приложения из словаря (пересчитывается, т.к. зависит от языка)
    var baseTitle = null;

    function defaultBaseTitle() {
        return BF.i18n.t('app.title');
    }

    var faviconEl = document.getElementById('favicon');
    var defaultFaviconHref = faviconEl ? faviconEl.getAttribute('href') : '/favicon.ico';
    var faviconRequestId = 0;

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

    function updateTitleBadge() {
        var total = 0;
        chats.forEach(function (c) { total += (c.countUnread || 0); });
        var base = baseTitle || defaultBaseTitle();
        document.title = total > 0 ? '(' + (total > 99 ? '99+' : total) + ') ' + base : base;
    }

    // ========== BROWSER NOTIFICATIONS ==========

    function showNewMessageNotification(chatTitle, msg) {
        if (!('Notification' in window) || Notification.permission !== 'granted') return;
        if (document.visibilityState !== 'visible') return;
        if (document.visibilityState === 'visible' && msg.chatId === currentChatId) return;

        var body = '';
        if (msg.content && msg.content.text) body = u.truncate(msg.content.text, 80);
        else if (msg.content && msg.content.attachments && msg.content.attachments.length > 0) {
            body = u.attachmentEmoji(msg.content.attachments[0].type);
        }

        try {
            var n = new Notification(chatTitle || BF.i18n.t('notification.newMessage'), {
                body: body,
                tag: 'bf-msg-' + (msg.id || Date.now()),
                renotify: true
            });
            n.onclick = function () { window.focus(); n.close(); };
            setTimeout(function () { n.close(); }, 5000);
        } catch (e) { /* ignore mobile/permission errors */ }
    }

    // ========== CONNECTION STATUS ==========

    var connectionBanner = $('#connectionBanner');
    var connectionBannerText = $('#connectionBannerText');
    var connectionRetryButton = $('#connectionRetryButton');
    var connectionSynced = $('#connectionSynced');
    var _connectionHadProblem = false;
    var _connectionCatchUpRunning = false;
    var _connectionSyncedTimer = null;
    var _connectionStatusInitialized = false;
    var _connectionStatusEpoch = 0;
    var _connectionState = 'reconnecting';

    function hideConnectionSynced() {
        if (_connectionSyncedTimer) {
            clearTimeout(_connectionSyncedTimer);
            _connectionSyncedTimer = null;
        }
        if (connectionSynced) connectionSynced.classList.remove('visible');
    }

    function showConnectionSynced() {
        if (!currentChatId || !connectionSynced) return;
        hideConnectionSynced();
        connectionSynced.classList.add('visible');
        _connectionSyncedTimer = setTimeout(hideConnectionSynced, 3000);
    }

    function showConnectionProblem(state) {
        if (!connectionBanner) return;
        var offline = state === 'offline';
        connectionBanner.classList.toggle('visible', state !== 'connected');
        connectionBanner.classList.toggle('offline', offline);
        if (connectionBannerText) {
            connectionBannerText.textContent = BF.i18n.t(offline ? 'connection.offline' : 'connection.reconnecting');
        }
        if (connectionRetryButton) connectionRetryButton.disabled = false;
    }

    function runConnectionCatchUp() {
        if (_connectionCatchUpRunning) return;
        _connectionCatchUpRunning = true;
        var retryAfterCatchUp = false;
        var statusEpoch = _connectionStatusEpoch;
        Promise.all([refreshChatListQuiet(), resyncCurrentChatTail()]).then(function (results) {
            if (statusEpoch !== _connectionStatusEpoch || _connectionState !== 'connected') return;
            if (!results[0] || !results[1]) {
                retryAfterCatchUp = true;
                return;
            }
            _connectionHadProblem = false;
            showConnectionSynced();
        }).finally(function () {
            _connectionCatchUpRunning = false;
            if (retryAfterCatchUp && statusEpoch === _connectionStatusEpoch && _connectionState === 'connected') {
                BF.realtime.reconnect();
                return;
            }
            if (statusEpoch !== _connectionStatusEpoch && _connectionState === 'connected' && _connectionHadProblem) {
                runConnectionCatchUp();
            }
        });
    }

    BF.realtime.on('connection_status', function (data) {
        var state = data && data.state ? data.state : (data && data.connected ? 'connected' : 'reconnecting');
        var initialStatus = !_connectionStatusInitialized;
        _connectionStatusInitialized = true;
        _connectionStatusEpoch++;
        _connectionState = state;
        showConnectionProblem(state);
        if (state !== 'connected') {
            if (!initialStatus) _connectionHadProblem = true;
            hideConnectionSynced();
            return;
        }
        if (_connectionHadProblem) runConnectionCatchUp();
    });

    if (connectionRetryButton) {
        connectionRetryButton.addEventListener('click', function () {
            if (BF.realtime.reconnect()) connectionRetryButton.disabled = true;
        });
    }

    // ── RESYNC: реконнект отдельного стрима ──────────────────────────────────
    // realtime.js шлёт 'resync' при ЛЮБОМ переоткрытии стрима (backoff/watchdog/
    // age-timer/visibility). Это ловит случай, когда отвалился только поток новых
    // сообщений, а остальные живы — тогда connection_status (OR по 4 стримам) не
    // флипается и обычный catch-up не срабатывает. За время разрыва server-streaming
    // не реплеит пропущенное → сообщение видно лишь после ручного переоткрытия чата.
    //
    // Дебаунсим: при churn'е все 4 стрима реконнектятся почти одновременно — склеиваем
    // в один проход. Дозагрузка тихая: ререндерим только если хвост реально изменился,
    // иначе (обычный случай — за окно реконнекта ничего не пропало) DOM не трогаем.
    var _resyncTimer = null;
    BF.realtime.on('resync', function () {
        if (_resyncTimer) return;
        _resyncTimer = setTimeout(function () {
            _resyncTimer = null;
            if (!currentChatId) {
                // Чат не открыт — обновлять нечего в области сообщений; освежаем сайдбар.
                refreshChatListQuiet();
                return;
            }
            // Тихо сверяем хвост открытого чата. Сайдбар трогаем только если что-то
            // реально пропустили (тогда вероятны пропуски и в других чатах), чтобы не
            // ререндерить список и не сбрасывать его прокрутку на каждом churn-цикле.
            resyncCurrentChatTail();
        }, 1200);
    });

    // Дифф свежезагруженного хвоста против показанного окна (по id, editedAt/тексту,
    // числу прочитавших и удалениям в диапазоне окна). null — различий нет.
    function diffFetchedTail(fetched) {
        var byId = new Map();
        messages.forEach(function (m) { byId.set(String(m.id), m); });
        var minId = Infinity, maxId = -Infinity;
        var edits = [], readUpdates = [], news = [];
        for (var i = 0; i < fetched.length; i++) {
            var f = fetched[i];
            var fid = Number(f.id);
            if (fid < minId) minId = fid;
            if (fid > maxId) maxId = fid;
            var cur = byId.get(String(f.id));
            if (!cur) { news.push(f); continue; }                     // новое сообщение
            var ct = (cur.content && cur.content.text) || '';
            var ft = (f.content && f.content.text) || '';
            if ((cur.editedAt || 0) !== (f.editedAt || 0) || ct !== ft) { edits.push(f); continue; } // правка
            if ((cur.readBy || []).length !== (f.readBy || []).length) readUpdates.push(f); // прочтение
        }
        // Удаление: есть наше сообщение в диапазоне окна, которого нет в свежей выборке.
        var fetchedIds = new Set(fetched.map(function (m) { return String(m.id); }));
        var deletes = [];
        for (var j = 0; j < messages.length; j++) {
            var mid = Number(messages[j].id);
            if (mid >= minId && mid <= maxId && !fetchedIds.has(String(messages[j].id))) deletes.push(messages[j].id);
        }
        if (deletes.length === 0 && edits.length === 0 && readUpdates.length === 0 && news.length === 0) return null;
        news.sort(function (a, b) { return Number(a.id) - Number(b.id); });
        return { deletes: deletes, edits: edits, readUpdates: readUpdates, news: news };
    }

    // Точечное обновление галочек прочтения — view-часть handleMessageRead,
    // без мутации счётчиков непрочитанного и без ререндера списка чатов.
    function applyReadByUpdate(fetchedMsg) {
        var msg = messages.find(function (m) { return String(m.id) === String(fetchedMsg.id); });
        if (!msg) return;
        msg.readBy = fetchedMsg.readBy || [];
        var el = messagesArea.querySelector('.msg-status[data-msg-id="' + fetchedMsg.id + '"]');
        if (el) {
            var rc = msg.readBy.filter(function (id) { return id !== myUserId; }).length;
            BF.messages.updateMessageStatus(el, rc > 0);
        }
    }

    function resyncCurrentChatTail() {
        if (!currentChatId) return Promise.resolve(true);
        if (isLoadingOlder || loadingMessages.classList.contains('visible')) return Promise.resolve(false);
        if (currentChatType === 1) return reloadCurrentPrivateChat();
        var chatId = currentChatId;
        return BF.api.getChatInfo(chatId).then(function (info) {
            if (chatId !== currentChatId) return { skipped: true };
            if (!info || info.error) return null;
            var fromId = info.firstUnreadMessageId || info.lastMessageId || 0;
            return BF.api.listMessages(chatId, fromId, 30, 10).then(function (data) {
                return { info: info, data: data };
            });
        }).then(function (res) {
            if (res && res.skipped) return true;
            if (!res || !res.data || !res.data.messages) return false;
            if (chatId !== currentChatId) return true;
            var fetched = res.data.messages;
            var diff = diffFetchedTail(fetched);
            if (!diff) return true; // за окно реконнекта ничего не пропало — DOM не трогаем
            currentChatInfo = res.info;
            var wasAtBottom = messagesArea.scrollHeight - messagesArea.scrollTop - messagesArea.clientHeight < 300;

            // Новые сообщения не в хвосте (окно прыгало через scrollToMessage, или своё
            // сообщение ушло раньше resync-дебаунса) — точечно не вставить, полный render.
            var numericMessageIds = messages
                .map(function (m) { return Number(m.id); })
                .filter(Number.isFinite);
            var maxCurId = numericMessageIds.length > 0
                ? Math.max.apply(null, numericMessageIds)
                : -Infinity;
            var tailOnly = numericMessageIds.length > 0 && diff.news.every(function (m) { return Number(m.id) > maxCurId; });
            // Якорь разделителя «Новые сообщения»: первое пропущенное. Не ставим, когда
            // пользователь был у нижнего края — там догруженное сразу помечается прочитанным.
            resyncSeparatorId = !wasAtBottom && diff.news.length > 0 ? diff.news[0].id : null;
            if (!tailOnly) {
                messages = fetched;
                mergePendingUploadsIntoMessages(chatId);
                hasNewerGap = false; // буфер заменён хвостом — обрезанного «вперёд» больше нет
                return renderMessages().then(function () {
                    if (wasAtBottom) scrollToBottom();
                    return refreshChatListQuiet();
                });
            }

            // Точечное применение диффа — как live-события, но без звуков/нотификаций.
            diff.deletes.forEach(function (id) { applyMessageDelete(chatId, id); });
            diff.edits.forEach(function (m) { applyMessageEdit(chatId, m); });
            diff.readUpdates.forEach(applyReadByUpdate);

            var chain = Promise.resolve();
            var firstNewsAppended = false;
            diff.news.forEach(function (m) {
                chain = chain.then(function () {
                    if (chatId !== currentChatId) return;
                    if (reconcilePendingUpload(chatId, m)) return;
                    messages.push(m);
                    // Перед первым дописанным — разделитель «Новые сообщения».
                    var sepKey = resyncSeparatorId && !firstNewsAppended ? 'chat.newMessages' : null;
                    firstNewsAppended = true;
                    return appendMessageToView(m, sepKey);
                });
            });
            return chain.then(function () {
                if (chatId !== currentChatId || diff.news.length === 0) return;
                if (wasAtBottom) {
                    scrollToBottom();
                    if (scrollToBottomBtn) scrollToBottomBtn.classList.remove('visible');
                    var anyIncoming = false;
                    diff.news.forEach(function (m) {
                        if (m.senderId !== myUserId) { markReadPending.add(m.id); anyIncoming = true; }
                    });
                    if (anyIncoming) {
                        if (markReadTimer) clearTimeout(markReadTimer);
                        markReadTimer = setTimeout(flushMarkRead, 500);
                    }
                } else {
                    if (scrollToBottomBtn) scrollToBottomBtn.classList.add('visible');
                }
            }).then(function () {
                // В открытом чате что-то пропустили → вероятно, пропуски есть и в других
                // чатах: тихо освежаем превью/счётчики непрочитанного в сайдбаре.
                return refreshChatListQuiet();
            });
        }).then(function (result) {
            return result !== false;
        }).catch(function () {
            return false;
        });
    }

    // ========== SCROLL-BASED MARK AS READ ==========

    function markVisibleMessagesAsRead() {
        if (!currentChatId || currentChatType === 1) return;
        var changed = false;
        var areaRect = messagesArea.getBoundingClientRect();

        messagesArea.querySelectorAll('.msg-bubble').forEach(function (el) {
            var msgId = Number(el.dataset.msgId);
            if (!msgId) return;
            var msg = messages.find(function (m) { return m.id === msgId; });
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
            markReadTimer = setTimeout(flushMarkRead, 500);
        }
    }

    var _markReadScrollTimer = null;
    messagesArea.addEventListener('scroll', function () {
        // Показываем/скрываем кнопку прокрутки вниз
        var distFromBottom = messagesArea.scrollHeight - messagesArea.scrollTop - messagesArea.clientHeight;
        if (scrollToBottomBtn) scrollToBottomBtn.classList.toggle('visible', distFromBottom > 300);
        if (distFromBottom < 100) loadNewerMessages();
        if (distFromBottom <= 300 && newMessagesBelowCount > 0) {
            newMessagesBelowCount = 0;
            updateScrollBadge();
        }

        // Разделитель «Новые сообщения» полностью ушёл выше видимой области —
        // пользователь его прошёл: убираем и элемент, и якорь.
        if (resyncSeparatorId) {
            var newMsgSep = messagesInner.querySelector('.msg-unread-separator[data-sep-key="chat.newMessages"]');
            if (newMsgSep && newMsgSep.getBoundingClientRect().bottom < messagesArea.getBoundingClientRect().top) {
                newMsgSep.remove();
                resyncSeparatorId = null;
            }
        }

        if (_markReadScrollTimer) return;
        _markReadScrollTimer = setTimeout(function () {
            _markReadScrollTimer = null;
            markVisibleMessagesAsRead();
        }, 300);
    });

    // ========== TAB VISIBILITY — REFRESH ON RETURN ==========

    BF.realtime.on('tab_visible', function () {
        // Тихий catch-up пропущенного за время скрытой вкладки: диффы вместо полного ререндера
        refreshChatListQuiet();
        resyncCurrentChatTail();
    });

    // ========== REALTIME HANDLERS ==========


    BF.realtime.on('new_message', function (data) {
        handleNewMessage(data.chatId, data.message);
    });

    BF.realtime.on('message_read', function (data) {
        handleMessageRead(data.chatId, data.messageId, data.readBy);
    });

    BF.realtime.on('online_status', function (data) {
        handleOnlineStatus(data.userId, data.status, data.lastSeen);
    });

    BF.realtime.on('message_edited', function (data) {
        applyMessageEdit(data.chatId, data.message);
    });

    BF.realtime.on('message_deleted', function (data) {
        applyMessageDelete(data.chatId, data.messageId);
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

    BF.realtime.on('typing', function (data) {
        if (!currentChatId || String(data.chatId).toLowerCase() !== String(currentChatId).toLowerCase()) return;
        if (data.userId === myUserId) return;
        var old = typingUsers.get(data.userId);
        if (old) clearTimeout(old);
        if (data.action === 2) {
            typingUsers.delete(data.userId);
        } else {
            typingUsers.set(data.userId, setTimeout(function () {
                typingUsers.delete(data.userId);
                renderTypingIndicator();
            }, 6000));
            if (currentChatInfo && currentChatInfo.isGroupChat) {
                getUser(data.userId).then(renderTypingIndicator);
            }
        }
        renderTypingIndicator();
    });

    function handleNewMessage(chatId, msg) {
        var reconciledPending = reconcilePendingUpload(chatId, msg);
        if (!reconciledPending && chatId === currentChatId && messages.some(function (m) { return m.id === msg.id; })) return;

        var chatIdx = chats.findIndex(function (c) { return c.id === chatId; });
        var chatTitle = '';
        if (chatIdx >= 0) {
            var chat = chats[chatIdx];
            chatTitle = chat.title || '';
            chat.lastMessage = msg;
            if (chatId !== currentChatId && msg.senderId !== myUserId) {
                chat.countUnread = (chat.countUnread || 0) + 1;
            }
            chats.splice(chatIdx, 1);
            chats.unshift(chat);
            renderChatList();
        } else {
            // Unknown chat — reload the list to pick it up
            loadChats(true);
        }

        // Browser notification for messages from others
        if (msg.senderId !== myUserId) {
            BF.sound.play('chime');
            showNewMessageNotification(chatTitle, msg);
        }

        updateTitleBadge();

        if (chatId === currentChatId && !reconciledPending) {
            var isAtBottom = messagesArea.scrollHeight - messagesArea.scrollTop - messagesArea.clientHeight < 300;
            messages.push(msg);
            appendMessageToView(msg).then(function () {
                if (isAtBottom) {
                    scrollToBottom();
                    if (scrollToBottomBtn) scrollToBottomBtn.classList.remove('visible');
                } else {
                    if (scrollToBottomBtn) scrollToBottomBtn.classList.add('visible');
                    newMessagesBelowCount++;
                    updateScrollBadge();
                }
                // Auto-mark as read if message is visible (user is at bottom)
                if (isAtBottom && msg.senderId !== myUserId) {
                    markReadPending.add(msg.id);
                    if (markReadTimer) clearTimeout(markReadTimer);
                    markReadTimer = setTimeout(flushMarkRead, 500);
                }
            });
        }
    }

    function handleMessageRead(chatId, messageId, readBy) {
        // Update the message's readBy in the active chat view
        if (chatId === currentChatId) {
            var msg = messages.find(function (m) { return m.id === messageId; });
            if (msg) {
                msg.readBy = readBy;
                // Update check-mark indicator (single = delivered, double = read by others)
                var el = messagesArea.querySelector('.msg-status[data-msg-id="' + messageId + '"]');
                if (el) {
                    var rc = readBy.filter(function (id) { return id !== myUserId; }).length;
                    BF.messages.updateMessageStatus(el, rc > 0);
                }
            }
        }

        // Update unread count in chat list
        var chat = chats.find(function (c) { return c.id === chatId; });
        if (chat) {
            if (readBy.includes(myUserId)) {
                // We read a message — if this chat is open, all visible are read
                if (chatId === currentChatId) {
                    chat.countUnread = 0;
                } else {
                    chat.countUnread = Math.max(0, (chat.countUnread || 0) - 1);
                }
            }
            renderChatList();
            updateTitleBadge();
        }
    }

    function isUserOnline(userId) {
        var entry = onlineStatuses.get(userId);
        return entry ? BF.utils.isStatusOnline(entry.status) : false;
    }

    function handleOnlineStatus(userId, status, lastSeen) {
        onlineStatuses.set(userId, { status: status, lastSeen: lastSeen });
        var online = BF.utils.isStatusOnline(status);
        document.querySelectorAll('.online-dot[data-online-user="' + userId + '"]').forEach(function (dot) {
            dot.classList.toggle('visible', online);
        });
        if (currentChatInfo && !currentChatInfo.isGroupChat && !currentChatPeerIsBot) {
            var peerId = (currentChatInfo.membersId || []).find(function (id) { return id !== myUserId; });
            if (peerId === userId) updateChatHeaderOnline(userId);
        }
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

    function renderTypingIndicator() {
        if (!currentChatId || !currentChatInfo) return;

        if (typingUsers.size === 0) {
            if (currentChatInfo.isGroupChat) {
                chatHeaderStatus.textContent = BF.i18n.tp('group.memberCount', currentChatInfo.membersId ? currentChatInfo.membersId.length : 0);
                chatHeaderStatus.classList.remove('online');
            } else {
                var peerId = (currentChatInfo.membersId || []).find(function (id) { return id !== myUserId; });
                if (peerId) updateChatHeaderOnline(peerId);
            }
            return;
        }

        if (currentChatInfo.isGroupChat) {
            var names = Array.from(typingUsers.keys()).slice(0, 3).map(function (id) {
                var user = userCache.get(id);
                if (!user) return BF.i18n.t('common.someone');
                return (user.firstName || '').split(' ')[0] || user.username || BF.i18n.t('common.someone');
            });
            chatHeaderStatus.textContent = BF.i18n.t(
                typingUsers.size > 1 ? 'status.typing.many' : 'status.typing.named',
                { names: names.join(', ') });
        } else {
            chatHeaderStatus.textContent = BF.i18n.t('status.typing');
        }
    }

    function collectOnlineUserIds() {
        var ids = new Set();
        chats.forEach(function (chat) {
            if (!chat.isGroupChat && chat.members) {
                chat.members.forEach(function (m) {
                    var user = userCache.get(m.userId);
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

    function subscribeOnlineForUsers(userIds) {
        var changed = false;
        userIds.forEach(function (id) {
            var user = userCache.get(id);
            if (!(user && user.isBot) && !onlineSubscribedUserIds.has(id)) {
                onlineSubscribedUserIds.add(id);
                changed = true;
            }
        });
        if (changed) BF.realtime.changeOnlineSubscription(Array.from(onlineSubscribedUserIds));
    }

    // ========== SEARCH ==========

    var searchTimer = null;
    var searchToken = 0;
    searchInput.addEventListener('input', function () {
        clearTimeout(searchTimer);
        var query = searchInput.value.trim();
        var qLower = query.toLowerCase();
        if (!query) { searchResults.classList.remove('visible'); searchResults.innerHTML = ''; return; }

        // Локальный фильтр по уже загруженным чатам (синхронно, как в cmdpalette).
        var matchedChats = chats.filter(function (c) {
            return (c.title || '').toLowerCase().indexOf(qLower) >= 0;
        });

        function render(users) {
            searchResults.classList.add('visible');
            searchResults.innerHTML = '';
            if (matchedChats.length === 0 && (!users || users.length === 0)) {
                searchResults.innerHTML = '<div style="padding:16px;text-align:center;color:var(--text-sub);font-size:14px;">' + u.escapeHtml(BF.i18n.t('common.nothingFound')) + '</div>';
                return;
            }
            matchedChats.forEach(function (chat) {
                var el = document.createElement('div');
                el.className = 'search-result-item';
                var initial = (chat.title || '?')[0].toUpperCase();
                var avHtml = chat.picture
                    ? '<img src="' + u.escapeHtml(chat.picture) + '" alt="">'
                    : initial;
                el.innerHTML = '<div class="chat-avatar">' + avHtml + '</div>' +
                    '<div class="search-result-info"><div class="user-name">' + u.escapeHtml(chat.title || BF.i18n.t('common.chat')) + '</div></div>';
                el.addEventListener('click', function () {
                    searchInput.value = '';
                    searchResults.classList.remove('visible');
                    searchResults.innerHTML = '';
                    openChat(chat.id);
                });
                searchResults.appendChild(el);
            });
            if (users) {
                users.forEach(function (user) {
                    var el = document.createElement('div');
                    el.className = 'search-result-item';
                    var initial = (user.firstName || user.username || '?')[0].toUpperCase();
                    var avHtml = user.profilePicturePreview
                        ? '<img src="' + u.escapeHtml(user.profilePicturePreview) + '" alt="">'
                        : initial;
                    el.innerHTML = '<div class="chat-avatar">' + avHtml + '</div>' +
                        '<div class="search-result-info"><div class="user-name">' + u.escapeHtml(((user.firstName || '') + ' ' + (user.lastName || '')).trim() || user.username) + '</div>' +
                        '<div class="user-username">@' + u.escapeHtml(user.username || '') + '</div></div>';
                    el.addEventListener('click', function () {
                        searchInput.value = '';
                        searchResults.classList.remove('visible');
                        searchResults.innerHTML = '';
                        BF.api.getPersonChatId(user.id).then(function (d) {
                            if (d && d.chatId) openChat(d.chatId);
                        });
                    });
                    searchResults.appendChild(el);
                });
            }
        }

        render(null);

        var token = ++searchToken;
        searchTimer = setTimeout(function () {
            BF.api.searchUsers(query, 0, 20).then(function (data) {
                if (token !== searchToken) return;
                render(data && data.users ? data.users : []);
            });
        }, 300);
    });

    // ========== PROFILE OVERLAY ==========

    function openProfile(userId) {
        if (!userId) return;
        currentProfileUserId = userId;
        if (profilePoster) BF.files.loadResilientBackground(profilePoster, null, false);

        BF.api.getUser(userId).then(function (d) {
            if (currentProfileUserId !== userId) return;
            if (!d || !d.user) return;
            var user = d.user;

            if (profilePoster) {
                BF.files.loadResilientBackground(profilePoster, user.profilePosterFileId, false);
            }

            var initial = (user.firstName || user.username || '?')[0].toUpperCase();
            if (user.profilePicture) {
                var avImg = document.createElement('img');
                avImg.src = user.profilePicture;
                avImg.alt = '';
                profileAvatar.replaceChildren(avImg);
            } else {
                profileAvatar.textContent = initial;
            }
            profileName.textContent = [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username;
            if (user.isBot) profileName.insertAdjacentHTML('beforeend', botBadgeMarkup());
            profileUsername.textContent = user.username ? '@' + user.username : '';
            profileBio.textContent = user.bio || '';
            profileBio.style.display = user.bio ? 'block' : 'none';

            var online = isUserOnline(userId);
            var entry = onlineStatuses.get(userId);
            profileStatus.textContent = online ? BF.i18n.t('status.online') : BF.utils.formatLastSeen(entry ? entry.lastSeen : null);
            profileStatus.className = 'profile-status-line' + (online ? ' online' : '');
            profileStatus.hidden = !!user.isBot;
            setProfileCallButtonsVisible(!user.isBot);

            var _profileUserId = $('#profileUserId');
            var _profileChatId = $('#profileChatId');
            if (_profileUserId) _profileUserId.textContent = user.id;
            if (_profileChatId) _profileChatId.textContent = currentChatId || '\u2014';

            if (user.registrationDate) {
                profileRegDate.textContent = new Date(user.registrationDate).toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' });
            } else { profileRegDate.textContent = '\u2014'; }

            profileBadges.innerHTML = '';
            if (user.badges && user.badges.length > 0) {
                user.badges.forEach(function (b) {
                    var el = document.createElement('div');
                    el.className = 'profile-badge';
                    if (b.imageUrl) {
                        var bImg = document.createElement('img');
                        bImg.src = b.imageUrl;
                        bImg.alt = '';
                        el.appendChild(bImg);
                    }
                    el.appendChild(document.createTextNode(b.name || ''));
                    profileBadges.appendChild(el);
                });
            }

            loadProfileMedia('media');
            BF.utils.openOverlay(profileOverlay);
        });
    }

    var MEDIA_TAB_TYPES = ['media', 'files', 'audio', 'voice'];

    function createMediaPanels(container) {
        var state = { chatId: null, requestIds: {}, panes: {}, contents: {}, fileSearch: null, searchTimer: null };
        container.replaceChildren();
        MEDIA_TAB_TYPES.forEach(function (type) {
            var pane = document.createElement('div');
            pane.className = 'profile-media-pane';
            pane.dataset.type = type;
            var content = document.createElement('div');
            if (type === 'files') {
                var input = document.createElement('input');
                input.className = 'profile-file-search';
                input.type = 'search';
                input.placeholder = BF.i18n.t('media.searchFiles.placeholder');
                input.maxLength = 255;
                input.autocomplete = 'off';
                state.fileSearch = input;
                pane.appendChild(input);
            }
            pane.appendChild(content);
            container.appendChild(pane);
            state.panes[type] = pane;
            state.contents[type] = content;
            state.requestIds[type] = 0;
        });
        state.panes.media.classList.add('active');
        state.fileSearch.addEventListener('input', function () {
            clearTimeout(state.searchTimer);
            state.searchTimer = setTimeout(function () { renderChatMedia('files', state); }, 300);
        });
        return state;
    }

    var profileMediaPanels = createMediaPanels(profileMediaContent);
    var groupMediaPanels = createMediaPanels(groupMediaContent);

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
        setMediaTabActive: setMediaTabActive,
        renderChatMedia: renderChatMedia,
        openChatBackgroundSelector: openChatBackgroundSelector
    });
    var openGroupInfo = BF.groupInfo.open;

    function setMediaTabActive(selector, panels, type) {
        document.querySelectorAll(selector).forEach(function (tab) { tab.classList.toggle('active', tab.dataset.type === type); });
        MEDIA_TAB_TYPES.forEach(function (tabType) { panels.panes[tabType].classList.toggle('active', tabType === type); });
    }

    function loadProfileMedia(type) {
        setMediaTabActive('#profileOverlay .profile-media-tab', profileMediaPanels, type);
        renderChatMedia(type, profileMediaPanels);
    }

    function isCurrentMediaRequest(panels, type, chatId, requestId) {
        return panels.chatId === chatId && currentChatId === chatId && panels.requestIds[type] === requestId;
    }

    function makeStaticGifPreview(fileUrl) {
        if (!fileUrl) return Promise.reject(new Error('GIF URL is missing'));
        return fetch(fileUrl).then(function (response) {
            if (!response.ok) throw new Error('GIF could not be loaded');
            return response.blob();
        }).then(function (blob) {
            return new Promise(function (resolve, reject) {
                var objectUrl = URL.createObjectURL(blob);
                var image = new Image();
                image.onload = function () {
                    URL.revokeObjectURL(objectUrl);
                    var maxSide = 1024;
                    var scale = Math.min(1, maxSide / Math.max(image.naturalWidth, image.naturalHeight));
                    var canvas = document.createElement('canvas');
                    canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
                    canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
                    canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
                    resolve(canvas.toDataURL('image/jpeg', 0.82));
                };
                image.onerror = function () { URL.revokeObjectURL(objectUrl); reject(new Error('GIF could not be decoded')); };
                image.src = objectUrl;
            });
        });
    }

    function getProfileMediaUrls(att) {
        return BF.files.getFileUrls([att.fileId]).then(function (urls) {
            var file = urls[0] || {};
            return {
                full: file.url || att.previewUrl || '',
                preview: file.previewUrl || att.previewUrl || ''
            };
        });
    }

    function renderChatMedia(type, panels) {
        if (!currentChatId) return;
        var chatId = currentChatId;
        if (panels.chatId !== chatId) {
            panels.chatId = chatId;
            MEDIA_TAB_TYPES.forEach(function (tabType) {
                panels.requestIds[tabType]++;
                panels.contents[tabType].replaceChildren();
            });
            panels.fileSearch.value = '';
        }

        var content = panels.contents[type];
        var requestId = ++panels.requestIds[type];
        content.replaceChildren();
        function current() { return isCurrentMediaRequest(panels, type, chatId, requestId); }
        function showEmpty(text) {
            if (current()) content.innerHTML = '<div class="profile-media-empty">' + text + '</div>';
        }

        if (type === 'media') {
            Promise.all([
                BF.api.listChatAttachments(chatId, 1, 0, 30),
                BF.api.listChatAttachments(chatId, 2, 0, 30),
                BF.api.listChatAttachments(chatId, 3, 0, 30)
            ]).then(function (results) {
                if (!current()) return;
                var all = (results[0].attachments || []).concat(results[1].attachments || [], results[2].attachments || []);
                all.sort(function (a, b) { return (b.sentAt || 0) - (a.sentAt || 0); });
                if (all.length === 0) { showEmpty(BF.i18n.t('media.empty.media')); return; }

                var grid = document.createElement('div');
                grid.className = 'profile-media-grid';
                content.appendChild(grid);
                var chain = Promise.resolve();
                all.forEach(function (item) {
                    chain = chain.then(function () {
                        var att = item.attachment;
                        if (!att || !att.fileId) return;
                        return getProfileMediaUrls(att).then(function (urls) {
                            var previewPromise = att.type === 'GIF' && !urls.preview
                                ? makeStaticGifPreview(urls.full)
                                : Promise.resolve(urls.preview || urls.full);
                            return previewPromise.then(function (preview) {
                                if (!current()) return;
                                var tile = document.createElement('button');
                                tile.type = 'button';
                                tile.className = 'profile-media-tile';
                                tile.setAttribute('aria-label', BF.i18n.t(att.type === 'VIDEO' ? 'media.openVideo' : 'media.openImage'));
                                if (preview) {
                                    var img = document.createElement('img');
                                    img.src = preview;
                                    img.loading = 'lazy';
                                    img.alt = '';
                                    BF.files.bindResilientMedia(img, att.fileId, true);
                                    tile.appendChild(img);
                                } else {
                                    var placeholder = document.createElement('span');
                                    placeholder.className = 'profile-media-placeholder';
                                    placeholder.textContent = BF.i18n.t('media.noPreview');
                                    tile.appendChild(placeholder);
                                }
                                if (att.type === 'VIDEO') {
                                    var play = document.createElement('span');
                                    play.className = 'profile-video-play';
                                    play.textContent = '▶';
                                    tile.appendChild(play);
                                }
                                tile.addEventListener('click', function () {
                                    // В profile/group панели GIF должен остаться неподвижным
                                    // и в просмотрщике; чат и его общий просмотрщик не меняем.
                                    if (att.type === 'GIF') {
                                        if (preview) showMediaOverlay('image', preview, null);
                                        return;
                                    }
                                    showMediaOverlay(att.type === 'VIDEO' ? 'video' : 'image', urls.full || preview, att.fileId);
                                });
                                grid.appendChild(tile);
                            });
                        }).catch(function () {
                            if (!current()) return;
                            var tile = document.createElement('div');
                            tile.className = 'profile-media-placeholder';
                            tile.textContent = BF.i18n.t('media.noPreview');
                            grid.appendChild(tile);
                        });
                    });
                });
            }).catch(function () { showEmpty(BF.i18n.t('media.error.media')); });
        } else if (type === 'files') {
            var query = panels.fileSearch.value.trim();
            BF.api.listChatAttachments(chatId, 4, 0, 30, query).then(function (data) {
                if (!current()) return;
                var files = data.attachments || [];
                if (files.length === 0) { showEmpty(BF.i18n.t(query ? 'media.notFound.files' : 'media.empty.files')); return; }
                var list = document.createElement('div');
                list.className = 'profile-file-list';
                content.appendChild(list);
                var chain = Promise.resolve();
                files.forEach(function (item) {
                    chain = chain.then(function () {
                        var att = item.attachment;
                        if (!att || !att.fileId) return;
                        return BF.files.getFileUrls([att.fileId]).then(function (urls) {
                            if (!current()) return;
                            var fileUrl = urls[0] ? urls[0].url : '#';
                            var el = document.createElement('a');
                            el.className = 'profile-file-item';
                            el.href = fileUrl; el.target = '_blank';
                            BF.files.bindResilientLink(el, att.fileId);
                            el.rel = 'noopener';
                            var icon = document.createElement('span');
                            icon.textContent = '\u{1F4C4}';
                            el.appendChild(icon);
                            el.appendChild(document.createTextNode(' ' + (att.fileName || BF.i18n.t('attachment.file'))));
                            list.appendChild(el);
                        });
                    });
                });
            }).catch(function () { showEmpty(BF.i18n.t('media.error.files')); });
        } else if (type === 'audio' || type === 'voice') {
            var attType = type === 'audio' ? 5 : 6;
            var emptyText = BF.i18n.t(type === 'audio' ? 'media.empty.audio' : 'media.empty.voice');
            BF.api.listChatAttachments(chatId, attType, 0, 30).then(function (data) {
                if (!current()) return;
                var items = data.attachments || [];
                if (items.length === 0) { showEmpty(emptyText); return; }
                var list = document.createElement('div');
                list.className = 'profile-file-list';
                content.appendChild(list);
                var chain = Promise.resolve();
                items.forEach(function (item) {
                    chain = chain.then(function () {
                        var att = item.attachment;
                        if (!att || !att.fileId) return;
                        return BF.files.getFileUrls([att.fileId]).then(function (urls) {
                            if (!current()) return;
                            var fileUrl = urls[0] ? urls[0].url : '';
                            if (!fileUrl) return;
                            var el = document.createElement('div');
                            el.className = 'profile-audio-item';
                            if (type === 'audio') {
                                var nm = document.createElement('div');
                                nm.className = 'profile-audio-name';
                                nm.textContent = att.fileName || BF.i18n.t('attachment.audio');
                                el.appendChild(nm);
                            }
                            var audio = document.createElement('audio');
                            audio.controls = true;
                            audio.preload = 'none';
                            audio.src = fileUrl;
                            el.appendChild(audio);
                            list.appendChild(el);
                        });
                    });
                });
            }).catch(function () { showEmpty(BF.i18n.t('media.error.attachments')); });
        }
    }

    document.querySelectorAll('#profileOverlay .profile-media-tab').forEach(function (tab) {
        tab.addEventListener('click', function () { loadProfileMedia(tab.dataset.type); });
    });

    function onChatHeaderClick() {
        if (!currentChatInfo) return;
        if (currentChatInfo.isGroupChat) { openGroupInfo(); return; }
        var peerId = (currentChatInfo.membersId || []).find(function (id) { return id !== myUserId; });
        if (peerId) openProfile(peerId);
    }
    chatHeaderAvatar.addEventListener('click', onChatHeaderClick);
    chatHeaderName.addEventListener('click', onChatHeaderClick);

    // --- Call buttons (шапка чата) ---
    var isInitiatingCall = false;
    function startCall(media) {
        if (isInitiatingCall || !currentChatId || !currentChatInfo) return;
        if (!currentChatInfo.isGroupChat && currentChatPeerIsBot) return;
        var target;
        if (currentChatInfo.isGroupChat) {
            target = { chatId: currentChatId };
        } else {
            var peerId = (currentChatInfo.membersId || []).find(function (id) { return id !== myUserId; });
            if (!peerId) return;
            target = { userId: peerId };
        }
        isInitiatingCall = true;
        BF.callsUI.ensureMediaPermissions(media)
            .then(function () { return BF.calls.initiate(target, media); })
            .catch(function (e) {
                if (!e || e.code !== 'media-permission-dismissed') console.error('call start failed:', e);
            })
            .finally(function () { isInitiatingCall = false; });
    }
    var _btnCallAudio = $('#btnCallAudio');
    var _btnCallVideo = $('#btnCallVideo');
    if (_btnCallAudio) _btnCallAudio.addEventListener('click', function () { startCall(BF.calls.MediaType.AUDIO); });
    if (_btnCallVideo) _btnCallVideo.addEventListener('click', function () { startCall(BF.calls.MediaType.VIDEO); });

    profileClose.addEventListener('click', function () { BF.utils.closeOverlay(profileOverlay); });
    profileOverlay.addEventListener('click', function (e) { if (e.target === profileOverlay) BF.utils.closeOverlay(profileOverlay); });

    var _profileMsgBtn = $('#profileMsgBtn');
    var _profileCallAudioBtn = $('#profileCallAudioBtn');
    var _profileCallVideoBtn = $('#profileCallVideoBtn');
    if (_profileMsgBtn) _profileMsgBtn.addEventListener('click', function () { BF.utils.closeOverlay(profileOverlay); });

    function openChatBackgroundSelector(chatId, title) {
        if (!chatId) return;
        var overlay = $('#chatBackgroundSelector');
        var selectorTitle = $('#chatBackgroundSelectorTitle');
        var grid = $('#chatBackgroundSelectorGrid');
        selectorTitle.textContent = BF.i18n.t('chat.background.for', { title: title || BF.i18n.t('common.chat').toLowerCase() });
        grid.innerHTML = '<div class="sd-hint">' + u.escapeHtml(BF.i18n.t('common.loadingShort')) + '</div>';
        BF.utils.openOverlay(overlay);

        BF.api.getPersonalization().then(function (data) {
            var ids = ((data && data.personalization) || {}).chatBackgroundFileIds || [];
            var current = BF.personalization.getChatBackgroundFileId(chatId);
            grid.innerHTML = '';
            function addCard(fileId, label) {
                var card = document.createElement('button');
                card.type = 'button';
                card.className = 'sd-bg-card' + (current === fileId ? ' active' : '') + (!fileId ? ' none-card' : '');
                if (fileId) {
                    var image = document.createElement('img');
                    image.alt = '';
                    BF.files.bindResilientMedia(image, fileId, true);
                    card.appendChild(image);
                    BF.files.getFileUrls([fileId]).then(function (urls) {
                        var item = urls && urls[0];
                        if (item) image.src = item.previewUrl || item.url;
                    });
                } else {
                    card.textContent = label;
                }
                card.addEventListener('click', function () {
                    card.disabled = true;
                    BF.personalization.setChatBackgroundFileId(chatId, fileId).then(function () {
                        BF.utils.closeOverlay(overlay);
                    }).catch(function () { card.disabled = false; });
                });
                grid.appendChild(card);
            }
            addCard('', BF.i18n.t('chat.background.useGlobal'));
            ids.forEach(function (fileId) { addCard(fileId, ''); });
        }).catch(function () { grid.innerHTML = '<div class="sd-hint error">' + u.escapeHtml(BF.i18n.t('chat.background.error')) + '</div>'; });
    }

    var _chatBackgroundSelector = $('#chatBackgroundSelector');
    var _chatBackgroundSelectorClose = $('#chatBackgroundSelectorClose');
    if (_chatBackgroundSelectorClose) _chatBackgroundSelectorClose.addEventListener('click', function () {
        BF.utils.closeOverlay(_chatBackgroundSelector);
    });
    if (_chatBackgroundSelector) _chatBackgroundSelector.addEventListener('click', function (e) {
        if (e.target === _chatBackgroundSelector) BF.utils.closeOverlay(_chatBackgroundSelector);
    });

    var _profileBackgroundBtn = $('#profileBackgroundButton');
    if (_profileBackgroundBtn) _profileBackgroundBtn.addEventListener('click', function () {
        openChatBackgroundSelector(currentChatId, currentChatInfo && currentChatInfo.title);
    });
    if (_profileCallAudioBtn) _profileCallAudioBtn.addEventListener('click', function () { startCall(BF.calls.MediaType.AUDIO); });
    if (_profileCallVideoBtn) _profileCallVideoBtn.addEventListener('click', function () { startCall(BF.calls.MediaType.VIDEO); });

    function copyText(text) {
        if (!text || !navigator.clipboard) return;
        navigator.clipboard.writeText(String(text)).then(function () {
            BF.sound.play('success');
            groupToast(BF.i18n.t('common.copied'));
        }).catch(function () {
            showToast(BF.i18n.t('error.copy'), true);
        });
    }
    document.querySelectorAll('.profile-info-copy').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var target = document.getElementById(btn.dataset.copy);
            if (target) copyText(target.textContent);
        });
    });

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

    // ========== SCROLL TO BOTTOM BUTTON ==========

    if (scrollToBottomBtn) {
        scrollToBottomBtn.addEventListener('click', function () {
            scrollToBottom();
            scrollToBottomBtn.classList.remove('visible');
            newMessagesBelowCount = 0;
            updateScrollBadge();
        });
    }

    // ========== SETTINGS MODAL ==========

    BF.settings.init({ myUserId: myUserId });
    BF.attach.init();
    if (BF.pendingSends) {
        BF.pendingSends.init(myUserId);
        restorePendingSends();
    }
    if (BF.drafts) BF.drafts.init(myUserId);
    if (BF.imageEditor) BF.imageEditor.init();
    $('#navChats').addEventListener('click', function () { /* already on chats page */ });
    $('#navSettings').addEventListener('click', function () { BF.settings.open(); });

    // ========== STICKER PICKER ==========

    var stickerPacksCache = null;
    var stickerPacksContentCache = {}; // packId → { stickers, coverFileId }
    var currentStickerPackId = null;
    var RECENT_TAB = '__recent__';
    var RECENT_STICKER_LIMIT = 32;
    var recentStickerIds = [];
    var stickerSearchQuery = '';
    var stickerGridRenderVersion = 0;

    function recentStickersKey() { return BF.node.key('bf_recent_stickers_' + myUserId); }
    try { recentStickerIds = JSON.parse(localStorage.getItem(recentStickersKey()) || '[]') || []; } catch (_) { recentStickerIds = []; }

    function addRecentSticker(stickerId) {
        if (!stickerId) return;
        var i = recentStickerIds.indexOf(stickerId);
        if (i >= 0) recentStickerIds.splice(i, 1);
        recentStickerIds.unshift(stickerId);
        if (recentStickerIds.length > RECENT_STICKER_LIMIT) recentStickerIds.length = RECENT_STICKER_LIMIT;
        try { localStorage.setItem(recentStickersKey(), JSON.stringify(recentStickerIds)); } catch (_) {}
    }

    // Локальные id резолвятся против закешированных паков: стикер, удалённый из пака, просто исчезает.
    function resolveRecentStickers() {
        var result = [];
        recentStickerIds.forEach(function (id) {
            for (var packId in stickerPacksContentCache) {
                var stickers = stickerPacksContentCache[packId].stickers || [];
                for (var i = 0; i < stickers.length; i++) {
                    if (stickers[i].id === id) { result.push(stickers[i]); return; }
                }
            }
        });
        return result;
    }

    if (stickerBtn) {
        stickerBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            var isOpen = stickerPicker.classList.contains('visible');
            stickerPicker.classList.toggle('visible', !isOpen);
            stickerBtn.classList.toggle('active', !isOpen);
            if (!isOpen) {
                if (stickerSearch) { stickerSearch.value = ''; stickerSearchQuery = ''; }
                loadStickerPacks();
            }
        });
    }

    document.addEventListener('click', function (e) {
        if (!stickerPicker || !stickerPicker.classList.contains('visible')) return;
        if (!stickerPicker.contains(e.target) && !stickerBtn.contains(e.target)) {
            stickerPicker.classList.remove('visible');
            stickerBtn.classList.remove('active');
        }
    });

    if (stickerSearch) {
        stickerSearch.addEventListener('input', function () {
            stickerSearchQuery = stickerSearch.value.trim();
            renderStickerGrid();
        });
    }

    function defaultStickerTabId() {
        return resolveRecentStickers().length > 0 ? RECENT_TAB : stickerPacksCache[0].id;
    }

    function loadStickerPacks() {
        if (stickerPacksCache) {
            if (stickerPacksCache.length === 0) return;
            renderStickerPackTabs();
            if (!currentStickerPackId) loadStickerPackContent(defaultStickerTabId());
            else if (currentStickerPackId === RECENT_TAB) loadStickerPackContent(RECENT_TAB);
            return;
        }
        BF.api.listStickerPacks(0, 50).then(function (data) {
            stickerPacksCache = data.packs || [];
            if (stickerPacksCache.length === 0) {
                if (stickerGrid) stickerGrid.innerHTML = '<div class="sticker-pack-empty">' + u.escapeHtml(BF.i18n.t('sticker.noPacks')) + '</div>';
                return;
            }
            // Prefetch всех паков для обложек и кэша контента
            var loads = stickerPacksCache.map(function (p) {
                return BF.api.getStickerPack(p.id).then(function (d) {
                    var stickers = d.stickers || [];
                    var cover = stickers.find(function (s) { return s.id === p.coverStickerId; }) || stickers[0];
                    stickerPacksContentCache[p.id] = {
                        stickers: stickers,
                        coverFileId: cover ? (cover.previewFileId || cover.fileId) : null
                    };
                }).catch(function () {
                    stickerPacksContentCache[p.id] = { stickers: [], coverFileId: null };
                });
            });
            Promise.all(loads).then(function () {
                var coverIds = stickerPacksCache
                    .map(function (p) { return stickerPacksContentCache[p.id].coverFileId; })
                    .filter(Boolean);
                return coverIds.length > 0 ? BF.files.getFileUrls(coverIds) : Promise.resolve();
            }).then(function () {
                renderStickerPackTabs();
                loadStickerPackContent(defaultStickerTabId());
            });
        }).catch(function () {
            if (stickerGrid) stickerGrid.innerHTML = '<div class="sticker-pack-empty">' + u.escapeHtml(BF.i18n.t('common.loadError')) + '</div>';
        });
    }

    function renderStickerPackTabs() {
        if (!stickerPacksBar) return;
        stickerPacksBar.innerHTML = '';
        if (resolveRecentStickers().length > 0) {
            var recentTab = document.createElement('div');
            recentTab.className = 'sticker-pack-tab recent' + (currentStickerPackId === RECENT_TAB ? ' active' : '');
            recentTab.title = BF.i18n.t('sticker.recent');
            recentTab.appendChild(BF.icons.element('history'));
            recentTab.addEventListener('click', function (event) {
                event.stopPropagation();
                loadStickerPackContent(RECENT_TAB);
            });
            stickerPacksBar.appendChild(recentTab);
        }
        stickerPacksCache.forEach(function (pack) {
            var tab = document.createElement('div');
            tab.className = 'sticker-pack-tab' + (pack.id === currentStickerPackId ? ' active' : '');
            tab.title = pack.name || '';
            var cached = stickerPacksContentCache[pack.id];
            var coverFid = cached && cached.coverFileId;
            var fd = coverFid ? BF.files.getCachedFileUrl(coverFid) : null;
            var url = fd && (fd.previewUrl || fd.url);
            if (url) {
                var img = document.createElement('img');
                img.src = url;
                img.alt = pack.name || '';
                BF.files.bindResilientMedia(img, coverFid, true);
                tab.appendChild(img);
            } else {
                tab.textContent = (pack.name || '?')[0].toUpperCase();
            }
            tab.addEventListener('click', function (event) {
                event.stopPropagation();
                loadStickerPackContent(pack.id);
            });
            stickerPacksBar.appendChild(tab);
        });
    }

    function currentTabStickers() {
        if (currentStickerPackId === RECENT_TAB) return resolveRecentStickers();
        var cached = stickerPacksContentCache[currentStickerPackId];
        return cached ? cached.stickers : [];
    }

    function renderStickerGrid() {
        if (!stickerGrid) return;
        var renderVersion = ++stickerGridRenderVersion;
        stickerGrid.innerHTML = '';
        var stickers = currentTabStickers();
        if (stickers.length === 0) {
            stickerGrid.innerHTML = '<div class="sticker-pack-empty">' + u.escapeHtml(BF.i18n.t(currentStickerPackId === RECENT_TAB ? 'sticker.empty' : 'sticker.packEmpty')) + '</div>';
            return;
        }
        if (stickerSearchQuery) {
            stickers = stickers.filter(function (s) { return (s.emoji || '').indexOf(stickerSearchQuery) >= 0; });
            if (stickers.length === 0) {
                stickerGrid.innerHTML = '<div class="sticker-pack-empty">' + u.escapeHtml(BF.i18n.t('sticker.empty')) + '</div>';
                return;
            }
        }
        // Показываем full-версии стикеров (fileId, не preview)
        var fileIds = stickers.map(function (s) { return s.fileId; }).filter(Boolean);
        BF.files.getFileUrls(fileIds).then(function () {
            if (renderVersion !== stickerGridRenderVersion) return;
            stickers.forEach(function (s) {
                var fd = BF.files.getCachedFileUrl(s.fileId);
                var url = fd && fd.url;
                if (!url) return;
                var img = document.createElement('img');
                img.src = url;
                img.title = s.emoji || '';
                img.loading = 'lazy';
                img.addEventListener('click', function () { sendSticker(s); });
                BF.files.bindResilientMedia(img, s.fileId, false);
                stickerGrid.appendChild(img);
            });
        });
    }

    function loadStickerPackContent(packId) {
        currentStickerPackId = packId;
        renderStickerPackTabs();
        renderStickerGrid();
    }

    function sendSticker(sticker) {
        var fileId = sticker && sticker.fileId;
        if (!currentChatId || currentChatType === 1 || !fileId) return;
        addRecentSticker(sticker.id);
        stickerPicker.classList.remove('visible');
        stickerBtn.classList.remove('active');
        var sentChatId = currentChatId;
        BF.api.sendMessage({ chatId: sentChatId, text: null, fileIds: [fileId] }).then(function (resp) {
            if (resp && resp.message) {
                var msg = resp.message;
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
        }).catch(function () {
            showToast(BF.i18n.t('error.sendMessage'), true);
        });
    }

    // ========== REPLY / FORWARD / CONTEXT MENU ==========

    function showSoonToast() {
        showToast(BF.i18n.t('common.comingSoon'), false);
    }

    function buildReplyPreviewText(msg) {
        if (msg.content && msg.content.text) return msg.content.text;
        var atts = (msg.content && msg.content.attachments) || [];
        for (var i = 0; i < atts.length; i++) {
            var t = atts[i].type;
            if (t === 8 || t === '8' || t === 'FORWARDED_MESSAGE') continue;
            return u.attachmentEmoji(t === 7 || t === '7' ? 'STICKER' : t);
        }
        return '';
    }

    function setPendingReply(msg, persist) {
        if (!msg) return;
        pendingReply = {
            messageId: msg.id,
            authorName: '',
            previewText: buildReplyPreviewText(msg)
        };
        if (msg.senderId === myUserId) {
            pendingReply.authorName = BF.i18n.t('call.you');
            renderReplyPreview();
        } else {
            getUser(msg.senderId).then(function (sender) {
                if (!pendingReply || pendingReply.messageId !== msg.id) return;
                if (sender) {
                    pendingReply.authorName = ((sender.firstName || '') + ' ' + (sender.lastName || '')).trim() || sender.username || '';
                }
                renderReplyPreview();
            });
            renderReplyPreview();
        }
        if (messageInput) {
            try { messageInput.focus(); } catch (e) { }
        }
        if (persist !== false) saveCurrentDraft();
    }

    function renderReplyPreview() {
        if (!replyPreviewBar) return;
        if (!pendingReply) {
            replyPreviewBar.classList.remove('visible');
            return;
        }
        rpbAuthor.textContent = pendingReply.authorName || '';
        rpbText.textContent = pendingReply.previewText || '';
        replyPreviewBar.classList.add('visible');
    }

    function clearPendingReply(persist) {
        pendingReply = null;
        if (replyPreviewBar) replyPreviewBar.classList.remove('visible');
        if (persist !== false) saveCurrentDraft();
    }

    function setPendingEdit(msg) {
        if (!msg) return;
        clearPendingReply();
        var origText = (msg.content && msg.content.text) || '';
        pendingEdit = { messageId: msg.id, originalText: origText };
        messageInput.value = origText;
        messageInput.style.height = 'auto';
        messageInput.style.height = Math.min(messageInput.scrollHeight, 120) + 'px';
        if (epbText) epbText.textContent = origText || BF.i18n.t('composer.edit.attachmentsOnly');
        if (editPreviewBar) editPreviewBar.classList.add('visible');
        try { messageInput.focus(); } catch (e) {}
    }

    function clearPendingEdit() {
        pendingEdit = null;
        if (editPreviewBar) editPreviewBar.classList.remove('visible');
        messageInput.value = '';
        messageInput.style.height = 'auto';
    }

    function requestDelete(messageId) {
        if (!deleteMsgConfirmOverlay || !messageId) return;
        BF.utils.openOverlay(deleteMsgConfirmOverlay);
        deleteMsgOk.onclick = function () {
            deleteMsgOk.disabled = true;
            BF.api.deleteMessage(messageId).then(function () {
                applyMessageDelete(currentChatId, messageId);
            }).catch(function () {
                showToast(BF.i18n.t('error.deleteMessage'), true);
            })
            .finally(function () {
                deleteMsgOk.disabled = false;
                BF.utils.closeOverlay(deleteMsgConfirmOverlay);
                deleteMsgOk.onclick = null;
            });
        };
    }

    function applyMessageEdit(chatId, updatedMsg) {
        if (!updatedMsg) return;
        var ch = chats.find(function (x) { return x.id === chatId; });
        if (ch && ch.lastMessage && ch.lastMessage.id === updatedMsg.id) {
            ch.lastMessage = updatedMsg;
            renderChatList();
        }
        if (chatId !== currentChatId) return;
        var idx = messages.findIndex(function (m) { return m.id === updatedMsg.id; });
        if (idx < 0) return;
        messages[idx] = updatedMsg;
        var oldEl = messagesInner.querySelector('.msg-group[data-msg-id="' + updatedMsg.id + '"]');
        if (!oldEl) return;
        BF.messages.buildMessageElement(
            updatedMsg, myUserId, getUser, showMediaOverlay, buildMessageOptions(updatedMsg, idx)
        ).then(function (newEl) {
            newEl.dataset.date = oldEl.dataset.date;
            oldEl.replaceWith(newEl);
        });
    }

    function applyMessageDelete(chatId, messageId) {
        if (messageId == null) return;
        var msgIdNum = Number(messageId);
        console.log('[main] applyMessageDelete', { chatId: chatId, messageId: messageId, currentChatId: currentChatId });

        // messageId глобально уникален: ищем и удаляем во всех текущих структурах,
        // не привязываясь к chatId-сравнению (на случай расхождения форматов id).
        var idx = messages.findIndex(function (m) { return Number(m.id) === msgIdNum; });
        if (idx >= 0) messages.splice(idx, 1);
        if (idx >= 0) renderMessages();

        // Обновляем lastMessage чат-листа для всех чатов, где это сообщение последнее.
        var anyChatTouched = false;
        chats.forEach(function (c) {
            if (c.lastMessage && Number(c.lastMessage.id) === msgIdNum) anyChatTouched = true;
        });
        if (anyChatTouched) loadChats(true);

        if (BF.pinned && BF.pinned.applyMessageDeleted) BF.pinned.applyMessageDeleted(msgIdNum);
    }

    function openContextMenu(x, y, msgEl) {
        if (!msgContextMenu || !msgEl) return;
        if (currentChatType === 1) return; // edit/delete/reply/pin для приватных сообщений не поддерживаются

        if (msgEl.classList.contains('msg-system')) return;
        var msgId = Number(msgEl.dataset.msgId);
        if (!msgId) return;
        var isOutgoing = msgEl.classList.contains('outgoing');
        contextMenuTarget = { messageId: msgId, isOutgoing: isOutgoing };

        var msgObj = messages.find(function (m) { return Number(m.id) === Number(msgId); });
        var isSystem = msgObj && (msgObj.type === 2 || msgObj.type === 'SYSTEM');
        var canModify = isOutgoing && !isSystem;
        var editBtn = msgContextMenu.querySelector('button[data-act="edit"]');
        var deleteBtn = msgContextMenu.querySelector('button[data-act="delete"]');
        if (editBtn) editBtn.style.display = canModify ? '' : 'none';
        if (deleteBtn) deleteBtn.style.display = canModify ? '' : 'none';

        // Pin/Unpin: для системных сообщений скрываем; для остальных — динамический текст.
        var pinBtn = msgContextMenu.querySelector('button[data-act="pin"]');
        if (pinBtn) {
            if (isSystem) {
                pinBtn.style.display = 'none';
            } else {
                pinBtn.style.display = '';
                var alreadyPinned = BF.pinned && BF.pinned.isPinned && BF.pinned.isPinned(msgId);
                var pinLabel = pinBtn.querySelector('.cm-label');
                if (pinLabel) pinLabel.textContent = BF.i18n.t(alreadyPinned ? 'menu.unpin' : 'menu.pin');
                pinBtn.dataset.state = alreadyPinned ? 'pinned' : 'unpinned';
            }
        }

        // Копировать текст — если у сообщения есть текст.
        var copyTextBtn = msgContextMenu.querySelector('button[data-act="copy-text"]');
        var msgText = msgObj && msgObj.content && msgObj.content.text;
        if (copyTextBtn) copyTextBtn.style.display = (msgText && !isSystem) ? '' : 'none';

        // Копировать изображение — только если ровно одно изображение и оно единственное медиа.
        var copyImageBtn = msgContextMenu.querySelector('button[data-act="copy-image"]');
        var singleImageFileId = null;
        if (msgObj && msgObj.content && msgObj.content.attachments && !isSystem) {
            var imgAtts = msgObj.content.attachments.filter(function (a) { return a.type !== 'FORWARDED_MESSAGE'; });
            if (imgAtts.length === 1 && (imgAtts[0].type === 'IMAGE' || imgAtts[0].type === 'GIF')) {
                singleImageFileId = imgAtts[0].fileId;
            }
        }
        contextMenuTarget.image = singleImageFileId ? msgEl.querySelector('.attach-image-grid img') : null;
        if (copyImageBtn) copyImageBtn.style.display = contextMenuTarget.image ? '' : 'none';

        msgContextMenu.classList.add('visible');

        var vw = window.innerWidth;
        var vh = window.innerHeight;
        var rect = msgContextMenu.getBoundingClientRect();
        var w = rect.width, h = rect.height;
        var left = Math.max(8, Math.min(x, vw - w - 8));
        var top = (y + h > vh) ? Math.max(8, y - h) : y;
        msgContextMenu.style.left = left + 'px';
        msgContextMenu.style.top = top + 'px';
        cmenuShownAt = Date.now();
    }

    function closeContextMenu() {
        if (!msgContextMenu) return;
        msgContextMenu.classList.remove('visible');
        contextMenuTarget = null;
    }

    function canvasToPngBlob(drawable, width, height) {
        return new Promise(function (resolve, reject) {
            try {
                var canvas = document.createElement('canvas');
                canvas.width = width;
                canvas.height = height;
                canvas.getContext('2d').drawImage(drawable, 0, 0);
                canvas.toBlob(function (blob) {
                    if (blob) resolve(blob);
                    else reject(new Error('no_png'));
                }, 'image/png');
            } catch (err) {
                reject(err);
            }
        });
    }

    // Копируем уже загруженное превью из облачка, а не полную версию файла.
    function copyImageToClipboard(image) {
        if (!navigator.clipboard || typeof ClipboardItem === 'undefined' || !image) return;
        var imageUrl = image.currentSrc || image.src;
        var previewPng = image.complete && image.naturalWidth && image.naturalHeight
            ? canvasToPngBlob(image, image.naturalWidth, image.naturalHeight)
            : Promise.reject(new Error('preview_not_loaded'));

        previewPng.catch(function () {
            if (!imageUrl) throw new Error('no_preview_url');
            return fetch(imageUrl).then(function (response) {
                if (!response.ok) throw new Error('preview_unavailable');
                return response.blob();
            }).then(function (blob) {
                return createImageBitmap(blob).then(function (bitmap) {
                    return canvasToPngBlob(bitmap, bitmap.width, bitmap.height).then(function (png) {
                        bitmap.close();
                        return png;
                    });
                });
            });
        }).then(function (png) {
            return navigator.clipboard.write([new ClipboardItem({ 'image/png': png })]);
        }).catch(function () {
            showToast(BF.i18n.t('error.copy'), true);
        });
    }

    function chatAvatarMarkup(chat) {
        var initial = (chat.title || '?')[0].toUpperCase();
        if (chat.picture) return '<img src="' + u.escapeHtml(chat.picture) + '" alt="">';
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
    function resolveForwardSourceIds(msg, fallbackId) {
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
        forwards.sort(function (x, y) { return (x.order || 0) - (y.order || 0); });
        for (var j = 0; j < forwards.length; j++) {
            if (forwards[j].originalMessageId) ids.push(forwards[j].originalMessageId);
        }
        return ids.length > 0 ? ids : [fallbackId];
    }

    function openForwardModal(sourceMessageIds) {
        if (!forwardOverlay || !sourceMessageIds || sourceMessageIds.length === 0) return;
        forwardSelection = new Set();
        if (forwardCommentEl) forwardCommentEl.value = '';
        forwardChatListEl.innerHTML = '';

        chats.forEach(function (chat) {
            var item = document.createElement('div');
            item.className = 'forward-chat-item';
            item.dataset.chatId = chat.id;
            item.innerHTML =
                '<div class="fwd-avatar">' + chatAvatarMarkup(chat) + '</div>' +
                '<div class="fwd-name">' + u.escapeHtml(chat.title || BF.i18n.t('common.chat')) + '</div>' +
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
        forwardSendBtn.onclick = function () { forwardSubmit(sourceMessageIds); };
    }

    function closeForwardModal() {
        if (!forwardOverlay) return;
        BF.utils.closeOverlay(forwardOverlay);
        forwardSelection = new Set();
        if (forwardSendBtn) forwardSendBtn.onclick = null;
    }

    function forwardSubmit(sourceMessageIds) {
        if (forwardSelection.size === 0 || !sourceMessageIds || sourceMessageIds.length === 0) return;
        var comment = forwardCommentEl ? forwardCommentEl.value.trim() : '';
        var ids = Array.from(forwardSelection);
        forwardSendBtn.disabled = true;
        var originalLabel = forwardSendBtn.textContent;
        forwardSendBtn.textContent = BF.i18n.t('forward.sending');

        var chain = ids.reduce(function (p, chatId) {
            return p.then(function () {
                return BF.api.sendMessage({
                    chatId: chatId,
                    text: comment || null,
                    forwardedMessageIds: sourceMessageIds
                }).catch(function () { });
            });
        }, Promise.resolve());

        chain.then(function () {
            forwardSendBtn.disabled = false;
            forwardSendBtn.textContent = originalLabel;
            closeForwardModal();
            showToast(BF.i18n.tp('forward.done', ids.length), false);
        });
    }

    // Прыжок к сообщению (reply-цитата, закреплённые): цель уже в DOM — плавный
    // скролл с подсветкой; иначе грузим окно ±30 вокруг цели и заменяем буфер
    // целиком. Идём через loadMessagesPage — работает и в приватных (E2E) чатах.
    // Прежний merge старого буфера с загруженным участком оставлял дыру в истории
    // без флага hasNewerGap, из-за чего хвост «смешивался» с прыжком.
    function scrollToMessage(id) {
        if (!id) return;
        var el = messagesInner.querySelector('[data-msg-id="' + id + '"]');
        if (el) {
            el.scrollIntoView({ block: 'center', behavior: 'smooth' });
            el.classList.add('highlight');
            setTimeout(function () { el.classList.remove('highlight'); }, 1500);
            return;
        }
        if (!currentChatId || isJumpingToMessage || isJumpingToTail || isLoadingOlder || isLoadingNewer) return;
        isJumpingToMessage = true;
        var chatId = currentChatId;

        loadMessagesPage(chatId, id, 30, 30).then(function (data) {
            if (chatId !== currentChatId) return;
            var fetched = (data && data.messages) || [];
            var target = fetched.find(function (m) { return Number(m.id) === Number(id); });
            if (!target) {
                showToast(BF.i18n.t('chat.messageNotFound'), true);
                return;
            }

            messages = fetched;
            mergePendingUploadsIntoMessages(chatId);
            resyncSeparatorId = null; // окно перенесено к цели прыжка — прежняя граница «нового» неактуальна

            // Края чата определяем по числу сообщений старее/новее цели: api.js
            // подменяет offsetBefore=0 на 30, поэтому размер ответа не показатель.
            // При ровно 30 оставляем «зазор» — следующая догрузка его закроет.
            var targetId = Number(id);
            var olderCount = fetched.filter(function (m) { return Number(m.id) < targetId; }).length;
            var newerCount = fetched.filter(function (m) { return Number(m.id) > targetId; }).length;
            noMoreOlder = olderCount < 30;
            hasNewerGap = newerCount >= 30; // хвост за окном: живые сообщения не рисуются (guard appendMessageToView)

            return renderMessages().then(function () { settleHighlight(id); });
        }).finally(function () { isJumpingToMessage = false; });
    }

    // --- Reply preview close handler ---
    if (rpbCloseBtn) rpbCloseBtn.addEventListener('click', clearPendingReply);

    // --- Edit preview close handler ---
    if (epbCloseBtn) epbCloseBtn.addEventListener('click', clearPendingEdit);

    // --- Delete confirm cancel ---
    if (deleteMsgCancel) {
        deleteMsgCancel.addEventListener('click', function () {
            if (deleteMsgConfirmOverlay) BF.utils.closeOverlay(deleteMsgConfirmOverlay);
            if (deleteMsgOk) deleteMsgOk.onclick = null;
        });
    }
    if (deleteMsgConfirmOverlay) {
        deleteMsgConfirmOverlay.addEventListener('click', function (e) {
            if (e.target === deleteMsgConfirmOverlay) {
                BF.utils.closeOverlay(deleteMsgConfirmOverlay);
                if (deleteMsgOk) deleteMsgOk.onclick = null;
            }
        });
    }

    // --- Forward modal close ---
    if (forwardCloseBtn) forwardCloseBtn.addEventListener('click', closeForwardModal);
    if (forwardOverlay) {
        forwardOverlay.addEventListener('click', function (e) {
            if (e.target === forwardOverlay) closeForwardModal();
        });
    }

    // --- Context menu actions ---
    if (msgContextMenu) {
        msgContextMenu.addEventListener('click', function (e) {
            var btn = e.target.closest('button[data-act]');
            if (!btn || !contextMenuTarget) return;
            var act = btn.dataset.act;
            var msgId = contextMenuTarget.messageId;
            var isOutgoing = contextMenuTarget.isOutgoing;
            var image = contextMenuTarget.image;
            var msg = messages.find(function (m) { return Number(m.id) === Number(msgId); });
            closeContextMenu();
            if (act === 'reply') {
                if (msg) setPendingReply(msg);
            } else if (act === 'forward') {
                openForwardModal(resolveForwardSourceIds(msg, msgId));
            } else if (act === 'copy-text') {
                var t = msg && msg.content && msg.content.text;
                if (t) navigator.clipboard.writeText(t).catch(function () {
                    showToast(BF.i18n.t('error.copy'), true);
                });
            } else if (act === 'copy-image') {
                copyImageToClipboard(image);
            } else if (act === 'edit') {
                if (msg && isOutgoing && msg.type !== 2 && msg.type !== 'SYSTEM') {
                    setPendingEdit(msg);
                }
            } else if (act === 'delete') {
                if (msg && isOutgoing && msg.type !== 2 && msg.type !== 'SYSTEM') {
                    requestDelete(msg.id);
                }
            } else if (act === 'pin') {
                if (!BF.pinned) return;
                var state = btn.dataset.state;
                if (state === 'pinned') {
                    BF.pinned.unpin(msgId);
                } else {
                    BF.pinned.pin(msgId);
                }
            } else {
                showSoonToast();
            }
        });
    }

    // --- Global close handlers for context menu ---
    document.addEventListener('click', function (e) {
        if (!msgContextMenu || !msgContextMenu.classList.contains('visible')) return;
        if (msgContextMenu.contains(e.target)) return;
        if (Date.now() - cmenuShownAt < 300) return;
        closeContextMenu();
    }, true);
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') {
            if (msgContextMenu && msgContextMenu.classList.contains('visible')) closeContextMenu();
            if (forwardOverlay && forwardOverlay.classList.contains('visible')) closeForwardModal();
        }
    });
    window.addEventListener('resize', closeContextMenu);
    if (messagesArea) messagesArea.addEventListener('scroll', closeContextMenu);

    // --- contextmenu (desktop right-click + system long-press) ---
    if (messagesInner) {
        messagesInner.addEventListener('contextmenu', function (e) {
            var grp = e.target.closest('.msg-group');
            if (!grp || !grp.dataset.msgId) return;
            e.preventDefault();
            openContextMenu(e.clientX, e.clientY, grp);
        });
    }

    // --- Touch handlers: long-press + swipe-left to reply ---
    (function () {
        if (!messagesInner) return;

        var pressTimer = null;
        var startX = 0, startY = 0;
        var lastX = 0, lastY = 0;
        var pressTarget = null;
        var swiping = false;
        var swipeLockedForReply = false;
        var axisLocked = false;
        var INTERACTIVE_SEL = 'img, video, a, button, .audio-play-btn, .attach-doc';

        function cancelPressTimer() {
            if (pressTimer) { clearTimeout(pressTimer); pressTimer = null; }
        }

        function resetSwipe(grp) {
            if (!grp) return;
            grp.classList.remove('swiping');
            grp.style.transform = '';
        }

        messagesInner.addEventListener('touchstart', function (e) {
            if (e.touches.length !== 1) return;
            var grp = e.target.closest('.msg-group');
            if (!grp || !grp.dataset.msgId) return;
            // Skip swipe init if interactive child
            var skipSwipe = !!e.target.closest(INTERACTIVE_SEL);

            pressTarget = grp;
            startX = lastX = e.touches[0].clientX;
            startY = lastY = e.touches[0].clientY;
            swiping = false;
            swipeLockedForReply = false;
            axisLocked = false;

            cancelPressTimer();
            pressTimer = setTimeout(function () {
                pressTimer = null;
                if (!pressTarget) return;
                if (swiping) return;
                try { if (navigator.vibrate) navigator.vibrate(20); } catch (e2) { }
                var rect = pressTarget.getBoundingClientRect();
                var cx = Math.min(Math.max(startX, rect.left), rect.right);
                var cy = Math.min(Math.max(startY, rect.top), rect.bottom);
                openContextMenu(cx, cy, pressTarget);
            }, 500);

            grp._skipSwipe = skipSwipe;
        }, { passive: true });

        messagesInner.addEventListener('touchmove', function (e) {
            if (!pressTarget) return;
            if (e.touches.length !== 1) return;
            lastX = e.touches[0].clientX;
            lastY = e.touches[0].clientY;
            var dx = lastX - startX;
            var dy = lastY - startY;

            if (!axisLocked) {
                if (Math.abs(dx) > 10 || Math.abs(dy) > 10) {
                    axisLocked = true;
                    if (Math.abs(dy) > Math.abs(dx)) {
                        // vertical scroll — abort everything
                        cancelPressTimer();
                        pressTarget = null;
                        return;
                    } else {
                        cancelPressTimer();
                    }
                } else {
                    return;
                }
            }

            // Only horizontal swipe, only on mobile, only left, only if not on interactive
            if (!mqlMobile.matches) return;
            if (pressTarget._skipSwipe) return;
            if (dx >= 0) {
                resetSwipe(pressTarget);
                return;
            }

            if (e.cancelable) e.preventDefault();
            swiping = true;
            pressTarget.classList.add('swiping');
            var translate = Math.max(dx, -90);
            pressTarget.style.transform = 'translateX(' + translate + 'px)';
            if (dx <= -60) swipeLockedForReply = true; else swipeLockedForReply = false;
        }, { passive: false });

        messagesInner.addEventListener('touchend', function () {
            cancelPressTimer();
            var t = pressTarget;
            pressTarget = null;
            if (!t) return;
            if (swiping) {
                if (swipeLockedForReply) {
                    var msgId = Number(t.dataset.msgId);
                    var msg = messages.find(function (m) { return m.id === msgId; });
                    if (msg) setPendingReply(msg);
                }
                resetSwipe(t);
            }
        });

        messagesInner.addEventListener('touchcancel', function () {
            cancelPressTimer();
            if (pressTarget) resetSwipe(pressTarget);
            pressTarget = null;
        });
    })();

    // ========== PROACTIVE TOKEN REFRESH ==========

    setInterval(function () {
        if (BF.tokens.isAccessExpired()) {
            BF.clients.refreshToken().then(function (token) {
                if (token) BF.realtime.reconnect();
            });
        }
    }, 60000);

    // ========== CHAT CONTEXT MENU (PCM на чате в сайдбаре) ==========

    var chatContextMenu = $('#chatContextMenu');
    var chatCmTargetId = null;
    var chatCmShownAt = 0;

    function contextMenuIcon() {
        return '<span class="cm-icon">' + BF.icons.html('chat-folders') + '</span>';
    }

    function buildChatContextMenu(chatId) {
        if (!chatContextMenu) return;
        chatContextMenu.innerHTML = '';

        if (BF.folders) {
            var without = BF.folders.getFoldersWithoutChat(chatId);
            var inFolders = BF.folders.getFoldersForChat(chatId);

            if (without.length > 0) {
                var hdr1 = document.createElement('div');
                hdr1.className = 'cm-section-title';
                hdr1.textContent = BF.i18n.t('folder.addToFolder');
                chatContextMenu.appendChild(hdr1);
                without.forEach(function (f) {
                    var btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'cm-item';
                    btn.dataset.act = 'add-folder';
                    btn.dataset.folderId = f.folderId;
                    btn.innerHTML = contextMenuIcon() + '<span class="cm-label">' + u.escapeHtml(f.folderName || BF.i18n.t('folder.default')) + '</span>';
                    chatContextMenu.appendChild(btn);
                });
            }

            if (inFolders.length > 0) {
                var hdr2 = document.createElement('div');
                hdr2.className = 'cm-section-title';
                hdr2.textContent = BF.i18n.t('folder.removeFromFolder');
                chatContextMenu.appendChild(hdr2);
                inFolders.forEach(function (f) {
                    var btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'cm-item';
                    btn.dataset.act = 'remove-folder';
                    btn.dataset.folderId = f.folderId;
                    btn.innerHTML = contextMenuIcon() + '<span class="cm-label">' + u.escapeHtml(f.folderName || BF.i18n.t('folder.default')) + '</span>';
                    chatContextMenu.appendChild(btn);
                });
            }

            if (without.length > 0 || inFolders.length > 0) {
                var sep = document.createElement('div');
                sep.className = 'cm-separator';
                chatContextMenu.appendChild(sep);
            }
        }

        var createBtn = document.createElement('button');
        createBtn.type = 'button';
        createBtn.className = 'cm-item';
        createBtn.dataset.act = 'create-folder';
        createBtn.innerHTML = contextMenuIcon() + '<span class="cm-label">' + u.escapeHtml(BF.i18n.t('folder.create')) + '</span>';
        chatContextMenu.appendChild(createBtn);
    }

    function openChatContextMenu(x, y, chatId) {
        if (!chatContextMenu) return;
        chatCmTargetId = chatId;
        buildChatContextMenu(chatId);
        chatContextMenu.classList.add('visible');

        var vw = window.innerWidth, vh = window.innerHeight;
        var rect = chatContextMenu.getBoundingClientRect();
        var w = rect.width, h = rect.height;
        var left = Math.max(8, Math.min(x, vw - w - 8));
        var top = (y + h > vh) ? Math.max(8, y - h) : y;
        chatContextMenu.style.left = left + 'px';
        chatContextMenu.style.top = top + 'px';
        chatCmShownAt = Date.now();
    }

    function closeChatContextMenu() {
        if (!chatContextMenu) return;
        chatContextMenu.classList.remove('visible');
        chatCmTargetId = null;
    }

    if (chatListEl) {
        chatListEl.addEventListener('contextmenu', function (e) {
            var item = e.target.closest('.chat-item');
            if (!item || !item.dataset.chatId) return;
            e.preventDefault();
            openChatContextMenu(e.clientX, e.clientY, item.dataset.chatId);
        });
    }

    if (chatContextMenu) {
        chatContextMenu.addEventListener('click', function (e) {
            var btn = e.target.closest('button[data-act]');
            if (!btn) return;
            var act = btn.dataset.act;
            var folderId = btn.dataset.folderId || '';
            var chatId = chatCmTargetId;
            closeChatContextMenu();
            if (!BF.folders) return;
            if (act === 'add-folder' && folderId && chatId) {
                BF.folders.addChatToFolder(folderId, chatId);
            } else if (act === 'remove-folder' && folderId && chatId) {
                BF.folders.removeChatFromFolder(folderId, chatId);
            } else if (act === 'create-folder') {
                BF.folders.openCreateModal();
            }
        });
    }

    document.addEventListener('click', function (e) {
        if (!chatContextMenu || !chatContextMenu.classList.contains('visible')) return;
        if (chatContextMenu.contains(e.target)) return;
        if (Date.now() - chatCmShownAt < 300) return;
        closeChatContextMenu();
    }, true);
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && chatContextMenu && chatContextMenu.classList.contains('visible')) {
            closeChatContextMenu();
        }
    });
    window.addEventListener('resize', closeChatContextMenu);
    if (chatListEl) chatListEl.addEventListener('scroll', closeChatContextMenu);

    // ========== DEEP-LINK ИЗ COOKIE (с публичной страницы пользователя) ==========

    function bfGetCookie(name) {
        var m = document.cookie.match('(?:^|; )' + name.replace(/([.$?*|{}()[\]\\/+^])/g, '\\$1') + '=([^;]*)');
        return m ? decodeURIComponent(m[1]) : null;
    }

    function bfDeleteOpenChatCookie() {
        var base = 'bf_open_chat=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT';
        document.cookie = base;
        if (/(^|\.)barkfluff\.com$/i.test(location.hostname)) {
            document.cookie = base + '; domain=.barkfluff.com';
        }
    }

    // Если на странице пользователя (barkfluff.com/<username>) нажали «Написать в браузере»,
    // там в cookie bf_open_chat записан username. Находим пользователя -> chatId -> открываем чат.
    // Логика повторяет Android DeepLinkActivity: SearchUsers -> точное совпадение -> GetPersonChatId.
    function maybeOpenChatFromCookie() {
        var uname = bfGetCookie('bf_open_chat');
        if (!uname) return;
        bfDeleteOpenChatCookie();          // одноразово: сразу удаляем
        uname = uname.trim();
        if (!uname) return;

        BF.api.searchUsers(uname, 0, 20).then(function (data) {
            var list = (data && data.users) || [];
            var target = null;
            for (var i = 0; i < list.length; i++) {
                if ((list[i].username || '').toLowerCase() === uname.toLowerCase()) {
                    target = list[i];
                    break;
                }
            }
            if (!target) return;           // точного совпадения нет — как в Android, ничего не открываем
            return BF.api.getPersonChatId(target.id).then(function (d) {
                if (d && d.chatId) openChat(d.chatId);
            });
        }).catch(function (err) {
            console.error('maybeOpenChatFromCookie failed:', err);
        });
    }

    var pendingPushChatId = null;
    var refreshedPushChatId = null;

    function openChatFromPush(chatId) {
        if (!chatId) return;
        if (!chats.some(function (chat) { return String(chat.id) === String(chatId); })) {
            pendingPushChatId = chatId;
            // A push may point to a chat outside the initial page of the list.
            // Refresh it once before leaving the link pending.
            if (refreshedPushChatId !== String(chatId)) {
                refreshedPushChatId = String(chatId);
                loadChats(true).then(function () {
                    if (pendingPushChatId) openChatFromPush(pendingPushChatId);
                }).catch(function (err) {
                    console.error('Could not load push target chat:', err);
                });
            }
            return;
        }
        pendingPushChatId = null;
        refreshedPushChatId = null;
        openChat(chatId);
    }

    function maybeOpenChatFromPushUrl() {
        var url = new URL(window.location.href);
        var chatId = url.searchParams.get('chat');
        if (!chatId) return;
        url.searchParams.delete('chat');
        url.searchParams.delete('call');
        window.history.replaceState({}, '', url.pathname + url.search + url.hash);
        openChatFromPush(chatId);
    }

    function maybeOpenPendingPushChat() {
        if (pendingPushChatId) openChatFromPush(pendingPushChatId);
    }

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
        setMessages: function (value) { messages = value; hasNewerGap = false; isLoadingNewer = false; },
        setNoMoreOlder: function (value) { noMoreOlder = value; },
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
        showNewMessageNotification: showNewMessageNotification
    });
    var openPrivateChat = BF.privateChatUI.open;
    var reloadCurrentPrivateChat = BF.privateChatUI.reload;
    var sendPrivateMessageFlow = BF.privateChatUI.send;
    var decryptPrivateBatch = BF.privateChatUI.decryptMessages;

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
    }).then(updateTitleBadge).then(maybeOpenChatFromCookie).then(maybeOpenChatFromPushUrl).then(maybeOpenPendingPushChat);

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
            if (data.type === 'bf-push-open') openChatFromPush(data.chatId);
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
        BF.stickerPack.init({ onStickerSend: sendSticker });
    }

    BF.realtime.startAll();
    if (BF.calls && BF.calls.start) BF.calls.start();

    // Метаданные ноды (в т.ч. отдельный файловый адрес): на самой ноде экрана выбора
    // не было, а адрес мог и поменяться. Промах не мешает — файлы пойдут по адресам Files.
    BF.node.refreshMeta();

    if (BF.personalization && BF.personalization.init) BF.personalization.init();

})();
