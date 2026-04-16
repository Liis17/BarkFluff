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

    function getUser(userId) {
        if (userCache.has(userId)) return Promise.resolve(userCache.get(userId));
        return BF.api.getUser(userId).then(function (d) {
            if (d && d.user) { userCache.set(userId, d.user); return d.user; }
            return null;
        });
    }

    // --- State ---
    var chats = [];
    var currentChatId = null;
    var currentChatInfo = null;
    var messages = [];
    var isLoadingOlder = false;
    var noMoreOlder = false;
    var markReadTimer = null;
    var markReadPending = new Set();
    var onlineSubscribedUserIds = new Set();
    var onlineStatuses = new Map();
    var chatListOffset = 0;
    var chatListTotal = 0;
    var chatListLoading = false;

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

    // Scroll-to-bottom button
    var scrollToBottomBtn = $('#scrollToBottomBtn');

    // Settings and confirm overlays are managed by BF.settings module

    // Sticker picker elements
    var stickerBtn = $('#stickerBtn');
    var stickerPicker = $('#stickerPicker');
    var stickerPacksBar = $('#stickerPacksBar');
    var stickerGrid = $('#stickerGrid');

    // Profile elements
    var profileOverlay = $('#profileOverlay');
    var profileClose = $('#profileClose');
    var profileAvatar = $('#profileAvatar');
    var profileName = $('#profileName');
    var profileUsername = $('#profileUsername');
    var profileStatus = $('#profileStatus');
    var profileBio = $('#profileBio');
    var profileBadges = $('#profileBadges');
    var profileRegDate = $('#profileRegDate');
    var profileMediaContent = $('#profileMediaContent');
    var currentProfileUserId = null;

    // ========== CHAT LIST ==========

    function loadChats(reset) {
        if (chatListLoading) return Promise.resolve();
        if (!reset && chats.length >= chatListTotal && chatListTotal > 0) return Promise.resolve();

        chatListLoading = true;
        if (reset) { chatListOffset = 0; chats = []; }

        return BF.api.listChats(chatListOffset, 50).then(function (data) {
            if (!data || !data.chats) { chatListLoading = false; return; }
            chatListTotal = data.totalCount;
            chats = reset ? data.chats : chats.concat(data.chats);
            chats.sort(function (a, b) { return ((b.lastMessage && b.lastMessage.sentAt) || 0) - ((a.lastMessage && a.lastMessage.sentAt) || 0); });
            chatListOffset = chats.length;
            chatListLoading = false;
            renderChatList();
            collectOnlineUserIds();
        }).catch(function () { chatListLoading = false; });
    }

    function renderChatList() {
        chatListEl.innerHTML = '';
        chats.forEach(function (chat) {
            var el = document.createElement('div');
            el.className = 'chat-item' + (chat.id === currentChatId ? ' active' : '');
            el.dataset.chatId = chat.id;

            var avatarInitial = (chat.title || '?')[0].toUpperCase();
            var avatarHtml = chat.picture
                ? '<img src="' + u.escapeHtml(chat.picture) + '" alt="">'
                : avatarInitial;

            var lm = chat.lastMessage;
            var preview = '';
            if (lm) {
                var text = (lm.content && lm.content.text) || '';
                var ac = (lm.content && lm.content.attachments && lm.content.attachments.length) || 0;
                if (text) preview = u.truncate(text, 50);
                else if (ac > 0) preview = u.attachmentEmoji(lm.content.attachments[0].type);
            }

            var time = (lm && lm.sentAt) ? u.formatTime(lm.sentAt) : '';
            var unread = chat.countUnread || 0;
            var unreadText = unread > 99 ? '99+' : unread;

            var peerUserId = null;
            if (!chat.isGroupChat && chat.members && chat.members.length > 0) {
                var peer = chat.members.find(function (m) { return m.userId !== myUserId; });
                if (peer) peerUserId = peer.userId;
            }

            el.innerHTML =
                '<div class="chat-avatar">' + avatarHtml +
                '<div class="online-dot' + (peerUserId && isUserOnline(peerUserId) ? ' visible' : '') + '" data-online-user="' + (peerUserId || '') + '"></div></div>' +
                '<div class="chat-info"><div class="chat-info-top">' +
                '<span class="chat-name">' + u.escapeHtml(chat.title || 'Чат') + '</span>' +
                '<span class="chat-time">' + time + '</span></div>' +
                '<div class="chat-info-bottom"><span class="chat-preview">' + u.escapeHtml(preview) + '</span>' +
                '<span class="chat-unread' + (unread > 0 ? ' visible' : '') + '">' + unreadText + '</span></div></div>';

            el.addEventListener('click', function () { openChat(chat.id); });
            chatListEl.appendChild(el);
        });
    }

    chatListEl.addEventListener('scroll', function () {
        if (chatListEl.scrollTop + chatListEl.clientHeight >= chatListEl.scrollHeight - 100) loadChats();
    });

    // ========== OPEN CHAT ==========

    function openChat(chatId) {
        if (chatId === currentChatId) return;

        currentChatId = chatId;
        messages = [];
        noMoreOlder = false;
        if (scrollToBottomBtn) scrollToBottomBtn.classList.remove('visible');
        chatEmpty.style.display = 'none';
        chatHeader.classList.add('visible');
        messagesArea.classList.add('visible');
        messagesInner.innerHTML = '';
        inputBar.classList.add('visible');
        loadingMessages.classList.add('visible');

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
                    subscribeOnlineForUsers([peerId]);
                    // Fetch current online status via unary RPC to show immediately
                    BF.api.getOnlineStatus([peerId]).then(function (data) {
                        if (data && data.statuses && data.statuses.length > 0) {
                            var s = data.statuses[0];
                            handleOnlineStatus(s.userId, s.status, s.lastSeen);
                        }
                    }).catch(function () {});
                }
            } else {
                chatHeaderStatus.textContent = (info.membersId ? info.membersId.length : 0) + ' участников';
                chatHeaderStatus.classList.remove('online');
            }

            var fromId = info.firstUnreadMessageId || 0;
            return BF.api.listMessages(chatId, fromId, 30, 10);
        }).then(function (data) {
            loadingMessages.classList.remove('visible');
            if (data && data.messages) {
                messages = data.messages;
                renderMessages().then(scrollToBottom);
                scheduleMarkRead();
            }
        }).catch(function () { loadingMessages.classList.remove('visible'); });
    }

    // ========== RENDER MESSAGES ==========

    function renderMessages() {
        messagesInner.innerHTML = '';
        var allFileIds = [];
        messages.forEach(function (msg) {
            ((msg.content && msg.content.attachments) || []).forEach(function (a) {
                if (a.fileId && !BF.files.getCachedFileUrl(a.fileId)) allFileIds.push(a.fileId);
            });
        });

        var p = allFileIds.length > 0 ? BF.files.getFileUrls(allFileIds) : Promise.resolve();

        return p.then(function () {
            var chain = Promise.resolve();
            var lastDate = null;
            messages.forEach(function (msg) {
                chain = chain.then(function () {
                    var msgDate = u.formatDate(msg.sentAt);
                    if (msgDate !== lastDate) {
                        lastDate = msgDate;
                        var sep = document.createElement('div');
                        sep.className = 'msg-date-separator';
                        sep.innerHTML = '<span>' + u.escapeHtml(msgDate) + '</span>';
                        messagesInner.appendChild(sep);
                    }
                    return BF.messages.buildMessageElement(msg, myUserId, !!(currentChatInfo && currentChatInfo.isGroupChat), getUser, showMediaOverlay).then(function (el) {
                        messagesInner.appendChild(el);
                    });
                });
            });
            return chain;
        });
    }

    function scrollToBottom() { messagesArea.scrollTop = messagesArea.scrollHeight; }

    function appendMessageToView(msg) {
        var fileIds = ((msg.content && msg.content.attachments) || []).map(function (a) { return a.fileId; }).filter(function (id) { return id && !BF.files.getCachedFileUrl(id); });
        var p = fileIds.length > 0 ? BF.files.getFileUrls(fileIds) : Promise.resolve();

        return p.then(function () {
            var msgDate = u.formatDate(msg.sentAt);
            var lastGroup = messagesInner.lastElementChild;
            var lastMsgDate = lastGroup && lastGroup.dataset && lastGroup.dataset.date;
            if (msgDate !== lastMsgDate) {
                var sep = document.createElement('div');
                sep.className = 'msg-date-separator';
                sep.dataset.date = msgDate;
                sep.innerHTML = '<span>' + u.escapeHtml(msgDate) + '</span>';
                messagesInner.appendChild(sep);
            }
            return BF.messages.buildMessageElement(msg, myUserId, !!(currentChatInfo && currentChatInfo.isGroupChat), getUser, showMediaOverlay);
        }).then(function (el) {
            el.dataset.date = u.formatDate(msg.sentAt);
            messagesInner.appendChild(el);
        });
    }

    // Lazy-load older messages
    messagesArea.addEventListener('scroll', function () {
        if (messagesArea.scrollTop < 100 && !isLoadingOlder && !noMoreOlder && currentChatId && messages.length > 0) {
            isLoadingOlder = true;
            loadingMessages.classList.add('visible');
            var oldestId = messages[0].id || 0;
            var prevHeight = messagesArea.scrollHeight;

            BF.api.listMessages(currentChatId, oldestId, 30, 0).then(function (data) {
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
        if (!text || !currentChatId) return;

        sendBtn.disabled = true;
        var sentChatId = currentChatId;

        BF.api.sendMessage({ chatId: sentChatId, text: text, fileIds: null }).then(function (resp) {
            messageInput.value = '';
            messageInput.style.height = 'auto';
            sendBtn.disabled = false;
            messageInput.focus();

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
        }).catch(function () { sendBtn.disabled = false; });
    }

    function sendMessageWithFiles(files, asDocuments) {
        var text = messageInput.value.trim();
        var sentChatId = currentChatId;
        sendBtn.disabled = true;

        var uploadChain = files.reduce(function (chain, file) {
            return chain.then(function (ids) {
                var t = BF.files.getUploadFileType(file.type, asDocuments);
                return BF.files.uploadFile(file, t)
                    .then(function (fid) { ids.push(fid); return ids; })
                    .catch(function () { return ids; });
            });
        }, Promise.resolve([]));

        uploadChain.then(function (fileIds) {
            if (fileIds.length === 0) { sendBtn.disabled = false; return; }
            return BF.api.sendMessage({
                chatId: sentChatId,
                text: text || null,
                fileIds: fileIds
            }).then(function (resp) {
                messageInput.value = '';
                messageInput.style.height = 'auto';
                sendBtn.disabled = false;
                messageInput.focus();
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
            });
        }).catch(function () { sendBtn.disabled = false; });
    }

    function openAttachModal(files) {
        if (!currentChatId) return;
        BF.attach.open(files, function (outFiles, asDocuments) {
            sendMessageWithFiles(outFiles, asDocuments);
        });
    }

    sendBtn.addEventListener('click', sendMessage);
    messageInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); }
    });
    messageInput.addEventListener('input', function () {
        messageInput.style.height = 'auto';
        messageInput.style.height = Math.min(messageInput.scrollHeight, 120) + 'px';
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
        chatArea.appendChild(dropOverlay);

        var dragCounter = 0;
        function isFileDrag(e) {
            return e.dataTransfer && Array.from(e.dataTransfer.types || []).includes('Files');
        }
        chatArea.addEventListener('dragenter', function (e) {
            if (!currentChatId || !isFileDrag(e)) return;
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
        BF.api.markAsRead(ids).catch(function () {});
    }

    // ========== TITLE UNREAD BADGE ==========

    var baseTitle = 'BarkFluff — Мессенджер';

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

    BF.realtime.on('connection_status', function (data) {
        if (connectionBanner) {
            connectionBanner.classList.toggle('visible', !data.connected);
        }
    });

    // ========== SCROLL-BASED MARK AS READ ==========

    function markVisibleMessagesAsRead() {
        if (!currentChatId) return;
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
        // Refresh chat list to sync any missed updates while tab was hidden
        loadChats(true);
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

    function handleNewMessage(chatId, msg) {
        if (chatId === currentChatId && messages.some(function (m) { return m.id === msg.id; })) return;

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
            showNewMessageNotification(chatTitle, msg);
        }

        updateTitleBadge();

        if (chatId === currentChatId) {
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
                // Update check-mark indicator (single ✓ = sent, double ✓✓ = read by others)
                var el = messagesArea.querySelector('.msg-status[data-msg-id="' + messageId + '"]');
                if (el) {
                    var rc = readBy.filter(function (id) { return id !== myUserId; }).length;
                    el.innerHTML = rc > 0 ? '&#10003;&#10003;' : '&#10003;';
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
        if (currentChatInfo && !currentChatInfo.isGroupChat) {
            var peerId = (currentChatInfo.membersId || []).find(function (id) { return id !== myUserId; });
            if (peerId === userId) updateChatHeaderOnline(userId);
        }
    }

    function updateChatHeaderOnline(userId) {
        var entry = onlineStatuses.get(userId);
        var online = entry ? BF.utils.isStatusOnline(entry.status) : false;
        if (online) {
            chatHeaderStatus.textContent = 'в сети';
        } else {
            chatHeaderStatus.textContent = BF.utils.formatLastSeen(entry ? entry.lastSeen : null);
        }
        chatHeaderStatus.classList.toggle('online', online);
    }

    function collectOnlineUserIds() {
        var ids = new Set();
        chats.forEach(function (chat) {
            if (!chat.isGroupChat && chat.members) {
                chat.members.forEach(function (m) { if (m.userId !== myUserId) ids.add(m.userId); });
            }
        });
        subscribeOnlineForUsers(Array.from(ids));
    }

    function subscribeOnlineForUsers(userIds) {
        var changed = false;
        userIds.forEach(function (id) {
            if (!onlineSubscribedUserIds.has(id)) { onlineSubscribedUserIds.add(id); changed = true; }
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

    function showMediaOverlay(type, url) {
        if (type === 'video') {
            overlayImage.style.display = 'none';
            overlayVideo.style.display = 'block';
            overlayVideo.src = url;
            overlayVideo.play();
        } else {
            overlayVideo.style.display = 'none';
            overlayImage.style.display = 'block';
            overlayImage.src = url;
        }
        imageOverlay.classList.add('visible');
    }

    imageOverlay.addEventListener('click', function (e) {
        if (e.target === overlayVideo) return;
        imageOverlay.classList.remove('visible');
        overlayImage.src = '';
        overlayVideo.pause();
        overlayVideo.src = '';
    });

    // ========== PROFILE OVERLAY ==========

    function openProfile(userId) {
        if (!userId) return;
        currentProfileUserId = userId;

        BF.api.getUser(userId).then(function (d) {
            if (!d || !d.user) return;
            var user = d.user;

            var initial = (user.firstName || user.username || '?')[0].toUpperCase();
            profileAvatar.innerHTML = user.profilePicture
                ? '<img src="' + user.profilePicture + '" alt="">' : initial;
            profileName.textContent = [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username;
            profileUsername.textContent = user.username ? '@' + user.username : '';
            profileBio.textContent = user.bio || '';
            profileBio.style.display = user.bio ? 'block' : 'none';

            var online = isUserOnline(userId);
            var entry = onlineStatuses.get(userId);
            profileStatus.textContent = online ? 'в сети' : BF.utils.formatLastSeen(entry ? entry.lastSeen : null);
            profileStatus.className = 'profile-status-line' + (online ? ' online' : '');

            if (user.registrationDate) {
                profileRegDate.textContent = new Date(user.registrationDate).toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' });
            } else { profileRegDate.textContent = '\u2014'; }

            profileBadges.innerHTML = '';
            if (user.badges && user.badges.length > 0) {
                user.badges.forEach(function (b) {
                    var el = document.createElement('div');
                    el.className = 'profile-badge';
                    el.innerHTML = (b.imageUrl ? '<img src="' + b.imageUrl + '" alt="">' : '') + b.name;
                    profileBadges.appendChild(el);
                });
            }

            loadProfileMedia('media');
            profileOverlay.classList.add('visible');
        });
    }

    function loadProfileMedia(type) {
        profileMediaContent.innerHTML = '';
        if (!currentChatId) return;

        if (type === 'media') {
            Promise.all([
                BF.api.listChatAttachments(currentChatId, 1, 0, 30),
                BF.api.listChatAttachments(currentChatId, 2, 0, 30)
            ]).then(function (results) {
                var all = (results[0].attachments || []).concat(results[1].attachments || []);
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
                            img.addEventListener('click', function () { showMediaOverlay(att.type === 'VIDEO' ? 'video' : 'image', url); });
                            grid.appendChild(img);
                        });
                    });
                });
                chain.then(function () { profileMediaContent.appendChild(grid); });
            });
        } else {
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
                            el.innerHTML = '<span>&#128196;</span> ' + (att.fileName || 'Файл');
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

    chatHeaderAvatar.addEventListener('click', function () {
        if (!currentChatInfo || currentChatInfo.isGroupChat) return;
        var peerId = (currentChatInfo.membersId || []).find(function (id) { return id !== myUserId; });
        if (peerId) openProfile(peerId);
    });

    chatHeaderName.addEventListener('click', function () {
        if (!currentChatInfo || currentChatInfo.isGroupChat) return;
        var peerId = (currentChatInfo.membersId || []).find(function (id) { return id !== myUserId; });
        if (peerId) openProfile(peerId);
    });

    profileClose.addEventListener('click', function () { profileOverlay.classList.remove('visible'); });
    profileOverlay.addEventListener('click', function (e) { if (e.target === profileOverlay) profileOverlay.classList.remove('visible'); });

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
        if (!stickerPicker.contains(e.target) && e.target !== stickerBtn) {
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
                stickerGrid.appendChild(img);
            });
        });
    }

    function sendSticker(fileId) {
        if (!currentChatId || !fileId) return;
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

    // ========== PROACTIVE TOKEN REFRESH ==========

    setInterval(function () {
        if (BF.tokens.isAccessExpired()) {
            BF.clients.refreshToken().then(function (token) {
                if (token) BF.realtime.reconnect();
            });
        }
    }, 60000);

    // ========== INIT ==========

    requestNotificationPermission();
    loadChats(true).then(updateTitleBadge);
    BF.realtime.startAll();

})();
