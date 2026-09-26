/**
 * Message feed of the open chat: rendering, sliding window (MAX_MESSAGES in the DOM), loading older/newer
 * pages on scroll, the scroll-to-bottom button and jumping to a message.
 * A11y: ARIA feed (role="article" per message, roving tabindex, arrow/Page/Home/End keys, aria-busy) and a
 * polite live region announcing new incoming messages of the open chat.
 * The message buffer itself (`messages`) stays in main.js and is reached through deps.getMessages/setMessages.
 * Requires: BF.api, BF.files, BF.i18n, BF.messages, BF.utils
 * Exposes: BF.feed
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var u;
    var messagesArea;
    var messagesInner;
    var loadingMessages;
    var scrollToBottomBtn;
    var scrollBadge;
    var liveRegion;

    var MAX_MESSAGES = 200; // скользящее окно ленты: сколько сообщений держим в DOM
    var isLoadingOlder = false;
    var noMoreOlder = false;
    var isLoadingNewer = false;
    var hasNewerGap = false; // хвост буфера обрезан — окно не доходит до конца чата
    var isJumpingToTail = false;
    var isJumpingToMessage = false;
    var resyncSeparatorId = null; // id первого сообщения после resync-пропуска (разделитель «Новые сообщения»)
    var newMessagesBelowCount = 0;
    var roverId = null; // сообщение с tabindex=0 (roving tabindex)
    var busyCount = 0;
    var announceQueue = [];
    var announceTimer = null;

    function $(selector) {
        return document.querySelector(selector);
    }

    // Состояние окна при открытии другого чата.
    function reset() {
        noMoreOlder = false;
        hasNewerGap = false;
        isLoadingNewer = false;
        isJumpingToTail = false;
        resyncSeparatorId = null;
        roverId = null;
        if (scrollToBottomBtn) scrollToBottomBtn.classList.remove('visible');
        newMessagesBelowCount = 0;
        updateScrollBadge();
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
            setTimeout(function () {
                el.classList.remove('highlight');
            }, 1500);
        }
        resettleAfterImages(anchor);
    }

    // Повторяет anchor, когда догрузятся картинки сообщений — без этого reflow
    // от картинок сбивает позицию скролла после открытия чата или прыжка к сообщению.
    function resettleAfterImages(anchor) {
        var pending = Array.prototype.filter.call(messagesInner.querySelectorAll('img'), function (im) {
            return !im.complete;
        });
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
                a.forwardedMessage.attachments.forEach(function (ia) {
                    inner.push(ia);
                });
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

    function buildElement(msg, index) {
        return BF.messages
            .buildMessageElement(
                msg,
                deps.getMyUserId(),
                deps.getUser,
                deps.showMediaOverlay,
                buildMessageOptions(msg, index)
            )
            .then(function (el) {
                return decorate(el, msg);
            });
    }

    function render() {
        // Фокус в ленте переживает полную перерисовку: то же сообщение, иначе сосед, иначе последнее.
        var focused = focusedArticle();
        var restoreIds = focused
            ? [focused, articleSibling(focused, true), articleSibling(focused, false)]
                  .filter(Boolean)
                  .map(function (el) {
                      return el.dataset.msgId;
                  })
            : null;
        messagesInner.innerHTML = '';
        var rendered = prefetchAttachmentUrls(deps.getMessages()).then(function () {
            var chain = Promise.resolve();
            var lastDate = null;
            var currentChatType = deps.getCurrentChatType();
            var currentChatInfo = deps.getCurrentChatInfo();
            // Разделитель непрочитанных: якорь resync-догрузки («Новые сообщения»)
            // важнее первого непрочитанного из chat info. Приватные чаты якорь не
            // ставят (их resync идёт мимо resyncCurrentChatTail) — игнорируем.
            var sepId =
                currentChatType !== 1 && resyncSeparatorId
                    ? resyncSeparatorId
                    : currentChatInfo && currentChatInfo.firstUnreadMessageId;
            var sepKey = currentChatType !== 1 && resyncSeparatorId ? 'chat.newMessages' : 'chat.unreadMessages';
            deps.getMessages().forEach(function (msg, index) {
                chain = chain.then(function () {
                    var msgDate = u.formatDate(msg.sentAt);
                    if (msgDate !== lastDate) {
                        lastDate = msgDate;
                        messagesInner.appendChild(makeDateSeparator(msgDate));
                    }
                    if (sepId && Number(msg.id) === Number(sepId)) {
                        messagesInner.appendChild(makeUnreadSeparator(sepKey));
                    }
                    return buildElement(msg, index).then(function (el) {
                        el.dataset.date = msgDate;
                        messagesInner.appendChild(el);
                    });
                });
            });
            return chain;
        });
        return trackBusy(
            rendered.then(function () {
                if (restoreIds && canRestoreFocus()) {
                    for (var i = 0; i < restoreIds.length; i++) {
                        var el = findMessageGroup(restoreIds[i]);
                        if (el) return focusArticle(el, false);
                    }
                    return focusArticle(lastArticle(), false);
                }
                syncRover();
            })
        );
    }

    // Дорисовывает подгруженные старые сообщения перед лентой, не перестраивая её целиком.
    // Вызывается после того, как newMsgs уже добавлены в начало массива messages.
    function prependMessages(newMsgs) {
        var firstOldEl = messagesInner.firstElementChild;
        var oldFirstMsg = deps.getMessages()[newMsgs.length] || null;
        var lastDate = null;

        return prefetchAttachmentUrls(newMsgs)
            .then(function () {
                var frag = document.createDocumentFragment();
                var chain = Promise.resolve();
                newMsgs.forEach(function (msg, index) {
                    chain = chain.then(function () {
                        var msgDate = u.formatDate(msg.sentAt);
                        if (msgDate !== lastDate) {
                            lastDate = msgDate;
                            frag.appendChild(makeDateSeparator(msgDate));
                        }
                        return buildElement(msg, index).then(function (el) {
                            el.dataset.date = msgDate;
                            frag.appendChild(el);
                        });
                    });
                });
                return chain.then(function () {
                    return frag;
                });
            })
            .then(function (frag) {
                // Разделитель даты бывшего первого сообщения теперь дублирует вставленный блок.
                if (
                    lastDate &&
                    firstOldEl &&
                    firstOldEl.classList.contains('msg-date-separator') &&
                    firstOldEl.dataset.date === lastDate
                )
                    firstOldEl.remove();
                messagesInner.insertBefore(frag, messagesInner.firstChild);
                syncRover();

                // Группировка бывшего первого сообщения могла измениться: перед ним появился сосед.
                if (!oldFirstMsg || !canGroupMessages(newMsgs[newMsgs.length - 1], oldFirstMsg)) return;
                return buildMessageViewElement(oldFirstMsg).then(function (replacement) {
                    var el = findMessageGroup(oldFirstMsg.id);
                    if (!el || !el.isConnected) return;
                    replacement.dataset.date = el.dataset.date;
                    replaceElement(el, replacement);
                });
            });
    }

    // Скользящее окно: держим в буфере не больше MAX_MESSAGES сообщений.
    // 'tail' — после подгрузки старых, 'head' — после подгрузки новых.
    function trimMessages(side) {
        var messages = deps.getMessages();
        var extra = messages.length - MAX_MESSAGES;
        if (extra <= 0) return;

        var dropped = side === 'head' ? messages.splice(0, extra) : messages.splice(messages.length - extra, extra);

        var droppedIds = new Set(
            dropped.map(function (msg) {
                return String(msg.id);
            })
        );
        var focused = focusedArticle();
        var lostFocus = !!focused && droppedIds.has(String(focused.dataset.msgId));
        Array.prototype.slice.call(messagesInner.querySelectorAll('.msg-group')).forEach(function (node) {
            if (droppedIds.has(String(node.dataset.msgId))) node.remove();
        });
        removeOrphanSeparators();
        // Сообщение с фокусом ушло из окна — фокус на ближайшее оставшееся.
        if (lostFocus) focusArticle(side === 'head' ? firstArticle() : lastArticle(), false);
        else syncRover();

        // Обрезав голову, снимаем флаг «старее ничего нет»: отрезанное снова можно догрузить.
        if (side === 'head') noMoreOlder = false;
        else hasNewerGap = true;
    }

    function removeOrphanSeparators() {
        Array.prototype.slice
            .call(messagesInner.querySelectorAll('.msg-date-separator, .msg-unread-separator'))
            .forEach(function (sep) {
                var next = sep.nextElementSibling;
                if (!next || next.classList.contains('msg-date-separator')) {
                    if (sep.dataset.sepKey === 'chat.newMessages') resyncSeparatorId = null;
                    sep.remove();
                }
            });
    }

    function scrollToBottom() {
        if (hasNewerGap) {
            jumpToLiveTail();
            return;
        }
        messagesArea.scrollTop = messagesArea.scrollHeight;
    }

    // Возврат к живому хвосту, когда скользящее окно обрезало последние сообщения.
    function jumpToLiveTail() {
        var chatId = deps.getCurrentChatId();
        if (isJumpingToTail || isJumpingToMessage || !chatId) return Promise.resolve();
        isJumpingToTail = true;

        return loadMessagesPage(chatId, 0, 30, 0)
            .then(function (data) {
                if (chatId !== deps.getCurrentChatId() || !data || !data.messages) return;
                deps.setMessages(data.messages);
                deps.mergePendingUploads(chatId);
                hasNewerGap = false;
                noMoreOlder = false;
                resyncSeparatorId = null; // окно снова на живом хвосте — границы «нового» нет
                return render().then(function () {
                    messagesArea.scrollTop = messagesArea.scrollHeight;
                });
            })
            .finally(function () {
                isJumpingToTail = false;
            });
    }

    function updateScrollBadge() {
        if (!scrollBadge) return;
        scrollBadge.textContent = newMessagesBelowCount > 0 ? String(newMessagesBelowCount) : '';
        scrollBadge.style.display = newMessagesBelowCount > 0 ? 'flex' : 'none';
    }

    // Новое сообщение пришло, пока пользователь не у нижнего края.
    function incrementNewBelow() {
        newMessagesBelowCount++;
        updateScrollBadge();
    }

    function buildMessageViewElement(msg) {
        var atts = (msg.content && msg.content.attachments) || [];
        var fileIds = atts
            .map(function (a) {
                return a.fileId;
            })
            .filter(function (id) {
                return id && !BF.files.getCachedFileUrl(id);
            });
        collectFwdAttachments(msg).forEach(function (a) {
            if (a.fileId && !BF.files.getCachedFileUrl(a.fileId)) fileIds.push(a.fileId);
        });
        var p = fileIds.length > 0 ? BF.files.getFileUrls(fileIds) : Promise.resolve();

        return p.then(function () {
            return buildElement(msg);
        });
    }

    function canGroupMessages(previous, current) {
        if (
            !previous ||
            !current ||
            previous.type === 2 ||
            previous.type === 'SYSTEM' ||
            current.type === 2 ||
            current.type === 'SYSTEM'
        )
            return false;
        if (previous.senderId !== current.senderId || !previous.sentAt || !current.sentAt) return false;
        return (
            current.sentAt >= previous.sentAt &&
            current.sentAt - previous.sentAt <= 5 * 60 * 1000 &&
            u.formatDate(previous.sentAt) === u.formatDate(current.sentAt)
        );
    }

    function buildMessageOptions(msg, index) {
        var messages = deps.getMessages();
        if (index == null) index = messages.indexOf(msg);
        if (index < 0)
            index = messages.findIndex(function (item) {
                return item.id === msg.id;
            });

        var previous = index > 0 ? messages[index - 1] : null;
        var next = index >= 0 && index < messages.length - 1 ? messages[index + 1] : null;
        var groupedWithPrevious = canGroupMessages(previous, msg);
        var currentChatInfo = deps.getCurrentChatInfo();
        var showSenderGutter =
            !!(currentChatInfo && currentChatInfo.isGroupChat) && msg.senderId !== deps.getMyUserId();
        return {
            onReplyClick: scrollToMessage,
            onPendingCancel: deps.onPendingCancel,
            onPendingRetry: deps.onPendingRetry,
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
            var messages = deps.getMessages();
            var gapIdx = messages.findIndex(function (m) {
                return String(m.id) === String(msg.id);
            });
            if (gapIdx >= 0) messages.splice(gapIdx, 1);
            return Promise.resolve();
        }
        return appendMessageElement(msg, separatorKey);
    }

    function appendMessageElement(msg, separatorKey) {
        var messages = deps.getMessages();
        var currentChatInfo = deps.getCurrentChatInfo();
        var previous = messages.length > 1 ? messages[messages.length - 2] : null;
        var refreshPrevious =
            previous &&
            currentChatInfo &&
            currentChatInfo.isGroupChat &&
            previous.senderId !== deps.getMyUserId() &&
            canGroupMessages(previous, msg)
                ? buildMessageViewElement(previous).then(function (replacement) {
                      var previousEl = findMessageGroup(previous.id);
                      if (!previousEl || !previousEl.isConnected) return;
                      replacement.dataset.date = previousEl.dataset.date;
                      replaceElement(previousEl, replacement);
                  })
                : Promise.resolve();

        return refreshPrevious.then(function () {
            return buildMessageViewElement(msg).then(function (el) {
                var msgDate = u.formatDate(msg.sentAt);
                var lastMsgDate = null;
                for (var node = messagesInner.lastElementChild; node; node = node.previousElementSibling) {
                    if (node.dataset && node.dataset.date) {
                        lastMsgDate = node.dataset.date;
                        break;
                    }
                }
                if (msgDate !== lastMsgDate) messagesInner.appendChild(makeDateSeparator(msgDate));
                if (separatorKey) messagesInner.appendChild(makeUnreadSeparator(separatorKey));
                el.dataset.date = msgDate;
                messagesInner.appendChild(el);
                syncRover();
            });
        });
    }

    function findMessageGroup(messageId) {
        return Array.prototype.find.call(messagesInner.querySelectorAll('.msg-group'), function (node) {
            return String(node.dataset.msgId) === String(messageId);
        });
    }

    // ========== A11Y: ARIA feed, roving tabindex, live region ==========

    function senderName(user) {
        return user ? ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || user.username || '' : '';
    }

    // role="article" и подпись «автор, время»; содержимое читается через aria-describedby на пузырь.
    // Ставится только здесь, а не в messages.js: buildMessageElement рисует и список закреплённых.
    function decorate(el, msg) {
        el.setAttribute('role', 'article');
        if (el.classList.contains('msg-system')) {
            el.setAttribute('aria-label', (msg.content && msg.content.text) || '');
            return el;
        }
        var time = u.formatTime(msg.sentAt);
        var bubble = el.querySelector('.msg-bubble');
        if (bubble) {
            bubble.id = 'feed-msg-' + msg.id;
            el.setAttribute('aria-describedby', bubble.id);
        }
        if (msg.senderId === deps.getMyUserId()) {
            el.setAttribute('aria-label', BF.i18n.t('call.you') + ', ' + time);
        } else {
            el.setAttribute('aria-label', time);
            Promise.resolve(deps.getUser(msg.senderId))
                .then(function (user) {
                    var name = senderName(user);
                    if (name) el.setAttribute('aria-label', name + ', ' + time);
                })
                .catch(function () {});
        }
        return el;
    }

    function articleSibling(el, forward) {
        var node = forward ? el.nextElementSibling : el.previousElementSibling;
        for (; node; node = forward ? node.nextElementSibling : node.previousElementSibling) {
            if (node.classList.contains('msg-group')) return node;
        }
        return null;
    }

    function firstArticle() {
        return messagesInner.querySelector('.msg-group');
    }

    function lastArticle() {
        var list = messagesInner.querySelectorAll('.msg-group');
        return list[list.length - 1] || null;
    }

    function focusedArticle() {
        var active = document.activeElement;
        if (!active || !messagesInner.contains(active) || !active.closest) return null;
        return active.closest('.msg-group');
    }

    // Пока лента перерисовывалась, пользователь мог уйти в другое место — тогда фокус не трогаем.
    function canRestoreFocus() {
        var active = document.activeElement;
        return !active || active === document.body || messagesInner.contains(active);
    }

    // Roving tabindex: tabindex=0 только у одного сообщения, у остальных атрибута нет —
    // иначе клик мышью по любому сообщению уводил бы в него фокус.
    function syncRover() {
        var list = messagesInner.querySelectorAll('.msg-group');
        var rover = null;
        for (var i = 0; i < list.length; i++) {
            if (String(list[i].dataset.msgId) === roverId) rover = list[i];
        }
        if (!rover && list.length > 0) rover = list[list.length - 1];
        roverId = rover ? String(rover.dataset.msgId) : null;
        for (var j = 0; j < list.length; j++) {
            if (list[j] === rover) list[j].tabIndex = 0;
            else list[j].removeAttribute('tabindex');
        }
    }

    function focusArticle(el, scroll) {
        if (!el) return;
        roverId = String(el.dataset.msgId);
        syncRover();
        el.focus({ preventScroll: true });
        if (scroll !== false) el.scrollIntoView({ block: 'nearest' });
    }

    // Замена узла сообщения (pending → серверное, правка, перегруппировка) с сохранением ровера и фокуса.
    function replaceElement(oldEl, newEl) {
        var hadFocus = oldEl.contains(document.activeElement);
        if (String(oldEl.dataset.msgId) === roverId) roverId = String(newEl.dataset.msgId);
        oldEl.replaceWith(newEl);
        if (hadFocus) focusArticle(newEl, false);
        else syncRover();
    }

    function removeElement(el) {
        var neighbor = el.contains(document.activeElement)
            ? articleSibling(el, true) || articleSibling(el, false)
            : null;
        el.remove();
        if (neighbor) focusArticle(neighbor, false);
        else syncRover();
    }

    function trackBusy(promise) {
        busyCount++;
        messagesInner.setAttribute('aria-busy', 'true');
        return promise.finally(function () {
            busyCount = Math.max(0, busyCount - 1);
            if (busyCount === 0) messagesInner.removeAttribute('aria-busy');
        });
    }

    // Сообщение примерно на экран выше/ниже текущего.
    function pageTarget(el, down) {
        var limit = messagesArea.clientHeight * 0.8;
        var target = el;
        for (var node = articleSibling(el, down); node; node = articleSibling(node, down)) {
            target = node;
            if (Math.abs(node.offsetTop - el.offsetTop) >= limit) break;
        }
        return target;
    }

    // Клавиатура на сообщении (паттерн ARIA feed): стрелки — соседнее сообщение (у края догружается
    // страница), PageUp/PageDown — примерно на экран, Home/End — края окна (End — к живому хвосту).
    function onFeedKeydown(e) {
        var el = e.target;
        if (!el.classList || !el.classList.contains('msg-group') || e.altKey || e.ctrlKey || e.metaKey) return;
        var target;
        switch (e.key) {
            case 'ArrowDown':
                target = articleSibling(el, true);
                if (target) focusArticle(target);
                else
                    loadNewerMessages().then(function () {
                        focusArticle(articleSibling(el, true));
                    });
                break;
            case 'ArrowUp':
                target = articleSibling(el, false);
                if (target) focusArticle(target);
                else
                    loadOlderMessages().then(function () {
                        focusArticle(articleSibling(el, false));
                    });
                break;
            case 'PageDown':
            case 'PageUp':
                focusArticle(pageTarget(el, e.key === 'PageDown'));
                break;
            case 'Home':
                focusArticle(firstArticle());
                break;
            case 'End':
                (hasNewerGap ? jumpToLiveTail() : Promise.resolve()).then(function () {
                    focusArticle(lastArticle());
                });
                break;
            default:
                return;
        }
        e.preventDefault();
    }

    function describeIncoming(msg) {
        var content = msg.content || {};
        var atts = content.attachments || [];
        var text = content.text
            ? u.truncate(u.markdownToPlainText(content.text), 200)
            : atts.length > 0
              ? u.attachmentEmoji(atts[0].type)
              : '';
        return Promise.resolve(deps.getUser(msg.senderId))
            .catch(function () {
                return null;
            })
            .then(function (user) {
                return BF.i18n.t('a11y.newMessage', {
                    name: senderName(user) || BF.i18n.t('common.user'),
                    text: text
                });
            });
    }

    // Новые входящие сообщения открытого чата — в скрытый live-регион. История, resync и пагинация
    // сюда не попадают; пачка за секунду объявляется одной фразой.
    function announceIncoming(msg) {
        if (!liveRegion || !msg) return;
        announceQueue.push(msg);
        if (announceTimer) return;
        announceTimer = setTimeout(function () {
            announceTimer = null;
            var batch = announceQueue.splice(0);
            var textPromise =
                batch.length === 1
                    ? describeIncoming(batch[0])
                    : Promise.resolve(BF.i18n.tp('a11y.newMessages', batch.length));
            textPromise.then(function (text) {
                // Очистка и повторная запись — иначе одинаковый текст подряд не озвучивается.
                liveRegion.textContent = '';
                setTimeout(function () {
                    liveRegion.textContent = text;
                }, 50);
            });
        }, 1000);
    }

    // ========== PAGING ==========

    // Страница ленты: обычный чат или приватный (там батч приходит зашифрованным).
    function loadMessagesPage(chatId, fromMessageId, offsetBefore, offsetAfter) {
        if (deps.getCurrentChatType() !== 1)
            return trackBusy(BF.api.listMessages(chatId, fromMessageId, offsetBefore, offsetAfter));
        return trackBusy(
            BF.api.listPrivateMessages(chatId, fromMessageId, offsetBefore, offsetAfter).then(function (d) {
                return deps.decryptPrivateBatch(chatId, d && d.messages).then(function (mapped) {
                    mapped.sort(function (a, b) {
                        return a.id - b.id;
                    });
                    return { messages: mapped };
                });
            })
        );
    }

    // Подгрузка новых сообщений, когда скользящее окно обрезало хвост ленты.
    function loadNewerMessages() {
        var pagedChatId = deps.getCurrentChatId();
        var messages = deps.getMessages();
        if (
            !hasNewerGap ||
            isLoadingNewer ||
            isJumpingToTail ||
            isJumpingToMessage ||
            !pagedChatId ||
            messages.length === 0
        )
            return Promise.resolve();
        isLoadingNewer = true;
        var newestId = messages[messages.length - 1].id || 0;

        return loadMessagesPage(pagedChatId, newestId, 0, 30)
            .then(function (data) {
                if (pagedChatId !== deps.getCurrentChatId()) return;
                var fetched = (data && data.messages) || [];
                var current = deps.getMessages();
                var fresh = fetched.filter(function (m) {
                    return !current.some(function (em) {
                        return em.id === m.id;
                    });
                });
                // `api.js` подменяет offsetBefore=0 на 30, поэтому в ответе всегда есть уже
                // загруженные сообщения: конец чата определяем по числу действительно новых.
                if (fresh.length < 30) hasNewerGap = false;
                if (fresh.length === 0) return;

                var chain = Promise.resolve();
                fresh.forEach(function (msg) {
                    chain = chain.then(function () {
                        deps.getMessages().push(msg);
                        // resync-пропуск мог прийти именно этой страницей (окно было в
                        // середине истории) — перед якорным сообщением ставим разделитель.
                        var sepKey =
                            resyncSeparatorId && Number(msg.id) === Number(resyncSeparatorId)
                                ? 'chat.newMessages'
                                : null;
                        return appendMessageElement(msg, sepKey);
                    });
                });
                return chain.then(function () {
                    trimMessages('head');
                });
            })
            .finally(function () {
                isLoadingNewer = false;
            });
    }

    // Подгрузка старых сообщений (скролл к верху ленты).
    function loadOlderMessages() {
        var pagedChatId = deps.getCurrentChatId();
        var messages = deps.getMessages();
        if (isLoadingOlder || isJumpingToMessage || noMoreOlder || !pagedChatId || messages.length === 0)
            return Promise.resolve();
        isLoadingOlder = true;
        loadingMessages.classList.add('visible');
        var oldestId = messages[0].id || 0;
        var prevHeight = messagesArea.scrollHeight;

        return loadMessagesPage(pagedChatId, oldestId, 30, 0)
            .then(function (data) {
                if (pagedChatId !== deps.getCurrentChatId()) return;
                if (data && data.messages && data.messages.length > 0) {
                    var current = deps.getMessages();
                    var newMsgs = data.messages.filter(function (m) {
                        return !current.some(function (em) {
                            return em.id === m.id;
                        });
                    });
                    if (newMsgs.length === 0) {
                        noMoreOlder = true;
                    } else {
                        deps.setMessages(newMsgs.concat(current));
                        return prependMessages(newMsgs).then(function () {
                            messagesArea.scrollTop = messagesArea.scrollHeight - prevHeight;
                            trimMessages('tail');
                        });
                    }
                } else {
                    noMoreOlder = true;
                }
            })
            .finally(function () {
                loadingMessages.classList.remove('visible');
                isLoadingOlder = false;
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
        var hadFocus = !!focusedArticle();
        if (el) {
            el.scrollIntoView({ block: 'center', behavior: 'smooth' });
            el.classList.add('highlight');
            setTimeout(function () {
                el.classList.remove('highlight');
            }, 1500);
            if (hadFocus) focusArticle(el.closest('.msg-group'), false);
            return;
        }
        var chatId = deps.getCurrentChatId();
        if (!chatId || isJumpingToMessage || isJumpingToTail || isLoadingOlder || isLoadingNewer) return;
        isJumpingToMessage = true;

        loadMessagesPage(chatId, id, 30, 30)
            .then(function (data) {
                if (chatId !== deps.getCurrentChatId()) return;
                var fetched = (data && data.messages) || [];
                var target = fetched.find(function (m) {
                    return Number(m.id) === Number(id);
                });
                if (!target) {
                    deps.showToast(BF.i18n.t('chat.messageNotFound'), true);
                    return;
                }

                deps.setMessages(fetched);
                deps.mergePendingUploads(chatId);
                resyncSeparatorId = null; // окно перенесено к цели прыжка — прежняя граница «нового» неактуальна

                // Края чата определяем по числу сообщений старее/новее цели: api.js
                // подменяет offsetBefore=0 на 30, поэтому размер ответа не показатель.
                // При ровно 30 оставляем «зазор» — следующая догрузка его закроет.
                var targetId = Number(id);
                var olderCount = fetched.filter(function (m) {
                    return Number(m.id) < targetId;
                }).length;
                var newerCount = fetched.filter(function (m) {
                    return Number(m.id) > targetId;
                }).length;
                noMoreOlder = olderCount < 30;
                hasNewerGap = newerCount >= 30; // хвост за окном: живые сообщения не рисуются (guard appendMessageToView)

                return render().then(function () {
                    settleHighlight(id);
                    if (hadFocus) focusArticle(findMessageGroup(id), false);
                });
            })
            .finally(function () {
                isJumpingToMessage = false;
            });
    }

    function onScroll() {
        if (messagesArea.scrollTop < 100) loadOlderMessages();

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
    }

    function init(options) {
        deps = options;
        u = BF.utils;
        messagesArea = $('#messagesArea');
        messagesInner = $('#messagesInner');
        loadingMessages = $('#loadingMessages');
        scrollToBottomBtn = $('#scrollToBottomBtn');
        scrollBadge = scrollToBottomBtn ? scrollToBottomBtn.querySelector('.scroll-badge') : null;
        liveRegion = $('#feedLiveRegion');

        messagesArea.addEventListener('scroll', onScroll);
        messagesInner.addEventListener('keydown', onFeedKeydown);

        if (scrollToBottomBtn) {
            scrollToBottomBtn.addEventListener('click', function () {
                scrollToBottom();
                scrollToBottomBtn.classList.remove('visible');
                newMessagesBelowCount = 0;
                updateScrollBadge();
            });
        }
    }

    window.BF.feed = {
        init: init,
        reset: reset,
        render: render,
        append: appendMessageToView,
        scrollToBottom: scrollToBottom,
        settleScroll: settleScroll,
        scrollToMessage: scrollToMessage,
        findGroup: findMessageGroup,
        buildElement: buildMessageViewElement,
        replaceElement: replaceElement,
        removeElement: removeElement,
        announceIncoming: announceIncoming,
        clearNewerGap: function (resetLoading) {
            hasNewerGap = false;
            if (resetLoading) isLoadingNewer = false;
        },
        setNoMoreOlder: function (value) {
            noMoreOlder = value;
        },
        isLoadingOlder: function () {
            return isLoadingOlder;
        },
        getResyncSeparatorId: function () {
            return resyncSeparatorId;
        },
        setResyncSeparatorId: function (id) {
            resyncSeparatorId = id;
        },
        incrementNewBelow: incrementNewBelow
    };
})();
