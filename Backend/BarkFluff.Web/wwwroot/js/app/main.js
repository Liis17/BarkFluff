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
    var messages = [];
    var isLoadingOlder = false;
    var noMoreOlder = false;
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
    var pendingUploadCounter = 0;
    var GENERIC_MESSAGE_TYPE = 1;
    var IMAGE_UPLOAD_TYPE = 2;
    var GIF_UPLOAD_TYPE = 4;

    // Reply / Forward / Context menu state
    var pendingReply = null;
    var pendingEdit = null; // { messageId, originalText }
    var contextMenuTarget = null;
    var forwardSelection = new Set();
    var knownMessageIds = new Set();
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
    var imageOverlay = $('#imageOverlay');
    var overlayImage = $('#overlayImage');
    var overlayVideo = $('#overlayVideo');
    var overlayPrev = $('#overlayPrev');
    var overlayNext = $('#overlayNext');

    // Scroll-to-bottom button
    var scrollToBottomBtn = $('#scrollToBottomBtn');

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

    // Group info panel elements
    var groupOverlay = $('#groupOverlay');
    var groupClose = $('#groupClose');
    var groupAvatar = $('#groupAvatar');
    var groupAvatarEdit = $('#groupAvatarEdit');
    var groupAvatarInput = $('#groupAvatarInput');
    var groupName = $('#groupName');
    var groupNameEdit = $('#groupNameEdit');
    var groupCount = $('#groupCount');
    var groupMembersEl = $('#groupMembers');
    var groupAddBtn = $('#groupAddBtn');
    var groupAddBox = $('#groupAddBox');
    var groupAddInput = $('#groupAddInput');
    var groupAddResults = $('#groupAddResults');
    var groupMediaContent = $('#groupMediaContent');

    function botBadgeMarkup() {
        return '<span class="bot-badge" role="img" aria-label="Бот" title="Бот"><svg aria-hidden="true"><use href="#bf-icon-bot"></use></svg></span>';
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
        var name = ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || 'Пользователь';
        return 'Чат • ' + name;
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
        chatListEl.innerHTML = '';
        var visibleChats = (BF.folders && BF.folders.filterChats) ? BF.folders.filterChats(chats) : chats;
        visibleChats.forEach(function (chat) {
            var el = document.createElement('div');
            el.className = 'chat-item' + (chat.id === currentChatId ? ' active' : '');
            el.dataset.chatId = chat.id;

            var avatarInitial = (chat.title || '?')[0].toUpperCase();
            var avatarHtml = chat.picture
                ? '<img src="' + u.escapeHtml(chat.picture) + '" alt="">'
                : avatarInitial;

            var isPrivate = chat.chatType === 1;
            var lm = chat.lastMessage;
            var previewHtml = '';
            if (isPrivate) {
                // Содержимое зашифровано — сервер (и превью) его не знает.
                if (chat.privateInviteState === 0) {
                    previewHtml = chat.privateInviterUserId === myUserId
                        ? 'Ожидание собеседника'
                        : '<span class="preview-private-invite">Приглашение в приватный чат</span>';
                } else if (chat.privateInviteState === 2) {
                    previewHtml = 'Приглашение отклонено';
                } else {
                    previewHtml = 'Сообщения зашифрованы';
                }
            } else if (lm) {
                var text = (lm.content && lm.content.text) || '';
                var ac = (lm.content && lm.content.attachments && lm.content.attachments.length) || 0;
                if (lm.type === 2 || lm.type === 'SYSTEM') {
                    previewHtml = u.callPreviewHtml(text, lm.senderId === myUserId) || u.escapeHtml(u.truncate(text, 50));
                } else if (text) {
                    previewHtml = u.escapeHtml(u.truncate(text, 50));
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
                '<span class="chat-name">' + (isPrivate ? '<span class="chat-lock" title="Приватный чат">\u{1F512}</span>' : '') + u.escapeHtml(chat.title || 'Чат') + (isBot ? botBadgeMarkup() : '') + '</span>' +
                '<span class="chat-time">' + time + '</span></div>' +
                '<div class="chat-info-bottom"><span class="chat-preview">' + previewHtml + '</span>' +
                '<span class="chat-unread' + (unread > 0 ? ' visible' : '') + '">' + unreadText + '</span></div></div>';

            el.addEventListener('click', function () { openChat(chat.id); });
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

    function openChat(chatId) {
        if (chatId === currentChatId) return;
        stopTypingSend(true);
        clearTypingReceiveState();

        var chatMeta = chats.find(function (c) { return c.id === chatId; });
        if (chatMeta && chatMeta.chatType === 1) { openPrivateChat(chatMeta); return; }

        if (BF.pinned && BF.pinned.openForChat) BF.pinned.openForChat(chatId);

        currentChatId = chatId;
        BF.realtime.subscribeTyping(chatId);
        currentChatInfo = null;
        currentChatType = 0;
        currentChatPeerIsBot = false;
        messages = [];
        noMoreOlder = false;
        knownMessageIds = new Set();
        clearPendingReply();
        clearPendingEdit();
        closeContextMenu();
        if (scrollToBottomBtn) scrollToBottomBtn.classList.remove('visible');
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

            chatHeaderName.textContent = info.title || 'Чат';
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
                    }).catch(function () {});
                }
            } else {
                chatHeaderStatus.textContent = (info.membersId ? info.membersId.length : 0) + ' участников';
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
            }
        }).catch(function () { loadingMessages.classList.remove('visible'); });
    }

    // Скроллит к первому непрочитанному (если есть) либо в самый низ чата,
    // и повторяет попытку после того как догрузятся картинки сообщений —
    // без этого reflow от догрузки картинок сбивает позицию скролла.
    function settleScroll(unreadId) {
        function anchor() {
            var el = unreadId && messagesInner.querySelector('[data-msg-id="' + unreadId + '"]');
            if (el) el.scrollIntoView({ block: 'start' });
            else scrollToBottom();
        }
        anchor();
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

    function renderMessages() {
        messagesInner.innerHTML = '';
        knownMessageIds = new Set(messages.map(function (m) { return m.id; }));
        var allFileIds = [];
        messages.forEach(function (msg) {
            ((msg.content && msg.content.attachments) || []).forEach(function (a) {
                if (a.fileId && !BF.files.getCachedFileUrl(a.fileId)) allFileIds.push(a.fileId);
            });
            collectFwdAttachments(msg).forEach(function (a) {
                if (a.fileId && !BF.files.getCachedFileUrl(a.fileId)) allFileIds.push(a.fileId);
            });
        });

        var p = allFileIds.length > 0 ? BF.files.getFileUrls(allFileIds) : Promise.resolve();

        return p.then(function () {
            var chain = Promise.resolve();
            var lastDate = null;
            messages.forEach(function (msg, index) {
                chain = chain.then(function () {
                    var msgDate = u.formatDate(msg.sentAt);
                    if (msgDate !== lastDate) {
                        lastDate = msgDate;
                        var sep = document.createElement('div');
                        sep.className = 'msg-date-separator';
                        sep.dataset.date = msgDate;
                        sep.innerHTML = '<span>' + u.escapeHtml(msgDate) + '</span>';
                        messagesInner.appendChild(sep);
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

    function scrollToBottom() { messagesArea.scrollTop = messagesArea.scrollHeight; }

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
            knownMessageIds: knownMessageIds,
            onReplyClick: scrollToMessage,
            groupedWithPrevious: groupedWithPrevious,
            showSenderGutter: showSenderGutter,
            showSenderAvatar: showSenderGutter && !canGroupMessages(msg, next)
        };
    }

    function appendMessageToView(msg) {
        if (msg && msg.id) knownMessageIds.add(msg.id);
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
                if (msgDate !== lastMsgDate) {
                    var sep = document.createElement('div');
                    sep.className = 'msg-date-separator';
                    sep.dataset.date = msgDate;
                    sep.innerHTML = '<span>' + u.escapeHtml(msgDate) + '</span>';
                    messagesInner.appendChild(sep);
                }
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
        releasePendingPreviews(entry);

        var idx = messages.findIndex(function (m) { return String(m.id) === String(entry.localId); });
        if (idx >= 0) messages.splice(idx, 1);
        knownMessageIds.delete(entry.localId);

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

        knownMessageIds.delete(entry.localId);
        if (msg.id) knownMessageIds.add(msg.id);
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

    // Lazy-load older messages
    messagesArea.addEventListener('scroll', function () {
        if (messagesArea.scrollTop < 100 && !isLoadingOlder && !noMoreOlder && currentChatId && messages.length > 0) {
            isLoadingOlder = true;
            loadingMessages.classList.add('visible');
            var oldestId = messages[0].id || 0;
            var prevHeight = messagesArea.scrollHeight;
            var pagedChatId = currentChatId;

            var older = currentChatType === 1
                ? BF.api.listPrivateMessages(pagedChatId, oldestId, 30, 0).then(function (d) {
                    return decryptPrivateBatch(pagedChatId, d && d.messages).then(function (mapped) {
                        mapped.sort(function (a, b) { return a.id - b.id; });
                        return { messages: mapped };
                    });
                })
                : BF.api.listMessages(pagedChatId, oldestId, 30, 0);

            older.then(function (data) {
                if (data && data.messages && data.messages.length > 0) {
                    var newMsgs = data.messages.filter(function (m) { return !messages.some(function (em) { return em.id === m.id; }); });
                    if (newMsgs.length === 0) { noMoreOlder = true; }
                    else {
                        messages = newMsgs.concat(messages);
                        return renderMessages().then(function () {
                            messagesArea.scrollTop = messagesArea.scrollHeight - prevHeight;
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

        sendBtn.disabled = true;
        var sentChatId = currentChatId;
        var replyId = pendingReply ? pendingReply.messageId : 0;

        BF.api.sendMessage({ chatId: sentChatId, text: text, fileIds: null, forwardedMessageId: replyId }).then(function (resp) {
            messageInput.value = '';
            messageInput.style.height = 'auto';
            sendBtn.disabled = false;
            messageInput.focus();
            clearPendingReply();

            if (resp && resp.message) {
                var msg = resp.message;
                BF.sound.play('tick');
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
        }).catch(function () { sendBtn.disabled = false; });
    }

    function sendMessageWithFiles(files, asDocuments, caption) {
        if (pendingEdit) {
            // Во время редактирования attach-flow заблокирован, чтобы не отправить новое сообщение
            // вместо правки исходного. Завершите или отмените редактирование.
            return;
        }
        stopTypingSend(true);
        var text = (caption != null ? caption : messageInput.value).trim();
        var sentChatId = currentChatId;
        sendBtn.disabled = true;

        var localId = 'pending-upload-' + Date.now() + '-' + (++pendingUploadCounter);
        var previewUrls = [];
        var localAttachments = files.map(function (file, index) {
            var uploadType = BF.files.getUploadFileType(file.type, asDocuments, file.name);
            var isImage = !asDocuments && (uploadType === IMAGE_UPLOAD_TYPE || uploadType === GIF_UPLOAD_TYPE);
            var previewUrl = isImage ? URL.createObjectURL(file) : '';
            if (previewUrl) previewUrls.push(previewUrl);
            return {
                type: isImage ? (uploadType === GIF_UPLOAD_TYPE ? 'GIF' : 'IMAGE') : 'DOCUMENT',
                fileId: '',
                fileName: file.name,
                attachmentSize: file.size,
                localPreviewUrl: previewUrl,
                uploadProgress: 0,
                uploadIndex: index,
                isPending: true
            };
        });
        var pendingMessage = {
            id: localId,
            senderId: myUserId,
            readBy: [],
            sentAt: Date.now(),
            type: GENERIC_MESSAGE_TYPE,
            isPending: true,
            content: { text: text || '', attachments: localAttachments }
        };
        var pendingEntry = {
            localId: localId,
            chatId: sentChatId,
            localMessage: pendingMessage,
            fileIds: [],
            previewUrls: previewUrls,
            settled: false
        };
        pendingUploads.set(localId, pendingEntry);

        if (String(sentChatId) === String(currentChatId)) {
            messages.push(pendingMessage);
            appendMessageToView(pendingMessage).then(scrollToBottom);
        }

        var uploadChain = files.reduce(function (chain, file, index) {
            return chain.then(function (ids) {
                var uploadType = BF.files.getUploadFileType(file.type, asDocuments, file.name);
                return BF.files.uploadFile(file, uploadType, function (progress) {
                    localAttachments[index].uploadProgress = progress;
                    BF.messages.updateAttachmentProgress(localId, index, progress);
                })
                    .then(function (fileId) {
                        localAttachments[index].fileId = fileId;
                        pendingEntry.fileIds.push(fileId);
                        ids.push(fileId);
                        return ids;
                    });
            });
        }, Promise.resolve([]));

        var replyId = pendingReply ? pendingReply.messageId : 0;
        var uploadsComplete = false;
        uploadChain.then(function (fileIds) {
            if (fileIds.length === 0) {
                removePendingUpload(pendingEntry);
                sendBtn.disabled = false;
                return;
            }
            uploadsComplete = true;
            return BF.api.sendMessage({
                chatId: sentChatId,
                text: text || null,
                fileIds: fileIds,
                forwardedMessageId: replyId
            }).then(function (resp) {
                messageInput.value = '';
                messageInput.style.height = 'auto';
                sendBtn.disabled = false;
                messageInput.focus();
                clearPendingReply();
                if (resp && resp.message) {
                    var msg = resp.message;
                    reconcilePendingUpload(sentChatId, msg, pendingEntry);
                    var chatIdx = chats.findIndex(function (c) { return c.id === sentChatId; });
                    if (chatIdx >= 0) {
                        var chat = chats[chatIdx];
                        chat.lastMessage = msg;
                        chats.splice(chatIdx, 1);
                        chats.unshift(chat);
                        renderChatList();
                    }
                } else {
                    removePendingUpload(pendingEntry);
                    groupToast('Не удалось отправить сообщение');
                }
            });
        }).catch(function () {
            removePendingUpload(pendingEntry);
            sendBtn.disabled = false;
            groupToast(uploadsComplete ? 'Не удалось отправить сообщение' : 'Не удалось загрузить вложение');
        });
    }

    function openAttachModal(files) {
        if (!currentChatId || currentChatType === 1) return; // в приватных чатах вложения не поддерживаются
        var prefill = messageInput.value;
        BF.attach.open(files, function (outFiles, asDocuments, caption) {
            // Если пользователь ввёл подпись в модалке — забираем её из неё, а исходный
            // ввод в чате очищаем, чтобы текст не отправился ещё раз отдельным сообщением.
            messageInput.value = '';
            messageInput.style.height = 'auto';
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

    attachBtn.addEventListener('click', function () { fileInput.click(); });

    fileInput.addEventListener('change', function () {
        var files = Array.from(fileInput.files);
        fileInput.value = '';
        if (files.length === 0) return;
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
        dropOverlay.textContent = 'Отпустите для отправки';
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

    // ========== PRIVATE CHATS (E2E через passphrase, зеркалит Android) ==========

    var privatePassOverlay = $('#privatePassOverlay');
    var privatePassTitle = $('#privatePassTitle');
    var privatePassInput = $('#privatePassInput');
    var privatePassRemember = $('#privatePassRemember');
    var privatePassError = $('#privatePassError');
    var privatePassCancel = $('#privatePassCancel');
    var privatePassOk = $('#privatePassOk');
    var privatePassActive = null; // { chat, onDone } — текущий запрос пароля

    function closePassphraseModal() {
        privatePassOverlay.classList.remove('visible');
        privatePassActive = null;
    }

    // Запрос passphrase с локальной проверкой verifier'а (Argon2id → HMAC).
    // onDone(key, remember) вызывается только после успешной проверки.
    function promptPassphrase(chat, title, onDone) {
        var ctx = { chat: chat, onDone: onDone };
        privatePassActive = ctx;
        privatePassTitle.textContent = title;
        privatePassInput.value = '';
        privatePassRemember.checked = true;
        privatePassError.textContent = '';
        privatePassOk.disabled = false;
        privatePassOverlay.classList.add('visible');
        setTimeout(function () { privatePassInput.focus(); }, 50);
    }

    function submitPassphrase() {
        if (!privatePassActive) return;
        var ctx = privatePassActive;
        var pass = privatePassInput.value;
        if (!pass) { privatePassError.textContent = 'Введите пароль'; return; }
        privatePassOk.disabled = true;
        privatePassError.textContent = 'Проверка…';
        BF.privateChat.deriveKey(pass, ctx.chat.kdfSalt).then(function (key) {
            return BF.privateChat.validateVerifier(key, ctx.chat.passphraseVerifier).then(function (ok) {
                if (privatePassActive !== ctx) return;
                if (!ok) {
                    privatePassOk.disabled = false;
                    privatePassError.textContent = 'Неверный пароль';
                    return;
                }
                var remember = privatePassRemember.checked;
                closePassphraseModal();
                ctx.onDone(key, remember);
            });
        }).catch(function (e) {
            console.error('[privateChat] deriveKey failed', e);
            if (privatePassActive !== ctx) return;
            privatePassOk.disabled = false;
            privatePassError.textContent = 'Ошибка проверки пароля';
        });
    }

    if (privatePassOk) privatePassOk.addEventListener('click', submitPassphrase);
    if (privatePassCancel) privatePassCancel.addEventListener('click', closePassphraseModal);
    if (privatePassInput) privatePassInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') { e.preventDefault(); submitPassphrase(); }
    });

    // Карточка-статус в области сообщений (инвайт/ожидание/разблокировка)
    function showPrivateCard(title, text, buttons) {
        messagesInner.innerHTML = '';
        var card = document.createElement('div');
        card.className = 'private-card';
        var h = document.createElement('div');
        h.className = 'private-card-title';
        h.textContent = title;
        card.appendChild(h);
        if (text) {
            var p = document.createElement('div');
            p.className = 'private-card-text';
            p.textContent = text;
            card.appendChild(p);
        }
        if (buttons && buttons.length) {
            var row = document.createElement('div');
            row.className = 'private-card-actions';
            buttons.forEach(function (b) {
                var btn = document.createElement('button');
                btn.className = 'private-card-btn' + (b.primary ? ' primary' : '');
                btn.textContent = b.label;
                btn.addEventListener('click', b.onClick);
                row.appendChild(btn);
            });
            card.appendChild(row);
        }
        messagesInner.appendChild(card);
    }

    // EncryptedMessage (+расшифрованный текст) → объект сообщения для BF.messages
    function privateToUiMessage(chatId, enc, text) {
        return {
            id: enc.id,
            senderId: enc.senderId,
            readBy: [],
            sentAt: enc.sentAt,
            type: 1,
            isEdited: enc.isEdited,
            editedAt: enc.editedAt,
            content: {
                text: (text !== null && text !== undefined) ? text : '\u{1F512} Не удалось расшифровать',
                attachments: []
            }
        };
    }

    function decryptPrivateBatch(chatId, encMsgs) {
        var alive = (encMsgs || []).filter(function (m) { return !m.isDeleted; });
        return Promise.all(alive.map(function (m) {
            return BF.privateChat.decryptMessage(chatId, m).then(function (t) {
                return privateToUiMessage(chatId, m, t);
            });
        }));
    }

    function openPrivateChat(chat) {
        stopTypingSend(true);
        if (BF.pinned && BF.pinned.closeForChat) BF.pinned.closeForChat();

        currentChatId = chat.id;
        currentChatInfo = null;
        currentChatType = 1;
        currentChatPeerIsBot = false;
        BF.realtime.unsubscribeTyping();
        messages = [];
        noMoreOlder = false;
        knownMessageIds = new Set();
        clearPendingReply();
        clearPendingEdit();
        closeContextMenu();
        if (scrollToBottomBtn) scrollToBottomBtn.classList.remove('visible');
        chatEmpty.style.display = 'none';
        chatHeader.classList.add('visible');
        messagesArea.parentElement.classList.add('visible');
        messagesArea.classList.add('visible');
        messagesInner.innerHTML = '';
        inputBar.classList.remove('visible');
        inputBar.classList.add('private-chat');
        loadingMessages.classList.remove('visible');
        setChatCallButtonsVisible(false);
        resetChatTabContext();

        var privatePeer = (chat.members || []).find(function (member) { return member.userId !== myUserId; });
        if (privatePeer) {
            getUser(privatePeer.userId).then(function (peer) {
                if (currentChatId !== chat.id || !peer) return;
                setChatTabContext(chatTabTitle(peer), peer.profilePicturePreview || peer.profilePicture || chat.picture || null);
            }).catch(function () {});
        }

        chatHeaderName.textContent = '\u{1F512} ' + (chat.title || 'Приватный чат');
        if (chat.picture) chatHeaderAvatar.innerHTML = '<img src="' + u.escapeHtml(chat.picture) + '" alt="">';
        else chatHeaderAvatar.textContent = (chat.title || '?')[0].toUpperCase();
        chatHeaderStatus.hidden = false;
        chatHeaderStatus.classList.remove('online');
        chatHeaderStatus.textContent = 'Приватный чат';

        if (chat.countUnread > 0) { chat.countUnread = 0; updateTitleBadge(); }
        renderChatList();

        if (chat.privateInviteState === 2) { // REJECTED
            showPrivateCard('Приглашение отклонено', 'Этот приватный чат недоступен.');
            return;
        }
        if (chat.privateInviteState === 0) { // PENDING
            if (chat.privateInviterUserId === myUserId || !chat.privateInviterUserId) {
                showPrivateCard('Ожидание собеседника', 'Собеседник ещё не принял приглашение в приватный чат.');
            } else {
                showPrivateInviteCard(chat);
            }
            return;
        }
        // ACCEPTED
        if (BF.privateChat.hasKey(chat.id)) {
            loadPrivateMessages(chat);
        } else {
            showPrivateUnlockCard(chat);
        }
    }

    function showPrivateInviteCard(chat) {
        showPrivateCard('Приглашение в приватный чат',
            'Для входа нужен пароль, о котором вы договорились с собеседником.', [
            { label: 'Отклонить', onClick: function () { rejectPrivateInvite(chat); } },
            { label: 'Принять', primary: true, onClick: function () { acceptPrivateInvite(chat); } }
        ]);
    }

    function showPrivateUnlockCard(chat) {
        showPrivateCard('Чат заблокирован',
            'Введите пароль чата, чтобы расшифровать сообщения на этом устройстве.', [
            { label: 'Ввести пароль', primary: true, onClick: function () {
                promptPassphrase(chat, 'Пароль приватного чата', function (key, remember) {
                    BF.privateChat.saveKey(chat.id, key, remember);
                    if (currentChatId === chat.id) loadPrivateMessages(chat);
                });
            } }
        ]);
    }

    function acceptPrivateInvite(chat) {
        promptPassphrase(chat, 'Пароль приватного чата', function (key, remember) {
            BF.api.acceptPrivateChat(chat.id).then(function (resp) {
                BF.privateChat.saveKey(chat.id, key, remember);
                var idx = chats.findIndex(function (c) { return c.id === chat.id; });
                var updated = (resp && resp.chat) ? resp.chat : chat;
                updated.privateInviteState = 1;
                updated.countUnread = 0;
                if (idx >= 0) chats[idx] = updated;
                renderChatList();
                if (currentChatId === chat.id) loadPrivateMessages(updated);
            }).catch(function (e) {
                console.error('[privateChat] acceptPrivateChat failed', e);
                if (currentChatId === chat.id) showPrivateInviteCard(chat);
            });
        });
    }

    function rejectPrivateInvite(chat) {
        BF.api.rejectPrivateChat(chat.id).then(function () {
            var idx = chats.findIndex(function (c) { return c.id === chat.id; });
            if (idx >= 0) chats.splice(idx, 1);
            renderChatList();
            updateTitleBadge();
            if (currentChatId === chat.id) closePrivateChatView();
        }).catch(function (e) { console.error('[privateChat] rejectPrivateChat failed', e); });
    }

    function closePrivateChatView() {
        currentChatId = null;
        currentChatType = 0;
        messages = [];
        messagesInner.innerHTML = '';
        chatHeader.classList.remove('visible');
        messagesArea.classList.remove('visible');
        messagesArea.parentElement.classList.remove('visible');
        inputBar.classList.remove('visible');
        chatEmpty.style.display = '';
        resetChatTabContext();
    }

    function loadPrivateMessages(chat) {
        var chatId = chat.id;
        messagesInner.innerHTML = '';
        loadingMessages.classList.add('visible');
        return BF.api.listPrivateMessages(chatId, 0, 50, 0).then(function (data) {
            if (chatId !== currentChatId) return;
            return decryptPrivateBatch(chatId, data && data.messages).then(function (mapped) {
                if (chatId !== currentChatId) return;
                mapped.sort(function (a, b) { return a.id - b.id; });
                messages = mapped;
                inputBar.classList.add('visible');
                renderMessages().then(scrollToBottom);
                var last = mapped.length ? mapped[mapped.length - 1].id : 0;
                if (last) BF.api.markPrivateMessagesAsRead(chatId, last).catch(function () {});
            });
        }).catch(function (e) {
            console.error('[privateChat] listPrivateMessages failed', e);
            return false;
        }).finally(function () {
            loadingMessages.classList.remove('visible');
        });
    }

    // Catch-up открытого приватного чата (реконнект стрима / возврат на вкладку)
    function reloadCurrentPrivateChat() {
        if (currentChatType !== 1 || !currentChatId) return Promise.resolve(true);
        var chat = chats.find(function (c) { return c.id === currentChatId; });
        if (chat && chat.privateInviteState === 1 && BF.privateChat.hasKey(chat.id)) {
            return loadPrivateMessages(chat).then(function (result) { return result !== false; });
        }
        return Promise.resolve(true);
    }

    function sendPrivateMessageFlow(text) {
        var sentChatId = currentChatId;
        sendBtn.disabled = true;
        BF.privateChat.encryptText(sentChatId, text).then(function (encd) {
            return BF.api.sendPrivateMessage(sentChatId, encd.ciphertext, encd.nonce, encd.associatedData);
        }).then(function (resp) {
            messageInput.value = '';
            messageInput.style.height = 'auto';
            sendBtn.disabled = false;
            messageInput.focus();
            if (resp && resp.message) {
                var msg = privateToUiMessage(sentChatId, resp.message, text);
                BF.sound.play('tick');
                if (sentChatId === currentChatId && !messages.some(function (m) { return m.id === msg.id; })) {
                    messages.push(msg);
                    appendMessageToView(msg).then(scrollToBottom);
                }
                var chatIdx = chats.findIndex(function (c) { return c.id === sentChatId; });
                if (chatIdx >= 0) {
                    var chat = chats[chatIdx];
                    chat.lastActivityAt = msg.sentAt || Date.now();
                    chats.splice(chatIdx, 1);
                    chats.unshift(chat);
                    renderChatList();
                }
            }
        }).catch(function (e) {
            console.error('[privateChat] send failed', e);
            sendBtn.disabled = false;
        });
    }

    BF.realtime.on('private_message', function (data) {
        handlePrivateMessage(data.chatId, data.message);
    });

    function handlePrivateMessage(chatId, enc) {
        var chat = chats.find(function (c) { return c.id === chatId; });
        if (chat) {
            chat.lastActivityAt = enc.sentAt || Date.now();
            if (chatId !== currentChatId && enc.senderId !== myUserId) {
                chat.countUnread = (chat.countUnread || 0) + 1;
            }
            var idx = chats.indexOf(chat);
            chats.splice(idx, 1);
            chats.unshift(chat);
            renderChatList();
        } else {
            // Неизвестный чат (например, свежий инвайт) — перечитываем список
            loadChats(true);
        }
        updateTitleBadge();

        if (enc.senderId !== myUserId) {
            BF.sound.play('chime');
            showNewMessageNotification(chat ? chat.title : 'Приватный чат',
                { id: enc.id, chatId: chatId, content: { text: '\u{1F512} Новое сообщение' } });
        }

        if (chatId !== currentChatId) return;
        if (enc.isDeleted) return;
        if (!BF.privateChat.hasKey(chatId)) return; // чат ещё не разблокирован
        if (messages.some(function (m) { return m.id === enc.id; })) return;
        BF.privateChat.decryptMessage(chatId, enc).then(function (text) {
            if (chatId !== currentChatId) return;
            if (messages.some(function (m) { return m.id === enc.id; })) return;
            var msg = privateToUiMessage(chatId, enc, text);
            var isAtBottom = messagesArea.scrollHeight - messagesArea.scrollTop - messagesArea.clientHeight < 300;
            messages.push(msg);
            appendMessageToView(msg).then(function () {
                if (isAtBottom) scrollToBottom();
                else if (scrollToBottomBtn) scrollToBottomBtn.classList.add('visible');
            });
            if (enc.senderId !== myUserId) {
                BF.api.markPrivateMessagesAsRead(chatId, enc.id).catch(function () {});
            }
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
        BF.api.markAsRead(ids).catch(function () {});
    }

    // ========== TITLE UNREAD BADGE ==========

    var defaultBaseTitle = 'Мессенджер';
    var baseTitle = defaultBaseTitle;

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
        baseTitle = defaultBaseTitle;
        setFavicon(null);
        updateTitleBadge();
    }

    function setChatTabContext(title, faviconHref) {
        baseTitle = title || defaultBaseTitle;
        setFavicon(faviconHref || null);
        updateTitleBadge();
    }

    function updateTitleBadge() {
        var total = 0;
        chats.forEach(function (c) { total += (c.countUnread || 0); });
        document.title = total > 0 ? '(' + (total > 99 ? '99+' : total) + ') ' + baseTitle : baseTitle;
    }

    // ========== BROWSER NOTIFICATIONS ==========

    var notificationsAllowed = false;

    function requestNotificationPermission() {
        if (!('Notification' in window)) return;
        if (Notification.permission === 'granted') { notificationsAllowed = true; return; }
        if (Notification.permission !== 'denied') {
            Notification.requestPermission().then(function (perm) {
                notificationsAllowed = (perm === 'granted');
            });
        }
    }

    function showNewMessageNotification(chatTitle, msg) {
        if (!notificationsAllowed) return;
        if (document.visibilityState === 'visible' && msg.chatId === currentChatId) return;

        var body = '';
        if (msg.content && msg.content.text) body = u.truncate(msg.content.text, 80);
        else if (msg.content && msg.content.attachments && msg.content.attachments.length > 0) {
            body = u.attachmentEmoji(msg.content.attachments[0].type);
        }

        try {
            var n = new Notification(chatTitle || 'Новое сообщение', {
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
            connectionBannerText.textContent = offline ? 'Нет сети' : 'Переподключаемся…';
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
            if (!tailOnly) {
                messages = fetched;
                mergePendingUploadsIntoMessages(chatId);
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
            diff.news.forEach(function (m) {
                chain = chain.then(function () {
                    if (chatId !== currentChatId) return;
                    if (reconcilePendingUpload(chatId, m)) return;
                    messages.push(m);
                    return appendMessageToView(m);
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
            chatHeaderStatus.textContent = 'в сети';
        } else {
            chatHeaderStatus.textContent = BF.utils.formatLastSeen(entry ? entry.lastSeen : null);
        }
        chatHeaderStatus.classList.toggle('online', online);
    }

    function renderTypingIndicator() {
        if (!currentChatId || !currentChatInfo) return;

        if (typingUsers.size === 0) {
            if (currentChatInfo.isGroupChat) {
                chatHeaderStatus.textContent = (currentChatInfo.membersId ? currentChatInfo.membersId.length : 0) + ' участников';
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
                if (!user) return 'Кто-то';
                return (user.firstName || '').split(' ')[0] || user.username || 'Кто-то';
            });
            chatHeaderStatus.textContent = names.join(', ') + (typingUsers.size > 1 ? ' печатают…' : ' печатает…');
        } else {
            chatHeaderStatus.textContent = 'печатает…';
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
    searchInput.addEventListener('input', function () {
        clearTimeout(searchTimer);
        var query = searchInput.value.trim();
        if (!query) { searchResults.classList.remove('visible'); searchResults.innerHTML = ''; return; }

        searchTimer = setTimeout(function () {
            BF.api.searchUsers(query, 0, 20).then(function (data) {
                if (!data || !data.users) return;
                searchResults.classList.add('visible');
                searchResults.innerHTML = '';
                data.users.forEach(function (user) {
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
                if (data.users.length === 0) {
                    searchResults.innerHTML = '<div style="padding:16px;text-align:center;color:var(--text-sub);font-size:14px;">Ничего не найдено</div>';
                }
            });
        }, 300);
    });

    // ========== MEDIA OVERLAY ==========
    var overlayFileToken = 0;
    var overlayOpenFrame = null;
    var overlayCloseTimer = null;

    // Выставляет display + src для overlay-элементов; сбрасывает data-флаги resilient,
    // чтобы при новом открытии bindResilientMedia обрабатывал ошибки с чистого старта.
    function applyOverlaySrc(type, url) {
        overlayImage.removeAttribute('data-bf-refreshed');
        overlayImage.removeAttribute('data-bf-failed');
        overlayVideo.removeAttribute('data-bf-refreshed');
        overlayVideo.removeAttribute('data-bf-failed');
        if (type === 'video') {
            overlayImage.style.display = 'none';
            overlayVideo.style.display = 'block';
            overlayVideo.src = url || '';
            if (url) overlayVideo.play();
        } else {
            overlayVideo.style.display = 'none';
            overlayImage.style.display = 'block';
            overlayImage.src = url || '';
        }
    }

    function showMediaOverlay(type, url, fileId) {
        if (overlayCloseTimer) {
            clearTimeout(overlayCloseTimer);
            overlayCloseTimer = null;
        }
        if (overlayOpenFrame) cancelAnimationFrame(overlayOpenFrame);
        if (fileId) {
            overlayImage.setAttribute('data-bf-file-id', fileId);
            overlayVideo.setAttribute('data-bf-file-id', fileId);
        } else {
            overlayImage.removeAttribute('data-bf-file-id');
            overlayVideo.removeAttribute('data-bf-file-id');
        }
        var token = ++overlayFileToken;
        applyOverlaySrc(type, url);
        overlayOpenFrame = requestAnimationFrame(function () {
            overlayOpenFrame = null;
            if (token === overlayFileToken) imageOverlay.classList.add('visible');
        });
        // Presigned-ссылки протухают при долгой сессии. При открытии полноразмерного
        // просмотра перезапрашиваем свежий URL по fileId, чтобы избежать 404.
        if (fileId) {
            BF.files.refreshFileUrl(fileId).then(function (f) {
                if (!f || token !== overlayFileToken) return;
                var fresh = type === 'video' ? f.url : (f.url || f.previewUrl);
                var cur = type === 'video' ? overlayVideo.src : overlayImage.src;
                if (fresh && fresh !== cur) applyOverlaySrc(type, fresh);
            });
        }
        viewerInit(fileId);
    }

    function cleanupMediaOverlay() {
        overlayCloseTimer = null;
        if (imageOverlay.classList.contains('visible')) return;
        overlayImage.removeAttribute('data-bf-file-id');
        overlayVideo.removeAttribute('data-bf-file-id');
        overlayImage.removeAttribute('data-bf-refreshed');
        overlayImage.removeAttribute('data-bf-failed');
        overlayVideo.removeAttribute('data-bf-refreshed');
        overlayVideo.removeAttribute('data-bf-failed');
        overlayImage.src = '';
        overlayVideo.pause();
        overlayVideo.src = '';
        viewerState.index = -1;
        if (overlayPrev) overlayPrev.hidden = true;
        if (overlayNext) overlayNext.hidden = true;
    }

    function closeMediaOverlay() {
        overlayFileToken++;
        if (overlayOpenFrame) {
            cancelAnimationFrame(overlayOpenFrame);
            overlayOpenFrame = null;
        }
        imageOverlay.classList.remove('visible');
        if (overlayCloseTimer) clearTimeout(overlayCloseTimer);
        overlayCloseTimer = setTimeout(cleanupMediaOverlay, 120);
    }

    // ----- Листаемый просмотрщик: картинки + видео всего чата через ListChatAttachments -----
    var VIEWER_PAGE = 30;
    var viewerState = { chatId: null, items: [], index: -1, offset: 0, exhausted: false, totalCount: 0, loading: null };

    function viewerReset() {
        viewerState = { chatId: null, items: [], index: -1, offset: 0, exhausted: false, totalCount: 0, loading: null };
    }

    function viewerItem(a, type) {
        var att = a.attachment || {};
        return { type: type, fileId: att.fileId, attachmentId: a.attachmentId, messageId: a.messageId, sentAt: a.sentAt };
    }

    // Догрузить следующую страницу медиа текущего чата (картинки type=1 + видео type=2).
    function viewerLoadMore() {
        if (viewerState.loading) return viewerState.loading;
        if (viewerState.exhausted) return Promise.resolve();
        var chatId = viewerState.chatId;
        var off = viewerState.offset;
        var p = Promise.all([
            BF.api.listChatAttachments(chatId, 1, off, VIEWER_PAGE),
            BF.api.listChatAttachments(chatId, 2, off, VIEWER_PAGE)
        ]).then(function (res) {
            if (viewerState.chatId !== chatId) return;
            var imgs = res[0].attachments || [];
            var vids = res[1].attachments || [];
            var batch = imgs.map(function (a) { return viewerItem(a, 'image'); })
                .concat(vids.map(function (a) { return viewerItem(a, 'video'); }));
            var seen = {};
            viewerState.items.forEach(function (it) { if (it.fileId) seen[it.fileId] = 1; });
            batch.forEach(function (it) {
                if (it.fileId && !seen[it.fileId]) { seen[it.fileId] = 1; viewerState.items.push(it); }
            });
            viewerState.items.sort(function (a, b) { return (b.sentAt || 0) - (a.sentAt || 0); });
            viewerState.offset += VIEWER_PAGE;
            viewerState.totalCount = (res[0].totalCount || 0) + (res[1].totalCount || 0);
            if (imgs.length < VIEWER_PAGE && vids.length < VIEWER_PAGE) viewerState.exhausted = true;
        }).catch(function () {}).then(function () { viewerState.loading = null; });
        viewerState.loading = p;
        return p;
    }

    function viewerUpdateNav() {
        if (overlayPrev) overlayPrev.hidden = viewerState.index <= 0;
        if (overlayNext) overlayNext.hidden = viewerState.index >= viewerState.items.length - 1 && viewerState.exhausted;
    }

    function viewerShow(index) {
        if (index < 0 || index >= viewerState.items.length) return;
        viewerState.index = index;
        var it = viewerState.items[index];
        var token = ++overlayFileToken;
        if (it.fileId) {
            overlayImage.setAttribute('data-bf-file-id', it.fileId);
            overlayVideo.setAttribute('data-bf-file-id', it.fileId);
        }
        var fd = BF.files.getCachedFileUrl(it.fileId);
        var url = fd && (it.type === 'video' ? fd.url : (fd.url || fd.previewUrl));
        if (url) applyOverlaySrc(it.type, url);
        BF.files.refreshFileUrl(it.fileId).then(function (f) {
            if (!f || token !== overlayFileToken) return;
            var fresh = it.type === 'video' ? f.url : (f.url || f.previewUrl);
            if (fresh) applyOverlaySrc(it.type, fresh);
        });
        viewerUpdateNav();
        if (index >= viewerState.items.length - 2 && !viewerState.exhausted) viewerLoadMore();
    }

    function viewerNav(dir) {
        var ni = viewerState.index + dir;
        if (ni < 0) return;
        if (ni >= viewerState.items.length) {
            if (viewerState.exhausted) return;
            viewerLoadMore().then(function () {
                if (viewerState.index + dir < viewerState.items.length) viewerShow(viewerState.index + dir);
            });
            return;
        }
        viewerShow(ni);
    }

    // Привязать открытый кадр к списку медиа чата: найти его индекс, при необходимости догружая страницы.
    function viewerInit(fileId) {
        var chatId = currentChatId;
        if (!chatId || !fileId) { viewerState.index = -1; viewerUpdateNav(); return; }
        if (viewerState.chatId !== chatId) {
            viewerReset();
            viewerState.chatId = chatId;
        }
        (function locate() {
            for (var i = 0; i < viewerState.items.length; i++) {
                if (viewerState.items[i].fileId === fileId) {
                    viewerState.index = i;
                    viewerUpdateNav();
                    if (i >= viewerState.items.length - 2 && !viewerState.exhausted) viewerLoadMore();
                    return;
                }
            }
            if (viewerState.exhausted) { viewerState.index = -1; viewerUpdateNav(); return; }
            viewerLoadMore().then(locate);
        })();
    }

    BF.files.bindResilientMedia(overlayImage, null, false);
    BF.files.bindResilientMedia(overlayVideo, null, false);

    if (overlayPrev) overlayPrev.addEventListener('click', function (e) { e.stopPropagation(); viewerNav(-1); });
    if (overlayNext) overlayNext.addEventListener('click', function (e) { e.stopPropagation(); viewerNav(1); });
    document.addEventListener('keydown', function (e) {
        if (!imageOverlay.classList.contains('visible')) return;
        if (e.key === 'ArrowLeft') { e.preventDefault(); viewerNav(-1); }
        else if (e.key === 'ArrowRight') { e.preventDefault(); viewerNav(1); }
    });

    imageOverlay.addEventListener('click', function (e) {
        if (e.target === overlayVideo) return;
        closeMediaOverlay();
    });

    // ========== PROFILE OVERLAY ==========

    function openProfile(userId) {
        if (!userId) return;
        currentProfileUserId = userId;

        BF.api.getUser(userId).then(function (d) {
            if (!d || !d.user) return;
            var user = d.user;

            if (profilePoster) {
                profilePoster.classList.remove('visible');
                profilePoster.style.backgroundImage = '';
                if (user.profilePosterFileId) {
                    BF.files.getFileUrls([user.profilePosterFileId]).then(function (urls) {
                        var u = urls && urls[0];
                        var url = u && (u.url || u.previewUrl);
                        if (url) {
                            profilePoster.style.backgroundImage = 'url("' + url + '")';
                            profilePoster.classList.add('visible');
                        }
                    });
                }
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
            profileStatus.textContent = online ? 'в сети' : BF.utils.formatLastSeen(entry ? entry.lastSeen : null);
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
            profileOverlay.classList.add('visible');
        });
    }

    function loadProfileMedia(type) {
        renderChatMedia(type, profileMediaContent);
    }

    function renderChatMedia(type, profileMediaContent) {
        profileMediaContent.innerHTML = '';
        if (!currentChatId) return;

        if (type === 'media') {
            Promise.all([
                BF.api.listChatAttachments(currentChatId, 1, 0, 30),
                BF.api.listChatAttachments(currentChatId, 2, 0, 30),
                BF.api.listChatAttachments(currentChatId, 3, 0, 30)
            ]).then(function (results) {
                var all = (results[0].attachments || []).concat(results[1].attachments || [], results[2].attachments || []);
                all.sort(function (a, b) { return (b.sentAt || 0) - (a.sentAt || 0); });
                if (all.length === 0) { profileMediaContent.innerHTML = '<div class="profile-media-empty">Нет медиафайлов</div>'; return; }

                var grid = document.createElement('div');
                grid.className = 'profile-media-grid';

                var chain = Promise.resolve();
                all.forEach(function (item) {
                    chain = chain.then(function () {
                        var att = item.attachment;
                        if (!att || !att.fileId) return;
                        var urlP = att.previewUrl ? Promise.resolve(att.previewUrl)
                            : BF.files.getFileUrls([att.fileId]).then(function (urls) { return urls[0] ? (urls[0].previewUrl || urls[0].url) : ''; });
                        return urlP.then(function (url) {
                            if (!url) return;
                            var img = document.createElement('img');
                            img.src = url; img.loading = 'lazy';
                            BF.files.bindResilientMedia(img, att.fileId, true);
                            img.addEventListener('click', function () { showMediaOverlay(att.type === 'VIDEO' ? 'video' : 'image', url, att.fileId); });
                            grid.appendChild(img);
                        });
                    });
                });
                chain.then(function () { profileMediaContent.appendChild(grid); });
            });
        } else if (type === 'files') {
            BF.api.listChatAttachments(currentChatId, 4, 0, 30).then(function (data) {
                var files = data.attachments || [];
                if (files.length === 0) { profileMediaContent.innerHTML = '<div class="profile-media-empty">Нет файлов</div>'; return; }
                var list = document.createElement('div');
                list.className = 'profile-file-list';

                var chain = Promise.resolve();
                files.forEach(function (item) {
                    chain = chain.then(function () {
                        var att = item.attachment;
                        if (!att || !att.fileId) return;
                        return BF.files.getFileUrls([att.fileId]).then(function (urls) {
                            var fileUrl = urls[0] ? urls[0].url : '#';
                            var el = document.createElement('a');
                            el.className = 'profile-file-item';
                            el.href = fileUrl; el.target = '_blank';
                            BF.files.bindResilientLink(el, att.fileId);
                            el.rel = 'noopener';
                            var icon = document.createElement('span');
                            icon.textContent = '\u{1F4C4}';
                            el.appendChild(icon);
                            el.appendChild(document.createTextNode(' ' + (att.fileName || 'Файл')));
                            list.appendChild(el);
                        });
                    });
                });
                chain.then(function () { profileMediaContent.appendChild(list); });
            });
        } else if (type === 'audio' || type === 'voice') {
            var attType = type === 'audio' ? 5 : 6;
            var emptyText = type === 'audio' ? 'Нет аудио' : 'Нет голосовых';
            BF.api.listChatAttachments(currentChatId, attType, 0, 30).then(function (data) {
                var items = data.attachments || [];
                if (items.length === 0) { profileMediaContent.innerHTML = '<div class="profile-media-empty">' + emptyText + '</div>'; return; }
                var list = document.createElement('div');
                list.className = 'profile-file-list';

                var chain = Promise.resolve();
                items.forEach(function (item) {
                    chain = chain.then(function () {
                        var att = item.attachment;
                        if (!att || !att.fileId) return;
                        return BF.files.getFileUrls([att.fileId]).then(function (urls) {
                            var fileUrl = urls[0] ? urls[0].url : '';
                            if (!fileUrl) return;
                            var el = document.createElement('div');
                            el.className = 'profile-audio-item';
                            if (type === 'audio') {
                                var nm = document.createElement('div');
                                nm.className = 'profile-audio-name';
                                nm.textContent = att.fileName || 'Аудио';
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
                chain.then(function () { profileMediaContent.appendChild(list); });
            });
        }
    }

    document.querySelectorAll('.profile-media-tab').forEach(function (tab) {
        tab.addEventListener('click', function () {
            document.querySelectorAll('.profile-media-tab').forEach(function (t) { t.classList.remove('active'); });
            tab.classList.add('active');
            loadProfileMedia(tab.dataset.type);
        });
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
        BF.calls.initiate(target, media)
            .catch(function (e) { console.error('Не удалось начать звонок:', e); })
            .finally(function () { isInitiatingCall = false; });
    }
    var _btnCallAudio = $('#btnCallAudio');
    var _btnCallVideo = $('#btnCallVideo');
    if (_btnCallAudio) _btnCallAudio.addEventListener('click', function () { startCall(BF.calls.MediaType.AUDIO); });
    if (_btnCallVideo) _btnCallVideo.addEventListener('click', function () { startCall(BF.calls.MediaType.VIDEO); });

    profileClose.addEventListener('click', function () { profileOverlay.classList.remove('visible'); });
    profileOverlay.addEventListener('click', function (e) { if (e.target === profileOverlay) profileOverlay.classList.remove('visible'); });

    var _profileMsgBtn = $('#profileMsgBtn');
    var _profileCallAudioBtn = $('#profileCallAudioBtn');
    var _profileCallVideoBtn = $('#profileCallVideoBtn');
    if (_profileMsgBtn) _profileMsgBtn.addEventListener('click', function () { profileOverlay.classList.remove('visible'); });
    if (_profileCallAudioBtn) _profileCallAudioBtn.addEventListener('click', function () { startCall(BF.calls.MediaType.AUDIO); });
    if (_profileCallVideoBtn) _profileCallVideoBtn.addEventListener('click', function () { startCall(BF.calls.MediaType.VIDEO); });

    function copyText(text) {
        if (!text || !navigator.clipboard) return;
        navigator.clipboard.writeText(String(text)).then(function () {
            BF.sound.play('success');
            groupToast('Скопировано');
        }).catch(function () {});
    }
    document.querySelectorAll('.profile-info-copy').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var target = document.getElementById(btn.dataset.copy);
            if (target) copyText(target.textContent);
        });
    });

    // ========== GROUP INFO PANEL ==========

    function groupToast(text) {
        if (!soonToastEl) return;
        soonToastEl.textContent = text;
        soonToastEl.classList.add('visible');
        if (groupToast._t) clearTimeout(groupToast._t);
        groupToast._t = setTimeout(function () {
            soonToastEl.classList.remove('visible');
            soonToastEl.textContent = 'Скоро будет';
        }, 1800);
    }

    function renderGroupAvatar(picture, title) {
        if (picture) {
            var img = document.createElement('img');
            img.src = picture; img.alt = '';
            groupAvatar.replaceChildren(img);
        } else {
            groupAvatar.textContent = (title || '?')[0].toUpperCase();
        }
    }

    function openGroupInfo() {
        if (!currentChatInfo || !currentChatId) return;
        groupName.textContent = currentChatInfo.title || 'Группа';
        var _groupChatId = $('#groupChatId');
        if (_groupChatId) _groupChatId.textContent = currentChatId || '—';
        renderGroupAvatar(currentChatInfo.picture, currentChatInfo.title);
        groupAddBox.classList.add('hidden');
        groupAddInput.value = '';
        groupAddResults.innerHTML = '';
        loadGroupMembers();
        document.querySelectorAll('.group-media-tab').forEach(function (t, i) { t.classList.toggle('active', i === 0); });
        renderChatMedia('media', groupMediaContent);
        groupOverlay.classList.add('visible');
    }

    function loadGroupMembers() {
        var chatId = currentChatId;
        groupMembersEl.innerHTML = '';
        BF.api.listChatMembers(chatId).then(function (data) {
            if (chatId !== currentChatId) return;
            var members = (data && data.members) || [];
            groupCount.textContent = members.length + ' участников';
            members.forEach(function (m) {
                var fullName = ((m.firstName || '') + ' ' + (m.lastName || '')).trim() || ('ID ' + m.userId);

                var row = document.createElement('div');
                row.className = 'group-member';

                var av = document.createElement('div');
                av.className = 'group-member-avatar';
                av.textContent = (fullName || '?')[0].toUpperCase();
                row.appendChild(av);

                var nm = document.createElement('div');
                nm.className = 'group-member-name';
                nm.textContent = m.userId === myUserId ? (fullName + ' (вы)') : fullName;
                row.appendChild(nm);

                if (m.userId !== myUserId) {
                    var rm = document.createElement('button');
                    rm.className = 'group-member-remove';
                    rm.innerHTML = '&times;';
                    rm.title = 'Удалить';
                    rm.addEventListener('click', function () { confirmRemoveMember(m, fullName); });
                    row.appendChild(rm);
                }

                groupMembersEl.appendChild(row);

                getUser(m.userId).then(function (user) {
                    if (!user) return;
                    var pic = user.profilePicturePreview || user.profilePicture;
                    if (pic) {
                        var img = document.createElement('img');
                        img.src = pic; img.alt = '';
                        av.replaceChildren(img);
                    }
                }).catch(function () {});
            });
        }).catch(function () { groupToast('Ошибка загрузки участников'); });
    }

    function confirmRemoveMember(member, name) {
        if (!window.confirm('Удалить ' + name + ' из группы?')) return;
        BF.api.kickUser(currentChatId, member.userId)
            .then(function () { loadGroupMembers(); })
            .catch(function () { groupToast('Не удалось удалить участника'); });
    }

    function renameGroup() {
        var current = currentChatInfo ? currentChatInfo.title : '';
        var next = window.prompt('Название группы', current || '');
        if (next == null) return;
        next = next.trim();
        if (!next) { groupToast('Название не может быть пустым'); return; }
        BF.api.updateGroupChat(currentChatId, next, null).then(function (res) {
            var title = (res && res.chat && res.chat.title) || next;
            if (currentChatInfo) currentChatInfo.title = title;
            groupName.textContent = title;
            chatHeaderName.textContent = title;
            var c = chats.find(function (x) { return x.id === currentChatId; });
            if (c) { c.title = title; renderChatList(); }
            groupToast('Название обновлено');
        }).catch(function () { groupToast('Не удалось изменить название'); });
    }

    function addGroupMember(userId, name) {
        BF.api.addUser(currentChatId, userId).then(function () {
            groupAddBox.classList.add('hidden');
            groupAddInput.value = '';
            groupAddResults.innerHTML = '';
            loadGroupMembers();
            groupToast(name + ' добавлен(а)');
        }).catch(function () { groupToast('Не удалось добавить участника'); });
    }

    groupClose.addEventListener('click', function () { groupOverlay.classList.remove('visible'); });
    groupOverlay.addEventListener('click', function (e) { if (e.target === groupOverlay) groupOverlay.classList.remove('visible'); });
    groupNameEdit.addEventListener('click', renameGroup);

    groupAvatarEdit.addEventListener('click', function () { groupAvatarInput.click(); });
    groupAvatarInput.addEventListener('change', function () {
        var file = groupAvatarInput.files[0];
        groupAvatarInput.value = '';
        if (!file) return;
        groupToast('Загрузка…');
        BF.files.uploadFile(file, 6 /* CHAT_PICTURE */).then(function (fileId) {
            return BF.api.updateGroupChat(currentChatId, null, fileId);
        }).then(function (res) {
            var pic = res && res.chat && res.chat.picture;
            if (pic) {
                if (currentChatInfo) currentChatInfo.picture = pic;
                renderGroupAvatar(pic, currentChatInfo && currentChatInfo.title);
                chatHeaderAvatar.innerHTML = '<img src="' + u.escapeHtml(pic) + '" alt="">';
                var c = chats.find(function (x) { return x.id === currentChatId; });
                if (c) { c.picture = pic; renderChatList(); }
            }
            groupToast('Аватар обновлён');
        }).catch(function () { groupToast('Не удалось обновить аватар'); });
    });

    groupAddBtn.addEventListener('click', function () {
        groupAddBox.classList.toggle('hidden');
        if (!groupAddBox.classList.contains('hidden')) groupAddInput.focus();
    });

    var groupSearchTimer = null;
    groupAddInput.addEventListener('input', function () {
        var q = groupAddInput.value.trim();
        if (groupSearchTimer) clearTimeout(groupSearchTimer);
        if (!q) { groupAddResults.innerHTML = ''; return; }
        groupSearchTimer = setTimeout(function () {
            BF.api.searchUsers(q, 0, 20).then(function (data) {
                groupAddResults.innerHTML = '';
                (data.users || []).forEach(function (user) {
                    var fullName = ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || user.username;
                    var row = document.createElement('div');
                    row.className = 'group-add-result';

                    var av = document.createElement('div');
                    av.className = 'group-member-avatar';
                    var pic = user.profilePicturePreview || user.profilePicture;
                    if (pic) {
                        var img = document.createElement('img');
                        img.src = pic; img.alt = '';
                        av.appendChild(img);
                    } else { av.textContent = (fullName || '?')[0].toUpperCase(); }
                    row.appendChild(av);

                    var nm = document.createElement('div');
                    nm.className = 'group-member-name';
                    nm.textContent = fullName;
                    row.appendChild(nm);

                    row.addEventListener('click', function () { addGroupMember(user.id, fullName); });
                    groupAddResults.appendChild(row);
                });
            }).catch(function () {});
        }, 300);
    });

    document.querySelectorAll('.group-media-tab').forEach(function (tab) {
        tab.addEventListener('click', function () {
            document.querySelectorAll('.group-media-tab').forEach(function (t) { t.classList.remove('active'); });
            tab.classList.add('active');
            renderChatMedia(tab.dataset.type, groupMediaContent);
        });
    });

    // ========== SCROLL TO BOTTOM BUTTON ==========

    if (scrollToBottomBtn) {
        scrollToBottomBtn.addEventListener('click', function () {
            scrollToBottom();
            scrollToBottomBtn.classList.remove('visible');
        });
    }

    // ========== SETTINGS MODAL ==========

    BF.settings.init({ myUserId: myUserId });
    BF.attach.init();
    if (BF.imageEditor) BF.imageEditor.init();
    $('#navChats').addEventListener('click', function () { /* already on chats page */ });
    $('#navSettings').addEventListener('click', function () { BF.settings.open(); });

    // ========== STICKER PICKER ==========

    var stickerPacksCache = null;
    var stickerPacksContentCache = {}; // packId → { stickers, coverFileId }
    var currentStickerPackId = null;

    if (stickerBtn) {
        stickerBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            var isOpen = stickerPicker.classList.contains('visible');
            stickerPicker.classList.toggle('visible', !isOpen);
            stickerBtn.classList.toggle('active', !isOpen);
            if (!isOpen) loadStickerPacks();
        });
    }

    document.addEventListener('click', function (e) {
        if (!stickerPicker || !stickerPicker.classList.contains('visible')) return;
        if (!stickerPicker.contains(e.target) && !stickerBtn.contains(e.target)) {
            stickerPicker.classList.remove('visible');
            stickerBtn.classList.remove('active');
        }
    });

    function loadStickerPacks() {
        if (stickerPacksCache) { renderStickerPackTabs(); return; }
        BF.api.listStickerPacks(0, 50).then(function (data) {
            stickerPacksCache = data.packs || [];
            if (stickerPacksCache.length === 0) {
                if (stickerGrid) stickerGrid.innerHTML = '<div class="sticker-pack-empty">Стикерпаки не найдены</div>';
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
                if (stickerPacksCache.length > 0) loadStickerPackContent(stickerPacksCache[0].id);
            });
        }).catch(function () {
            if (stickerGrid) stickerGrid.innerHTML = '<div class="sticker-pack-empty">Ошибка загрузки</div>';
        });
    }

    function renderStickerPackTabs() {
        if (!stickerPacksBar) return;
        stickerPacksBar.innerHTML = '';
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
            tab.addEventListener('click', function () { loadStickerPackContent(pack.id); });
            stickerPacksBar.appendChild(tab);
        });
    }

    function loadStickerPackContent(packId) {
        currentStickerPackId = packId;
        if (!stickerGrid) return;
        stickerGrid.innerHTML = '';

        if (stickerPacksBar) {
            stickerPacksBar.querySelectorAll('.sticker-pack-tab').forEach(function (tab, i) {
                tab.classList.toggle('active', stickerPacksCache[i] && stickerPacksCache[i].id === packId);
            });
        }

        var cached = stickerPacksContentCache[packId];
        var stickers = cached ? cached.stickers : [];
        if (stickers.length === 0) {
            stickerGrid.innerHTML = '<div class="sticker-pack-empty">В этом паке нет стикеров</div>';
            return;
        }
        // Показываем full-версии стикеров (fileId, не preview)
        var fileIds = stickers.map(function (s) { return s.fileId; }).filter(Boolean);
        BF.files.getFileUrls(fileIds).then(function () {
            stickers.forEach(function (s) {
                var fd = BF.files.getCachedFileUrl(s.fileId);
                var url = fd && fd.url;
                if (!url) return;
                var img = document.createElement('img');
                img.src = url;
                img.title = s.emoji || '';
                img.loading = 'lazy';
                img.addEventListener('click', function () { sendSticker(s.fileId); });
                BF.files.bindResilientMedia(img, s.fileId, false);
                stickerGrid.appendChild(img);
            });
        });
    }

    function sendSticker(fileId) {
        if (!currentChatId || currentChatType === 1 || !fileId) return;
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
        }).catch(function () {});
    }

    // ========== REPLY / FORWARD / CONTEXT MENU ==========

    function showSoonToast() {
        if (!soonToastEl) return;
        soonToastEl.classList.add('visible');
        if (showSoonToast._t) clearTimeout(showSoonToast._t);
        showSoonToast._t = setTimeout(function () {
            soonToastEl.classList.remove('visible');
        }, 1800);
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

    function setPendingReply(msg) {
        if (!msg) return;
        pendingReply = {
            messageId: msg.id,
            authorName: '',
            previewText: buildReplyPreviewText(msg)
        };
        if (msg.senderId === myUserId) {
            pendingReply.authorName = 'Вы';
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

    function clearPendingReply() {
        pendingReply = null;
        if (replyPreviewBar) replyPreviewBar.classList.remove('visible');
    }

    function setPendingEdit(msg) {
        if (!msg) return;
        clearPendingReply();
        var origText = (msg.content && msg.content.text) || '';
        pendingEdit = { messageId: msg.id, originalText: origText };
        messageInput.value = origText;
        messageInput.style.height = 'auto';
        messageInput.style.height = Math.min(messageInput.scrollHeight, 120) + 'px';
        if (epbText) epbText.textContent = origText || '(вложения)';
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
        deleteMsgConfirmOverlay.classList.add('visible');
        deleteMsgOk.onclick = function () {
            deleteMsgOk.disabled = true;
            BF.api.deleteMessage(messageId).then(function () {
                applyMessageDelete(currentChatId, messageId);
            }).catch(function () {})
            .finally(function () {
                deleteMsgOk.disabled = false;
                deleteMsgConfirmOverlay.classList.remove('visible');
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
        var msgIdStr = String(messageId);
        console.log('[main] applyMessageDelete', { chatId: chatId, messageId: messageId, currentChatId: currentChatId });

        // messageId глобально уникален: ищем и удаляем во всех текущих структурах,
        // не привязываясь к chatId-сравнению (на случай расхождения форматов id).
        var idx = messages.findIndex(function (m) { return Number(m.id) === msgIdNum; });
        if (idx >= 0) messages.splice(idx, 1);
        if (knownMessageIds && typeof knownMessageIds.delete === 'function') {
            knownMessageIds.delete(msgIdNum);
            knownMessageIds.delete(msgIdStr);
        }
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
                if (pinLabel) pinLabel.textContent = alreadyPinned ? 'Открепить' : 'Закрепить';
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
        }).catch(function () {});
    }

    function chatAvatarMarkup(chat) {
        var initial = (chat.title || '?')[0].toUpperCase();
        if (chat.picture) return '<img src="' + u.escapeHtml(chat.picture) + '" alt="">';
        return initial;
    }

    function updateForwardCounter() {
        if (!forwardCounterEl) return;
        var n = forwardSelection.size;
        if (n === 0) forwardCounterEl.textContent = 'Не выбрано чатов';
        else forwardCounterEl.textContent = 'Выбрано: ' + n;
        if (forwardSendBtn) forwardSendBtn.disabled = n === 0;
    }

    function resolveForwardSourceId(msg, fallbackId) {
        if (!msg || !msg.content || !msg.content.attachments) return fallbackId;
        for (var i = 0; i < msg.content.attachments.length; i++) {
            var a = msg.content.attachments[i];
            var t = a.type;
            if ((t === 'FORWARDED_MESSAGE' || t === 8 || t === '8') && a.forwardedMessage && a.forwardedMessage.originalMessageId) {
                return a.forwardedMessage.originalMessageId;
            }
        }
        return fallbackId;
    }

    function openForwardModal(originalMsgId) {
        if (!forwardOverlay || !originalMsgId) return;
        forwardSelection = new Set();
        if (forwardCommentEl) forwardCommentEl.value = '';
        forwardChatListEl.innerHTML = '';

        chats.forEach(function (chat) {
            var item = document.createElement('div');
            item.className = 'forward-chat-item';
            item.dataset.chatId = chat.id;
            item.innerHTML =
                '<div class="fwd-avatar">' + chatAvatarMarkup(chat) + '</div>' +
                '<div class="fwd-name">' + u.escapeHtml(chat.title || 'Чат') + '</div>' +
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

        forwardOverlay.classList.add('visible');
        forwardSendBtn.onclick = function () { forwardSubmit(originalMsgId); };
    }

    function closeForwardModal() {
        if (!forwardOverlay) return;
        forwardOverlay.classList.remove('visible');
        forwardSelection = new Set();
        if (forwardSendBtn) forwardSendBtn.onclick = null;
    }

    function forwardSubmit(originalMsgId) {
        if (forwardSelection.size === 0 || !originalMsgId) return;
        var comment = forwardCommentEl ? forwardCommentEl.value.trim() : '';
        var ids = Array.from(forwardSelection);
        forwardSendBtn.disabled = true;
        var originalLabel = forwardSendBtn.textContent;
        forwardSendBtn.textContent = 'Отправка...';

        var chain = ids.reduce(function (p, chatId) {
            return p.then(function () {
                return BF.api.sendMessage({
                    chatId: chatId,
                    text: comment || null,
                    forwardedMessageId: originalMsgId
                }).catch(function () { });
            });
        }, Promise.resolve());

        chain.then(function () {
            forwardSendBtn.disabled = false;
            forwardSendBtn.textContent = originalLabel;
            closeForwardModal();
            if (soonToastEl) {
                soonToastEl.textContent = 'Переслано в ' + ids.length + ' ' + (ids.length === 1 ? 'чат' : 'чатов');
                soonToastEl.classList.add('visible');
                setTimeout(function () {
                    soonToastEl.classList.remove('visible');
                    soonToastEl.textContent = 'Скоро будет';
                }, 1800);
            }
        });
    }

    function scrollToMessage(id) {
        if (!id) return;
        var el = messagesInner.querySelector('[data-msg-id="' + id + '"]');
        if (el) {
            el.scrollIntoView({ block: 'center', behavior: 'smooth' });
            el.classList.add('highlight');
            setTimeout(function () { el.classList.remove('highlight'); }, 1500);
            return;
        }
        if (!currentChatId) return;
        BF.api.listMessages(currentChatId, id, 25, 25).then(function (data) {
            if (!data || !data.messages || data.messages.length === 0) return;
            var existingIds = new Set(messages.map(function (m) { return m.id; }));
            var merged = messages.slice();
            data.messages.forEach(function (m) {
                if (!existingIds.has(m.id)) merged.push(m);
            });
            merged.sort(function (a, b) { return (a.sentAt || 0) - (b.sentAt || 0); });
            messages = merged;
            renderMessages().then(function () {
                var el2 = messagesInner.querySelector('[data-msg-id="' + id + '"]');
                if (el2) {
                    el2.scrollIntoView({ block: 'center', behavior: 'smooth' });
                    el2.classList.add('highlight');
                    setTimeout(function () { el2.classList.remove('highlight'); }, 1500);
                }
            });
        });
    }

    // --- Reply preview close handler ---
    if (rpbCloseBtn) rpbCloseBtn.addEventListener('click', clearPendingReply);

    // --- Edit preview close handler ---
    if (epbCloseBtn) epbCloseBtn.addEventListener('click', clearPendingEdit);

    // --- Delete confirm cancel ---
    if (deleteMsgCancel) {
        deleteMsgCancel.addEventListener('click', function () {
            if (deleteMsgConfirmOverlay) deleteMsgConfirmOverlay.classList.remove('visible');
            if (deleteMsgOk) deleteMsgOk.onclick = null;
        });
    }
    if (deleteMsgConfirmOverlay) {
        deleteMsgConfirmOverlay.addEventListener('click', function (e) {
            if (e.target === deleteMsgConfirmOverlay) {
                deleteMsgConfirmOverlay.classList.remove('visible');
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
                openForwardModal(resolveForwardSourceId(msg, msgId));
            } else if (act === 'copy-text') {
                var t = msg && msg.content && msg.content.text;
                if (t) navigator.clipboard.writeText(t).catch(function () {});
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

    function contextMenuIcon(name) {
        return '<span class="cm-icon"><svg aria-hidden="true"><use href="#bf-icon-' + name + '"></use></svg></span>';
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
                hdr1.textContent = 'Добавить в папку';
                chatContextMenu.appendChild(hdr1);
                without.forEach(function (f) {
                    var btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'cm-item';
                    btn.dataset.act = 'add-folder';
                    btn.dataset.folderId = f.folderId;
                    btn.innerHTML = contextMenuIcon('folder-plus') + '<span class="cm-label">' + u.escapeHtml(f.folderName || 'Папка') + '</span>';
                    chatContextMenu.appendChild(btn);
                });
            }

            if (inFolders.length > 0) {
                var hdr2 = document.createElement('div');
                hdr2.className = 'cm-section-title';
                hdr2.textContent = 'Удалить из папки';
                chatContextMenu.appendChild(hdr2);
                inFolders.forEach(function (f) {
                    var btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'cm-item';
                    btn.dataset.act = 'remove-folder';
                    btn.dataset.folderId = f.folderId;
                    btn.innerHTML = contextMenuIcon('folder-minus') + '<span class="cm-label">' + u.escapeHtml(f.folderName || 'Папка') + '</span>';
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
        createBtn.innerHTML = contextMenuIcon('folder-plus') + '<span class="cm-label">Создать папку</span>';
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

    // ========== INIT ==========

    requestNotificationPermission();

    if (BF.pinned && BF.pinned.init) {
        BF.pinned.init({
            getMyUserId: function () { return myUserId; },
            getCurrentChatInfo: function () { return currentChatInfo; },
            getUser: getUser,
            showMediaOverlay: showMediaOverlay,
            scrollToMessage: scrollToMessage
        });
    }

    if (BF.folders && BF.folders.init) {
        BF.folders.setOnChange(function () { renderChatList(); });
        BF.folders.init().then(function () {
            return loadChats(true);
        }).then(updateTitleBadge).then(maybeOpenChatFromCookie);
    } else {
        loadChats(true).then(updateTitleBadge).then(maybeOpenChatFromCookie);
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

    BF.realtime.startAll();
    if (BF.calls && BF.calls.start) BF.calls.start();

    if (BF.personalization && BF.personalization.init) BF.personalization.init();

})();
