/**
 * Sidebar chat list: paged loading, rendering, quiet catch-up refresh and the chat context menu (folders).
 * Requires: BF.api, BF.folders, BF.drafts, BF.i18n, BF.icons, BF.utils
 * Exposes: BF.chatList
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var u;
    var chatListEl;
    var chatContextMenu;
    var chatListUserIdsLoaded = new Set();
    var chatListOffset = 0;
    var chatListTotal = 0;
    var chatListLoading = false;
    var chatListRequest = null;
    var chatCmTargetId = null;
    var chatCmShownAt = 0;

    function $(selector) {
        return document.querySelector(selector);
    }

    function load(reset) {
        if (chatListLoading) return chatListRequest || Promise.resolve(false);
        var chats = deps.getChats();
        if (!reset && chats.length >= chatListTotal && chatListTotal > 0) return Promise.resolve();

        chatListLoading = true;
        if (reset) {
            chatListOffset = 0;
            deps.setChats([]);
        }

        var request = BF.api
            .listChats(chatListOffset, 50)
            .then(function (data) {
                if (!data || !data.chats) return false;
                chatListTotal = data.totalCount;
                deps.setChats(reset ? data.chats : deps.getChats().concat(data.chats));
                deps.getChats().sort(function (a, b) {
                    var bt = (b.lastMessage && b.lastMessage.sentAt) || b.lastActivityAt || 0;
                    var at = (a.lastMessage && a.lastMessage.sentAt) || a.lastActivityAt || 0;
                    return bt - at;
                });
                chatListOffset = deps.getChats().length;
                render();
                deps.collectOnlineUserIds();
                loadChatListUsers();
                return true;
            })
            .catch(function () {
                return false;
            })
            .then(function (result) {
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
        var myUserId = deps.getMyUserId();
        var userIds = [];
        deps.getChats().forEach(function (chat) {
            if (chat.isGroupChat || !chat.members) return;
            chat.members.forEach(function (member) {
                if (
                    member.userId !== myUserId &&
                    !deps.getCachedUser(member.userId) &&
                    !chatListUserIdsLoaded.has(member.userId)
                ) {
                    chatListUserIdsLoaded.add(member.userId);
                    userIds.push(member.userId);
                }
            });
        });
        if (userIds.length === 0) return;

        var chain = Promise.resolve();
        for (var i = 0; i < userIds.length; i += 5) {
            (function (batch) {
                chain = chain.then(function () {
                    return Promise.all(batch.map(deps.getUser));
                });
            })(userIds.slice(i, i + 5));
        }
        chain
            .then(function () {
                render();
                deps.collectOnlineUserIds();
            })
            .catch(function () {
                userIds.forEach(function (id) {
                    chatListUserIdsLoaded.delete(id);
                });
            });
    }

    function render() {
        var chats = deps.getChats();
        var currentChatId = deps.getCurrentChatId();
        var myUserId = deps.getMyUserId();
        if (BF.folders && BF.folders.renderTabs) BF.folders.renderTabs(chats);
        chatListEl.innerHTML = '';
        var visibleChats = BF.folders && BF.folders.filterChats ? BF.folders.filterChats(chats) : chats;
        visibleChats.forEach(function (chat) {
            var el = document.createElement('div');
            el.className = 'chat-item' + (chat.id === currentChatId ? ' active' : '');
            el.dataset.chatId = chat.id;
            el.tabIndex = 0;
            el.setAttribute('role', 'button');
            el.setAttribute('aria-label', chat.title || BF.i18n.t('common.chat'));

            var avatarInitial = (chat.title || '?')[0].toUpperCase();
            var avatarHtml = chat.picture ? '<img src="' + u.escapeHtml(chat.picture) + '" alt="">' : avatarInitial;

            var isPrivate = chat.chatType === 1;
            var lm = chat.lastMessage;
            var hasDraft = chat.chatType === 0 && (chat.hasDraft === true || (BF.drafts && BF.drafts.has(chat.id)));
            var previewHtml = '';
            if (hasDraft) {
                previewHtml = '<span class="preview-draft">' + u.escapeHtml(BF.i18n.t('chatlist.draft')) + '</span>';
            } else if (isPrivate) {
                // Содержимое зашифровано — сервер (и превью) его не знает.
                if (chat.privateInviteState === 0) {
                    previewHtml =
                        chat.privateInviterUserId === myUserId
                            ? u.escapeHtml(BF.i18n.t('privatechat.waitingPeer'))
                            : '<span class="preview-private-invite">' +
                              u.escapeHtml(BF.i18n.t('privatechat.invite')) +
                              '</span>';
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
                var peer = chat.members.find(function (m) {
                    return m.userId !== myUserId;
                });
                if (peer) peerUserId = peer.userId;
            }
            var peerUser = peerUserId ? deps.getCachedUser(peerUserId) : null;
            var isBot = !!(peerUser && peerUser.isBot);
            var onlineDot =
                peerUser && !isBot
                    ? '<div class="online-dot' +
                      (peerUserId && deps.isUserOnline(peerUserId) ? ' visible' : '') +
                      '" data-online-user="' +
                      (peerUserId || '') +
                      '"></div>'
                    : '';

            el.innerHTML =
                '<div class="chat-avatar">' +
                avatarHtml +
                onlineDot +
                '</div>' +
                '<div class="chat-info"><div class="chat-info-top">' +
                '<span class="chat-name">' +
                (isPrivate
                    ? '<span class="chat-lock" title="' +
                      u.escapeHtml(BF.i18n.t('newchat.mode.private')) +
                      '">' +
                      BF.icons.html('security') +
                      '</span>'
                    : '') +
                u.escapeHtml(chat.title || BF.i18n.t('common.chat')) +
                (isBot ? deps.botBadgeMarkup() : '') +
                '</span>' +
                '<span class="chat-time">' +
                time +
                '</span></div>' +
                '<div class="chat-info-bottom"><span class="chat-preview">' +
                previewHtml +
                '</span>' +
                '<span class="chat-unread' +
                (unread > 0 ? ' visible' : '') +
                '">' +
                unreadText +
                '</span></div></div>';

            el.addEventListener('click', function () {
                deps.openChat(chat.id);
            });
            el.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    deps.openChat(chat.id);
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
            c.id,
            c.title,
            c.picture,
            c.countUnread || 0,
            c.privateInviteState,
            c.lastActivityAt || 0,
            lm ? lm.id + '|' + (lm.editedAt || 0) + '|' + ((lm.content && lm.content.text) || '') : ''
        ].join('');
    }

    function refreshQuiet() {
        if (chatListLoading) return chatListRequest || Promise.resolve(false);
        chatListLoading = true;

        var request = BF.api
            .listChats(0, 50)
            .then(function (data) {
                if (!data || !data.chats) return false;
                var fetched = data.chats.slice();
                fetched.sort(function (a, b) {
                    var bt = (b.lastMessage && b.lastMessage.sentAt) || b.lastActivityAt || 0;
                    var at = (a.lastMessage && a.lastMessage.sentAt) || a.lastActivityAt || 0;
                    return bt - at;
                });

                var chats = deps.getChats();
                var same = data.totalCount === chatListTotal && fetched.length <= chats.length;
                if (same) {
                    for (var i = 0; i < fetched.length; i++) {
                        if (chatSignature(fetched[i]) !== chatSignature(chats[i])) {
                            same = false;
                            break;
                        }
                    }
                }
                if (same) return true; // ничего не изменилось — DOM не трогаем

                deps.setChats(fetched);
                chatListTotal = data.totalCount;
                chatListOffset = fetched.length;
                render();
                deps.collectOnlineUserIds();
                loadChatListUsers();
                deps.updateTitleBadge();
                return true;
            })
            .catch(function () {
                return false;
            })
            .then(function (result) {
                if (chatListRequest === request) {
                    chatListLoading = false;
                    chatListRequest = null;
                }
                return result;
            });
        chatListRequest = request;
        return request;
    }

    // ========== CHAT CONTEXT MENU (PCM на чате в сайдбаре) ==========

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
                    btn.innerHTML =
                        contextMenuIcon() +
                        '<span class="cm-label">' +
                        u.escapeHtml(f.folderName || BF.i18n.t('folder.default')) +
                        '</span>';
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
                    btn.innerHTML =
                        contextMenuIcon() +
                        '<span class="cm-label">' +
                        u.escapeHtml(f.folderName || BF.i18n.t('folder.default')) +
                        '</span>';
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
        createBtn.innerHTML =
            contextMenuIcon() + '<span class="cm-label">' + u.escapeHtml(BF.i18n.t('folder.create')) + '</span>';
        chatContextMenu.appendChild(createBtn);
    }

    function openChatContextMenu(x, y, chatId) {
        if (!chatContextMenu) return;
        chatCmTargetId = chatId;
        buildChatContextMenu(chatId);
        chatContextMenu.classList.add('visible');

        var vw = window.innerWidth,
            vh = window.innerHeight;
        var rect = chatContextMenu.getBoundingClientRect();
        var w = rect.width,
            h = rect.height;
        var left = Math.max(8, Math.min(x, vw - w - 8));
        var top = y + h > vh ? Math.max(8, y - h) : y;
        chatContextMenu.style.left = left + 'px';
        chatContextMenu.style.top = top + 'px';
        chatCmShownAt = Date.now();
    }

    function closeChatContextMenu() {
        if (!chatContextMenu) return;
        chatContextMenu.classList.remove('visible');
        chatCmTargetId = null;
    }

    function init(options) {
        deps = options;
        u = BF.utils;
        chatListEl = $('#chatList');
        chatContextMenu = $('#chatContextMenu');

        chatListEl.addEventListener('scroll', function () {
            if (chatListEl.scrollTop + chatListEl.clientHeight >= chatListEl.scrollHeight - 100) load();
        });

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

        document.addEventListener(
            'click',
            function (e) {
                if (!chatContextMenu || !chatContextMenu.classList.contains('visible')) return;
                if (chatContextMenu.contains(e.target)) return;
                if (Date.now() - chatCmShownAt < 300) return;
                closeChatContextMenu();
            },
            true
        );
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && chatContextMenu && chatContextMenu.classList.contains('visible')) {
                closeChatContextMenu();
            }
        });
        window.addEventListener('resize', closeChatContextMenu);
        if (chatListEl) chatListEl.addEventListener('scroll', closeChatContextMenu);
    }

    window.BF.chatList = {
        init: init,
        load: load,
        render: render,
        refreshQuiet: refreshQuiet
    };
})();
