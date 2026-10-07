/**
 * Sidebar chat list: paged loading, windowed (virtualized) rendering, keyboard listbox navigation,
 * quiet catch-up refresh and the chat context menu (folders, delete chat / leave group).
 * Requires: BF.api, BF.folders, BF.drafts, BF.i18n, BF.icons, BF.utils, BF.realtime
 * Exposes: BF.chatList
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var u;
    var chatListEl;
    var chatContextMenu;
    var deleteChatOverlay;
    var deleteChatTitle;
    var deleteChatText;
    var deleteChatForEveryoneRow;
    var deleteChatForEveryoneLabel;
    var deleteChatForEveryoneCheckbox;
    var deleteChatOk;
    var deleteChatCancel;
    var chatListUserIdsLoaded = new Set();
    var chatListOffset = 0;
    var chatListTotal = 0;
    var chatListTotalKnown = false;
    var serverCountedChatIds = new Set();
    var chatListLoading = false;
    var chatListRequest = null;
    var chatListPageRequest = null;
    var refreshAfterCurrentRequest = false;
    var loadAfterCurrentRequest = false;
    var resetAfterCurrentRequest = false;
    var removedChatIds = new Set();
    var chatCmTargetId = null;
    var chatCmShownAt = 0;
    var chatCmReturnChatId = null; // открыто с клавиатуры: фокус вернётся на строку чата
    var chatCmReturnEl = null; // открыто мышью: фокус вернётся на прежний элемент
    var suppressContextMenuUntil = 0; // браузер шлёт contextmenu вслед за клавишей ContextMenu/Shift+F10

    // Оконная виртуализация: в DOM только строки видимой области ± OVERSCAN (высота строки фиксирована).
    var OVERSCAN = 8;
    var ROW = 75; // --chat-row-height
    var NAV = 0; // нижний padding списка под плавающую навигацию
    var spacer;
    var visibleChats = []; // после фильтра папок и дедупа по id
    var indexById = new Map(); // String(chat.id) → индекс в visibleChats
    var rows = new Map(); // String(chat.id) → { el, html, top, index }
    var roverId = null; // строка с tabindex=0 (roving tabindex)
    var lastActiveId = null;
    var windowFramePending = false;

    function $(selector) {
        return document.querySelector(selector);
    }

    function filterRemovedChats(chats, totalCount, removedIdsAtRequest, totalKnownAtRequest) {
        var removedDuringRequest = [];
        removedChatIds.forEach(function (id) {
            if (!removedIdsAtRequest.has(id)) removedDuringRequest.push(id);
        });
        if (removedDuringRequest.length > 0) {
            if (totalKnownAtRequest) {
                totalCount = Math.min(totalCount, chatListTotal);
            } else {
                var fetchedIds = new Set(
                    chats.map(function (chat) {
                        return String(chat.id);
                    })
                );
                removedDuringRequest.forEach(function (id) {
                    if (fetchedIds.has(id)) totalCount = Math.max(0, totalCount - 1);
                });
            }
        }
        return {
            chats: chats.filter(function (chat) {
                return !removedChatIds.has(String(chat.id));
            }),
            totalCount: Math.max(0, totalCount)
        };
    }

    function rememberServerCountedChats(chats, reset) {
        if (reset) serverCountedChatIds.clear();
        chats.forEach(function (chat) {
            serverCountedChatIds.add(String(chat.id));
        });
    }

    function completeListRequest(request, result) {
        var shouldRefresh = false;
        var shouldLoad = false;
        var shouldReset = false;
        if (chatListRequest === request) {
            chatListLoading = false;
            chatListRequest = null;
            chatListPageRequest = null;
            shouldRefresh = refreshAfterCurrentRequest;
            shouldLoad = loadAfterCurrentRequest;
            shouldReset = resetAfterCurrentRequest;
            refreshAfterCurrentRequest = false;
            loadAfterCurrentRequest = false;
            resetAfterCurrentRequest = false;
        }
        if (shouldRefresh) {
            if (shouldReset) resetAfterCurrentRequest = true;
            else if (shouldLoad) loadAfterCurrentRequest = true;
            return refreshChatListTotal().then(function () {
                return result;
            });
        }
        if (shouldReset) {
            return load(true).then(function () {
                return result;
            });
        }
        if (shouldLoad) {
            return load().then(function () {
                return result;
            });
        }
        return result;
    }

    function load(reset) {
        if (chatListLoading) {
            if (reset) {
                resetAfterCurrentRequest = true;
                loadAfterCurrentRequest = false;
            } else if (!resetAfterCurrentRequest) {
                loadAfterCurrentRequest = true;
            }
            return chatListRequest || Promise.resolve(false);
        }
        if (!reset && chatListTotalKnown && chatListOffset >= chatListTotal) return Promise.resolve();

        chatListLoading = true;
        var removedIdsAtRequest = new Set(removedChatIds);
        var totalKnownAtRequest = chatListTotalKnown;
        var pageRequest = !reset && chatListOffset > 0 ? { invalidated: false } : null;
        chatListPageRequest = pageRequest;
        if (reset) {
            chatListOffset = 0;
            serverCountedChatIds.clear();
            deps.setChats([]);
        }

        var request = BF.api
            .listChats(chatListOffset, 50)
            .then(function (data) {
                if (!data || !data.chats) return false;
                if (pageRequest && pageRequest.invalidated) return false;
                var filtered = filterRemovedChats(
                    data.chats,
                    data.totalCount,
                    removedIdsAtRequest,
                    totalKnownAtRequest
                );
                chatListTotal = filtered.totalCount;
                chatListTotalKnown = true;
                var fetched = filtered.chats;
                rememberServerCountedChats(fetched, reset);
                deps.setChats(reset ? fetched : deps.getChats().concat(fetched));
                deps.getChats().sort(function (a, b) {
                    var bt = (b.lastMessage && b.lastMessage.sentAt) || b.lastActivityAt || 0;
                    var at = (a.lastMessage && a.lastMessage.sentAt) || a.lastActivityAt || 0;
                    return bt - at;
                });
                // Keep the offset in sync with live rows; a tombstoned API row no longer occupies a server slot.
                chatListOffset = reset ? fetched.length : chatListOffset + fetched.length;
                render();
                deps.collectOnlineUserIds();
                loadChatListUsers();
                return true;
            })
            .catch(function () {
                return false;
            })
            .then(function (result) {
                return completeListRequest(request, result);
            });
        chatListRequest = request;
        return request;
    }

    function removeChat(chatId) {
        var id = String(chatId);
        var alreadyRemoved = removedChatIds.has(id);
        var chats = deps.getChats();
        var filtered = chats.filter(function (chat) {
            return String(chat.id) !== id;
        });
        var removed = filtered.length !== chats.length;
        var wasCountedByServer = serverCountedChatIds.has(id);
        var wasOpen = deps.getCurrentChatId && String(deps.getCurrentChatId()) === id;
        if (!removed && !wasOpen && alreadyRemoved) return false;

        if (!alreadyRemoved) {
            removedChatIds.add(id);
            if (removed && wasCountedByServer && chatListTotalKnown) chatListTotal = Math.max(0, chatListTotal - 1);
        }
        if (removed) {
            deps.setChats(filtered);
            if (wasCountedByServer) {
                serverCountedChatIds.delete(id);
                chatListOffset = Math.max(0, chatListOffset - 1);
            }
        }
        if (wasOpen && deps.closeCurrentChat) deps.closeCurrentChat();
        if (chatListLoading) {
            refreshAfterCurrentRequest = true;
            if (removed && wasCountedByServer && chatListPageRequest) {
                chatListPageRequest.invalidated = true;
                loadAfterCurrentRequest = true;
            }
        }

        render();
        if (deps.collectOnlineUserIds) deps.collectOnlineUserIds();
        if (deps.updateTitleBadge) deps.updateTitleBadge();
        if ((!removed || (!wasCountedByServer && chatListTotalKnown)) && !chatListLoading) refreshChatListTotal();
        return true;
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

    function rowHtml(chat) {
        var myUserId = deps.getMyUserId();
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

        return (
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
            '</span></div></div>'
        );
    }

    // Метка option для скринридера: название, непрочитанные, превью и время.
    function rowLabel(el, chat) {
        var parts = [chat.title || BF.i18n.t('common.chat')];
        var unread = chat.countUnread || 0;
        if (unread > 0) parts.push(BF.i18n.tp('a11y.chat.unread', unread));
        var preview = el.querySelector('.chat-preview');
        var time = el.querySelector('.chat-time');
        if (preview && preview.textContent) parts.push(preview.textContent);
        if (time && time.textContent) parts.push(time.textContent);
        return parts.join(', ');
    }

    function readMetrics() {
        var style = getComputedStyle(chatListEl);
        ROW = parseFloat(style.getPropertyValue('--chat-row-height')) || 75;
        NAV = parseFloat(style.paddingBottom) || 0;
    }

    function focusedRowKey() {
        var active = document.activeElement;
        if (!active || !chatListEl.contains(active) || !active.closest) return null;
        var item = active.closest('.chat-item');
        return item ? String(item.dataset.chatId) : null;
    }

    // Данные списка изменились: пересчитать видимые чаты и перерисовать окно.
    function render() {
        var chats = deps.getChats();
        if (BF.folders && BF.folders.renderTabs) BF.folders.renderTabs(chats);
        var filtered = BF.folders && BF.folders.filterChats ? BF.folders.filterChats(chats) : chats;

        var focusedKey = focusedRowKey();
        var focusedIndex = focusedKey !== null ? indexById.get(focusedKey) : undefined;

        visibleChats = [];
        indexById = new Map();
        filtered.forEach(function (chat) {
            var key = String(chat.id);
            if (indexById.has(key)) return; // offset-пагинация может вернуть чат повторно
            indexById.set(key, visibleChats.length);
            visibleChats.push(chat);
        });

        var currentChatId = deps.getCurrentChatId();
        var activeKey = currentChatId == null ? null : String(currentChatId);
        var refocus = false;
        if (focusedKey !== null && !indexById.has(focusedKey) && visibleChats.length > 0) {
            // Чат с фокусом исчез (фильтр папки, удаление) — фокус переходит соседу.
            roverId = String(visibleChats[Math.min(focusedIndex || 0, visibleChats.length - 1)].id);
            refocus = true;
        } else if (roverId === null || !indexById.has(roverId)) {
            roverId =
                activeKey !== null && indexById.has(activeKey)
                    ? activeKey
                    : visibleChats.length > 0
                      ? String(visibleChats[0].id)
                      : null;
        }

        if (activeKey !== lastActiveId) {
            lastActiveId = activeKey;
            // Чат открыт извне списка (палитра, push, deep-link, newchat) — показать его строку.
            if (activeKey !== null && indexById.has(activeKey)) {
                if (focusedKey === null) roverId = activeKey;
                revealIndex(indexById.get(activeKey), true);
            }
        }

        renderWindow();
        if (refocus) focusRow(indexById.get(roverId));
    }

    function revealIndex(index, center) {
        var viewH = chatListEl.clientHeight - NAV;
        if (viewH <= 0) return;
        var top = index * ROW;
        var scrollTop = chatListEl.scrollTop;
        if (center) {
            if (top + ROW > scrollTop && top < scrollTop + viewH) return; // строка уже видна
            chatListEl.scrollTop = Math.max(0, top - (viewH - ROW) / 2);
        } else if (top < scrollTop) {
            chatListEl.scrollTop = top;
        } else if (top + ROW > scrollTop + viewH) {
            chatListEl.scrollTop = top + ROW - viewH;
        }
    }

    function scheduleWindow() {
        if (windowFramePending) return;
        windowFramePending = true;
        requestAnimationFrame(renderWindow);
    }

    function createRow() {
        var el = document.createElement('div');
        el.className = 'chat-item';
        el.setAttribute('role', 'option');
        el.tabIndex = -1;
        return el;
    }

    // Синхронизирует DOM с окном прокрутки. Узлы строк переиспользуются по chat.id;
    // строка с фокусом не удаляется и не переносится (перенос узла снимает фокус).
    function renderWindow() {
        windowFramePending = false;
        var n = visibleChats.length;
        spacer.style.height = n * ROW + 'px';
        var viewH = chatListEl.clientHeight;
        if (!viewH) return; // список скрыт (открыт поиск) — отрисуем, когда снова появится

        var scrollTop = chatListEl.scrollTop;
        var first = Math.max(0, Math.floor(scrollTop / ROW) - OVERSCAN);
        var last = Math.min(n - 1, Math.ceil((scrollTop + viewH) / ROW) + OVERSCAN);
        var focusedKey = focusedRowKey();
        var needed = new Set();
        for (var i = first; i <= last; i++) needed.add(String(visibleChats[i].id));
        if (roverId !== null && indexById.has(roverId)) needed.add(roverId);
        if (focusedKey !== null && indexById.has(focusedKey)) needed.add(focusedKey);

        rows.forEach(function (row, key) {
            if (needed.has(key)) return;
            row.el.remove();
            rows.delete(key);
        });

        var activeKey = deps.getCurrentChatId() == null ? null : String(deps.getCurrentChatId());
        var focusedEl = focusedKey !== null && rows.has(focusedKey) ? rows.get(focusedKey).el : null;
        var ordered = Array.from(needed)
            .map(function (key) {
                return indexById.get(key);
            })
            .sort(function (a, b) {
                return a - b;
            });
        var prev = null;
        ordered.forEach(function (index) {
            var chat = visibleChats[index];
            var key = String(chat.id);
            var row = rows.get(key);
            if (!row) {
                row = { el: createRow(), html: null, top: -1, index: -1 };
                rows.set(key, row);
            }
            var el = row.el;
            el.dataset.chatId = chat.id;
            var html = rowHtml(chat);
            if (html !== row.html) {
                el.innerHTML = html;
                row.html = html;
                el.setAttribute('aria-label', rowLabel(el, chat));
            }
            var top = index * ROW;
            if (top !== row.top) {
                el.style.transform = 'translateY(' + top + 'px)';
                row.top = top;
            }
            if (index !== row.index) {
                el.setAttribute('aria-posinset', String(index + 1));
                row.index = index;
            }
            el.setAttribute('aria-setsize', String(n));
            var isActive = key === activeKey;
            el.classList.toggle('active', isActive);
            el.setAttribute('aria-selected', isActive ? 'true' : 'false');
            el.tabIndex = key === roverId ? 0 : -1;

            // Порядок в DOM — по индексу (режим обзора скринридера), кроме узла с фокусом.
            var expectedNext = prev ? prev.nextSibling : spacer.firstChild;
            if (el !== expectedNext && el !== focusedEl) spacer.insertBefore(el, expectedNext);
            prev = el;
        });
    }

    function focusRow(index) {
        var chat = visibleChats[index];
        if (!chat) return;
        roverId = String(chat.id);
        revealIndex(index, false);
        renderWindow();
        var row = rows.get(roverId);
        if (row) row.el.focus({ preventScroll: true });
    }

    function onListKeydown(e) {
        var item = e.target;
        if (!item || !item.classList || !item.classList.contains('chat-item')) return;
        var index = indexById.get(String(item.dataset.chatId));
        if (index === undefined) return;
        var n = visibleChats.length;
        var page = Math.max(1, Math.floor((chatListEl.clientHeight - NAV) / ROW) - 1);
        if ((e.shiftKey && e.key === 'F10') || e.key === 'ContextMenu') {
            e.preventDefault();
            var rect = item.getBoundingClientRect();
            suppressContextMenuUntil = Date.now() + 500;
            openChatContextMenu(
                rect.left + 16,
                Math.min(Math.max(rect.bottom, 8), window.innerHeight - 8),
                item.dataset.chatId,
                true
            );
            return;
        }
        var target;
        switch (e.key) {
            case 'ArrowDown':
                target = index + 1;
                break;
            case 'ArrowUp':
                target = index - 1;
                break;
            case 'Home':
                target = 0;
                break;
            case 'End':
                target = n - 1;
                break;
            case 'PageDown':
                target = index + page;
                break;
            case 'PageUp':
                target = index - page;
                break;
            case 'Enter':
            case ' ':
                e.preventDefault();
                deps.openChat(visibleChats[index].id);
                if (window.__mobileShowChat) window.__mobileShowChat();
                return;
            default:
                return;
        }
        e.preventDefault();
        focusRow(Math.max(0, Math.min(n - 1, target)));
    }

    function chatFromEvent(e) {
        var item = e.target && e.target.closest ? e.target.closest('.chat-item') : null;
        if (!item) return null;
        var index = indexById.get(String(item.dataset.chatId));
        return index === undefined ? null : visibleChats[index];
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
        var removedIdsAtRequest = new Set(removedChatIds);
        var totalKnownAtRequest = chatListTotalKnown;

        var request = BF.api
            .listChats(0, 50)
            .then(function (data) {
                if (!data || !data.chats) return false;
                var filtered = filterRemovedChats(
                    data.chats,
                    data.totalCount,
                    removedIdsAtRequest,
                    totalKnownAtRequest
                );
                var totalCount = filtered.totalCount;
                var fetched = filtered.chats;
                chatListTotalKnown = true;
                fetched.sort(function (a, b) {
                    var bt = (b.lastMessage && b.lastMessage.sentAt) || b.lastActivityAt || 0;
                    var at = (a.lastMessage && a.lastMessage.sentAt) || a.lastActivityAt || 0;
                    return bt - at;
                });

                var chats = deps.getChats();
                var same = totalCount === chatListTotal && fetched.length <= chats.length;
                if (same) {
                    for (var i = 0; i < fetched.length; i++) {
                        if (chatSignature(fetched[i]) !== chatSignature(chats[i])) {
                            same = false;
                            break;
                        }
                    }
                }
                if (same) return true; // ничего не изменилось — DOM не трогаем

                serverCountedChatIds.clear();
                rememberServerCountedChats(fetched, false);
                deps.setChats(fetched);
                chatListTotal = totalCount;
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
                return completeListRequest(request, result);
            });
        chatListRequest = request;
        return request;
    }

    function refreshChatListTotal() {
        if (chatListLoading) return chatListRequest || Promise.resolve(false);
        chatListLoading = true;
        var removedIdsAtRequest = new Set(removedChatIds);
        var totalKnownAtRequest = chatListTotalKnown;

        var request = BF.api
            .listChats(0, 50)
            .then(function (data) {
                if (!data || !data.chats) return false;
                var filtered = filterRemovedChats(
                    data.chats,
                    data.totalCount,
                    removedIdsAtRequest,
                    totalKnownAtRequest
                );
                chatListTotal = filtered.totalCount;
                chatListTotalKnown = true;
                return true;
            })
            .catch(function () {
                return false;
            })
            .then(function (result) {
                return completeListRequest(request, result);
            });
        chatListRequest = request;
        return request;
    }

    // ========== CHAT CONTEXT MENU (PCM на чате в сайдбаре) ==========

    function contextMenuIcon() {
        return '<span class="cm-icon">' + BF.icons.html('chat-folders') + '</span>';
    }

    function menuItem(act, folderId, label) {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'cm-item';
        btn.setAttribute('role', 'menuitem');
        btn.tabIndex = -1;
        btn.dataset.act = act;
        if (folderId) btn.dataset.folderId = folderId;
        btn.innerHTML = contextMenuIcon() + '<span class="cm-label">' + u.escapeHtml(label) + '</span>';
        return btn;
    }

    // Секция меню: role="group" с подписью; сам заголовок скрыт от скринридера, чтобы не читался дважды.
    function appendFolderSection(titleKey, act, folders) {
        var group = document.createElement('div');
        group.setAttribute('role', 'group');
        group.setAttribute('aria-label', BF.i18n.t(titleKey));
        var hdr = document.createElement('div');
        hdr.className = 'cm-section-title';
        hdr.setAttribute('aria-hidden', 'true');
        hdr.textContent = BF.i18n.t(titleKey);
        group.appendChild(hdr);
        folders.forEach(function (f) {
            group.appendChild(menuItem(act, f.folderId, f.folderName || BF.i18n.t('folder.default')));
        });
        chatContextMenu.appendChild(group);
    }

    function buildChatContextMenu(chatId) {
        if (!chatContextMenu) return;
        chatContextMenu.innerHTML = '';

        if (BF.folders) {
            var without = BF.folders.getFoldersWithoutChat(chatId);
            var inFolders = BF.folders.getFoldersForChat(chatId);

            if (without.length > 0) appendFolderSection('folder.addToFolder', 'add-folder', without);
            if (inFolders.length > 0) appendFolderSection('folder.removeFromFolder', 'remove-folder', inFolders);

            if (without.length > 0 || inFolders.length > 0) {
                var sep = document.createElement('div');
                sep.className = 'cm-separator';
                sep.setAttribute('role', 'separator');
                chatContextMenu.appendChild(sep);
            }
        }

        chatContextMenu.appendChild(menuItem('create-folder', null, BF.i18n.t('folder.create')));

        var chat = deps.getChats().find(function (c) {
            return c.id === chatId;
        });
        if (chat) {
            var sep2 = document.createElement('div');
            sep2.className = 'cm-separator';
            sep2.setAttribute('role', 'separator');
            chatContextMenu.appendChild(sep2);

            var dangerBtn = menuItem(
                chat.isGroupChat ? 'leave-chat' : 'delete-chat',
                null,
                BF.i18n.t(chat.isGroupChat ? 'chat.leave' : 'chat.delete')
            );
            dangerBtn.classList.add('cm-danger');
            chatContextMenu.appendChild(dangerBtn);
        }
    }

    // Личный/приватный (не группа, не fed-DM) — показываем чекбокс "удалить также для {имя}".
    function openDeleteChatDialog(chatId) {
        var chat = deps.getChats().find(function (c) {
            return c.id === chatId;
        });
        if (!chat || !deleteChatOverlay) return;

        var isGroup = chat.isGroupChat;
        deleteChatTitle.textContent = BF.i18n.t(isGroup ? 'dialog.leaveChat.title' : 'dialog.deleteChat.title');
        deleteChatText.textContent = BF.i18n.t(isGroup ? 'dialog.leaveChat.text' : 'dialog.deleteChat.text');
        var okLabel = BF.i18n.t(isGroup ? 'chat.leave' : 'common.delete');
        var pendingLabel = BF.i18n.t(isGroup ? 'dialog.leaveChat.pending' : 'dialog.deleteChat.pending');
        deleteChatOk.textContent = okLabel;
        deleteChatOk.disabled = false;
        deleteChatOk.classList.remove('is-loading');
        deleteChatOk.removeAttribute('aria-busy');
        deleteChatForEveryoneCheckbox.disabled = false;
        if (deleteChatCancel) deleteChatCancel.disabled = false;

        var showCheckbox = !isGroup && !chat.isFederated;
        deleteChatForEveryoneCheckbox.checked = false;
        deleteChatForEveryoneRow.style.display = showCheckbox ? '' : 'none';
        if (showCheckbox) {
            deleteChatForEveryoneLabel.textContent = BF.i18n.t('dialog.deleteChat.forEveryone', {
                name: chat.title || ''
            });
        }

        BF.utils.openOverlay(deleteChatOverlay);

        deleteChatOk.onclick = function () {
            if (deleteChatOk.disabled) return;
            var deleteForEveryone = deleteChatForEveryoneCheckbox.checked;
            deleteChatOk.disabled = true;
            deleteChatOk.textContent = pendingLabel;
            deleteChatOk.classList.add('is-loading');
            deleteChatOk.setAttribute('aria-busy', 'true');
            deleteChatForEveryoneCheckbox.disabled = true;
            if (deleteChatCancel) deleteChatCancel.disabled = true;

            var action = Promise.resolve().then(function () {
                return isGroup ? BF.api.leaveChat(chatId) : BF.api.deleteChat(chatId, deleteForEveryone);
            });
            action
                .then(function () {
                    removeChat(chatId);
                })
                .catch(function () {
                    if (deps.showToast)
                        deps.showToast(BF.i18n.t(isGroup ? 'error.leaveChat' : 'error.deleteChat'), true);
                })
                .finally(function () {
                    deleteChatOk.disabled = false;
                    deleteChatOk.textContent = okLabel;
                    deleteChatOk.classList.remove('is-loading');
                    deleteChatOk.removeAttribute('aria-busy');
                    deleteChatForEveryoneCheckbox.disabled = false;
                    if (deleteChatCancel) deleteChatCancel.disabled = false;
                    BF.utils.closeOverlay(deleteChatOverlay);
                    deleteChatOk.onclick = null;
                });
        };
    }

    // keyboard — открыто клавишей ContextMenu/Shift+F10: фокус сразу на первый пункт.
    function openChatContextMenu(x, y, chatId, keyboard) {
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

        chatCmReturnChatId = keyboard ? String(chatId) : null;
        chatCmReturnEl = keyboard ? null : document.activeElement;
        if (keyboard) u.focusMenuItem(chatContextMenu, 0);
    }

    function closeChatContextMenu() {
        if (!chatContextMenu) return;
        // Проверяем до скрытия: после display:none фокус уже на body.
        var hadFocus = chatContextMenu.contains(document.activeElement);
        chatContextMenu.classList.remove('visible');
        chatCmTargetId = null;
        var returnChatId = chatCmReturnChatId;
        var returnEl = chatCmReturnEl;
        chatCmReturnChatId = null;
        chatCmReturnEl = null;
        if (!hadFocus) return;
        // Строку могли переиспользовать или убрать из окна — возвращаемся по id чата.
        if (returnChatId !== null && indexById.has(returnChatId)) focusRow(indexById.get(returnChatId));
        else if (returnEl && document.contains(returnEl) && typeof returnEl.focus === 'function')
            returnEl.focus({ preventScroll: true });
    }

    function init(options) {
        deps = options;
        u = BF.utils;
        chatListEl = $('#chatList');
        chatContextMenu = $('#chatContextMenu');
        deleteChatOverlay = $('#deleteChatConfirmOverlay');
        deleteChatTitle = $('#deleteChatTitle');
        deleteChatText = $('#deleteChatText');
        deleteChatForEveryoneRow = $('#deleteChatForEveryoneRow');
        deleteChatForEveryoneLabel = $('#deleteChatForEveryoneLabel');
        deleteChatForEveryoneCheckbox = $('#deleteChatForEveryone');
        deleteChatOk = $('#deleteChatOk');
        deleteChatCancel = $('#deleteChatCancel');
        if (deleteChatCancel) {
            deleteChatCancel.addEventListener('click', function () {
                if (deleteChatOk && deleteChatOk.disabled) return;
                if (deleteChatOverlay) BF.utils.closeOverlay(deleteChatOverlay);
                if (deleteChatOk) deleteChatOk.onclick = null;
            });
        }
        if (deleteChatOverlay) {
            deleteChatOverlay.addEventListener('click', function (e) {
                if (e.target === deleteChatOverlay && (!deleteChatOk || !deleteChatOk.disabled)) {
                    BF.utils.closeOverlay(deleteChatOverlay);
                    if (deleteChatOk) deleteChatOk.onclick = null;
                }
            });
        }
        if (BF.realtime) {
            BF.realtime.on('chat_hidden', function (data) {
                removeChat(data.chatId);
            });
        }
        spacer = document.createElement('div');
        spacer.className = 'chat-list-spacer';
        spacer.setAttribute('role', 'none');
        chatListEl.appendChild(spacer);
        readMetrics();

        chatListEl.addEventListener(
            'scroll',
            function () {
                scheduleWindow();
                if (chatListEl.scrollTop + chatListEl.clientHeight >= chatListEl.scrollHeight - 100) load();
            },
            { passive: true }
        );
        // Смена размеров (в т.ч. возврат из скрытого состояния после поиска) — перечитать метрики и окно.
        var onResize = function () {
            readMetrics();
            scheduleWindow();
        };
        if (window.ResizeObserver) new ResizeObserver(onResize).observe(chatListEl);
        else window.addEventListener('resize', onResize);

        chatListEl.addEventListener('click', function (e) {
            var chat = chatFromEvent(e);
            if (chat) deps.openChat(chat.id);
        });
        chatListEl.addEventListener('keydown', onListKeydown);
        chatListEl.addEventListener('focusin', function (e) {
            var item = e.target && e.target.closest ? e.target.closest('.chat-item') : null;
            if (!item) return;
            roverId = String(item.dataset.chatId);
            rows.forEach(function (row, key) {
                row.el.tabIndex = key === roverId ? 0 : -1;
            });
        });

        if (chatListEl) {
            chatListEl.addEventListener('contextmenu', function (e) {
                var item = e.target.closest('.chat-item');
                if (!item || !item.dataset.chatId) return;
                e.preventDefault();
                if (Date.now() < suppressContextMenuUntil) return;
                openChatContextMenu(e.clientX, e.clientY, item.dataset.chatId);
            });
        }

        if (chatContextMenu) {
            chatContextMenu.addEventListener('keydown', function (e) {
                u.handleMenuKeydown(e, chatContextMenu, closeChatContextMenu);
            });
            chatContextMenu.addEventListener('contextmenu', function (e) {
                e.preventDefault();
            });
            chatContextMenu.addEventListener('click', function (e) {
                var btn = e.target.closest('button[data-act]');
                if (!btn) return;
                var act = btn.dataset.act;
                var folderId = btn.dataset.folderId || '';
                var chatId = chatCmTargetId;
                closeChatContextMenu();
                if (act === 'delete-chat' || act === 'leave-chat') {
                    if (chatId) openDeleteChatDialog(chatId);
                    return;
                }
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
            if (!chatContextMenu || !chatContextMenu.classList.contains('visible')) return;
            if (e.key === 'Escape') closeChatContextMenu();
            // Меню открыто мышью — стрелки переводят фокус в него.
            else if (
                (e.key === 'ArrowDown' || e.key === 'ArrowUp') &&
                !chatContextMenu.contains(document.activeElement)
            ) {
                e.preventDefault();
                u.focusMenuItem(chatContextMenu, e.key === 'ArrowDown' ? 0 : -1);
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
