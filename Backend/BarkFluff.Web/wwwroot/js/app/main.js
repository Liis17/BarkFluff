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
    var sendBtn = $('#sendBtn');
    var attachBtn = $('#attachBtn');
    var fileInput = $('#fileInput');
    // Scroll-to-bottom button
    var scrollToBottomBtn = $('#scrollToBottomBtn');

    // Delete confirmation dialog
    var deleteMsgConfirmOverlay = $('#deleteMsgConfirmOverlay');
    var deleteMsgCancel = $('#deleteMsgCancel');
    var deleteMsgOk = $('#deleteMsgOk');
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
                mergePendingUploadsIntoMessages(chatId);
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
        mergePendingUploads: mergePendingUploadsIntoMessages,
        onPendingCancel: cancelPendingSend,
        onPendingRetry: retryPendingSend,
        decryptPrivateBatch: BF.privateChatUI.decryptMessages,
        showToast: showToast
    });
    var renderMessages = BF.feed.render;
    var appendMessageToView = BF.feed.append;
    var scrollToBottom = BF.feed.scrollToBottom;
    var settleScroll = BF.feed.settleScroll;
    var scrollToMessage = BF.feed.scrollToMessage;
    var findMessageGroup = BF.feed.findGroup;
    var buildMessageViewElement = BF.feed.buildElement;

    function releasePendingPreviews(entry) {
        if (!entry || !entry.previewUrls) return;
        entry.previewUrls.forEach(function (url) { URL.revokeObjectURL(url); });
        entry.previewUrls = [];
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
        if (el) BF.feed.removeElement(el);
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
            if (oldEl.isConnected) BF.feed.replaceElement(oldEl, newEl);
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
            if (oldEl) BF.feed.removeElement(oldEl);
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
            if (oldEl.isConnected) BF.feed.replaceElement(oldEl, newEl);
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
            replyToMessageId: BF.composer.getReplyToId(),
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
        var text = BF.composer.getText().trim();
        if (!currentChatId) return;
        stopTypingSend(true);

        if (currentChatType === 1) {
            if (text) sendPrivateMessageFlow(text);
            return;
        }

        var edit = BF.composer.getEdit();
        if (edit) {
            var editId = edit.messageId;
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
        if (BF.composer.getEdit()) {
            // Во время редактирования attach-flow заблокирован, чтобы не отправить новое сообщение
            // вместо правки исходного. Завершите или отмените редактирование.
            return;
        }
        stopTypingSend(true);
        var text = (caption != null ? caption : BF.composer.getText()).trim();
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

    sendBtn.addEventListener('click', sendMessage);

    // ========== COMPOSER ==========

    BF.composer.init({
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatType: function () { return currentChatType; },
        getMessages: function () { return messages; },
        getMyUserId: function () { return myUserId; },
        getUser: getUser,
        renderChatList: renderChatList,
        onSubmit: sendMessage,
        onSendFiles: sendMessageWithFiles
    });
    var stopTypingSend = BF.composer.stopTyping;
    var setPendingReply = BF.composer.setReply;
    var clearPendingReply = BF.composer.clearReply;
    var setPendingEdit = BF.composer.setEdit;
    var clearPendingEdit = BF.composer.clearEdit;
    var restoreChatDraft = BF.composer.restoreDraft;
    var restorePendingComposer = BF.composer.restoreFromPending;
    var clearComposerForPending = BF.composer.clearForSend;
    var openAttachModal = BF.composer.openAttach;

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

    // ========== MARK AS READ ==========

    BF.markRead.init({
        getCurrentChatId: function () { return currentChatId; },
        getCurrentChatType: function () { return currentChatType; },
        getMessages: function () { return messages; },
        getMyUserId: function () { return myUserId; },
        showToast: showToast
    });

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
        mergePendingUploads: mergePendingUploadsIntoMessages,
        reconcilePendingUpload: reconcilePendingUpload,
        renderMessages: renderMessages,
        appendMessageToView: appendMessageToView,
        scrollToBottom: scrollToBottom,
        applyMessageDelete: applyMessageDelete,
        applyMessageEdit: applyMessageEdit
    });

    // ========== REALTIME HANDLERS ==========


    BF.realtime.on('new_message', function (data) {
        handleNewMessage(data.chatId, data.message);
    });

    BF.realtime.on('message_read', function (data) {
        handleMessageRead(data.chatId, data.messageId, data.readBy);
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
            if (msg.senderId !== myUserId) BF.feed.announceIncoming(msg);
            messages.push(msg);
            appendMessageToView(msg).then(function () {
                if (isAtBottom) {
                    scrollToBottom();
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
    if (BF.pendingSends) {
        BF.pendingSends.init(myUserId);
        restorePendingSends();
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

    // ========== REPLY / FORWARD / CONTEXT MENU ==========

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
        buildMessageViewElement(updatedMsg).then(function (newEl) {
            newEl.dataset.date = oldEl.dataset.date;
            BF.feed.replaceElement(oldEl, newEl);
        });
    }

    function applyMessageDelete(chatId, messageId) {
        if (messageId == null) return;
        var msgIdNum = Number(messageId);
        console.log('[main] applyMessageDelete', { chatId: chatId, messageId: messageId, currentChatId: currentChatId });
        BF.composer.onMessageDeleted(msgIdNum);

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

    // --- Forward dialog ---
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
        requestDelete: requestDelete,
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
    var sendPrivateMessageFlow = BF.privateChatUI.send;

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
