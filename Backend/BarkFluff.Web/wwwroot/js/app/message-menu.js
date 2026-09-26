/**
 * Context menu of a message in the feed (right click, long press, Shift+F10 / ContextMenu key on a focused
 * message) and the swipe-left-to-reply gesture. The menu is a non-modal role="menu" with arrow-key navigation.
 * Reply/edit/forward/delete are performed by main.js and the composer through deps.
 * Requires: BF.i18n, BF.pinned
 * Exposes: BF.messageMenu
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var msgContextMenu;
    var messagesArea;
    var messagesInner;
    var contextMenuTarget = null;
    var cmenuShownAt = 0;
    var mqlMobile;
    var returnFocusEl = null; // куда вернуть фокус, если меню закрывается с фокусом внутри
    var suppressContextMenuUntil = 0; // браузер шлёт contextmenu вслед за клавишей ContextMenu/Shift+F10

    function $(selector) {
        return document.querySelector(selector);
    }

    function findMessage(msgId) {
        return deps.getMessages().find(function (m) {
            return Number(m.id) === Number(msgId);
        });
    }

    // options.keyboard — открыто с клавиатуры: фокус сразу на первый пункт.
    function open(x, y, msgEl, options) {
        if (!msgContextMenu || !msgEl) return;
        if (deps.getCurrentChatType() === 1) return; // edit/delete/reply/pin для приватных сообщений не поддерживаются

        if (msgEl.classList.contains('msg-system')) return;
        var msgId = Number(msgEl.dataset.msgId);
        if (!msgId) return;
        var isOutgoing = msgEl.classList.contains('outgoing');
        contextMenuTarget = { messageId: msgId, isOutgoing: isOutgoing };

        var msgObj = findMessage(msgId);
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
        if (copyTextBtn) copyTextBtn.style.display = msgText && !isSystem ? '' : 'none';

        // Копировать изображение — только если ровно одно изображение и оно единственное медиа.
        var copyImageBtn = msgContextMenu.querySelector('button[data-act="copy-image"]');
        var singleImageFileId = null;
        if (msgObj && msgObj.content && msgObj.content.attachments && !isSystem) {
            var imgAtts = msgObj.content.attachments.filter(function (a) {
                return a.type !== 'FORWARDED_MESSAGE';
            });
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
        var w = rect.width,
            h = rect.height;
        var left = Math.max(8, Math.min(x, vw - w - 8));
        var top = y + h > vh ? Math.max(8, y - h) : y;
        msgContextMenu.style.left = left + 'px';
        msgContextMenu.style.top = top + 'px';
        cmenuShownAt = Date.now();

        var keyboard = !!(options && options.keyboard);
        returnFocusEl = keyboard ? msgEl : document.activeElement;
        if (keyboard) BF.utils.focusMenuItem(msgContextMenu, 0);
    }

    function close() {
        if (!msgContextMenu) return;
        // Проверяем до скрытия: после display:none фокус уже на body.
        var hadFocus = msgContextMenu.contains(document.activeElement);
        msgContextMenu.classList.remove('visible');
        contextMenuTarget = null;
        var target = returnFocusEl;
        returnFocusEl = null;
        if (hadFocus && target && document.contains(target) && typeof target.focus === 'function') {
            target.focus({ preventScroll: true });
        }
    }

    function isContextMenuKey(e) {
        return (e.shiftKey && e.key === 'F10') || e.key === 'ContextMenu';
    }

    // Открытие с клавиатуры у сфокусированного сообщения (виден хотя бы его верх).
    function openFromKeyboard(msgEl) {
        var rect = msgEl.getBoundingClientRect();
        var y = Math.min(Math.max(rect.top + 16, 8), window.innerHeight - 8);
        suppressContextMenuUntil = Date.now() + 500;
        open(rect.left + 16, y, msgEl, { keyboard: true });
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
        var previewPng =
            image.complete && image.naturalWidth && image.naturalHeight
                ? canvasToPngBlob(image, image.naturalWidth, image.naturalHeight)
                : Promise.reject(new Error('preview_not_loaded'));

        previewPng
            .catch(function () {
                if (!imageUrl) throw new Error('no_preview_url');
                return fetch(imageUrl)
                    .then(function (response) {
                        if (!response.ok) throw new Error('preview_unavailable');
                        return response.blob();
                    })
                    .then(function (blob) {
                        return createImageBitmap(blob).then(function (bitmap) {
                            return canvasToPngBlob(bitmap, bitmap.width, bitmap.height).then(function (png) {
                                bitmap.close();
                                return png;
                            });
                        });
                    });
            })
            .then(function (png) {
                return navigator.clipboard.write([new ClipboardItem({ 'image/png': png })]);
            })
            .catch(function () {
                deps.showToast(BF.i18n.t('error.copy'), true);
            });
    }

    function onMenuClick(e) {
        var btn = e.target.closest('button[data-act]');
        if (!btn || !contextMenuTarget) return;
        var act = btn.dataset.act;
        var msgId = contextMenuTarget.messageId;
        var isOutgoing = contextMenuTarget.isOutgoing;
        var image = contextMenuTarget.image;
        var msg = findMessage(msgId);
        close();
        if (act === 'reply') {
            if (msg) deps.setReply(msg);
        } else if (act === 'forward') {
            deps.forward(msg, msgId);
        } else if (act === 'copy-text') {
            var t = msg && msg.content && msg.content.text;
            if (t)
                navigator.clipboard.writeText(t).catch(function () {
                    deps.showToast(BF.i18n.t('error.copy'), true);
                });
        } else if (act === 'copy-image') {
            copyImageToClipboard(image);
        } else if (act === 'edit') {
            if (msg && isOutgoing && msg.type !== 2 && msg.type !== 'SYSTEM') {
                deps.setEdit(msg);
            }
        } else if (act === 'delete') {
            if (msg && isOutgoing && msg.type !== 2 && msg.type !== 'SYSTEM') {
                deps.requestDelete(msg.id);
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
            deps.showToast(BF.i18n.t('common.comingSoon'), false);
        }
    }

    // --- Touch handlers: long-press + swipe-left to reply ---
    function initTouch() {
        if (!messagesInner) return;

        var pressTimer = null;
        var startX = 0,
            startY = 0;
        var lastX = 0,
            lastY = 0;
        var pressTarget = null;
        var swiping = false;
        var swipeLockedForReply = false;
        var axisLocked = false;
        var INTERACTIVE_SEL = 'img, video, a, button, .audio-play-btn, .attach-doc';

        function cancelPressTimer() {
            if (pressTimer) {
                clearTimeout(pressTimer);
                pressTimer = null;
            }
        }

        function resetSwipe(grp) {
            if (!grp) return;
            grp.classList.remove('swiping');
            grp.style.transform = '';
        }

        messagesInner.addEventListener(
            'touchstart',
            function (e) {
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
                    try {
                        if (navigator.vibrate) navigator.vibrate(20);
                    } catch (e2) {}
                    var rect = pressTarget.getBoundingClientRect();
                    var cx = Math.min(Math.max(startX, rect.left), rect.right);
                    var cy = Math.min(Math.max(startY, rect.top), rect.bottom);
                    open(cx, cy, pressTarget);
                }, 500);

                grp._skipSwipe = skipSwipe;
            },
            { passive: true }
        );

        messagesInner.addEventListener(
            'touchmove',
            function (e) {
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
                if (dx <= -60) swipeLockedForReply = true;
                else swipeLockedForReply = false;
            },
            { passive: false }
        );

        messagesInner.addEventListener('touchend', function () {
            cancelPressTimer();
            var t = pressTarget;
            pressTarget = null;
            if (!t) return;
            if (swiping) {
                if (swipeLockedForReply) {
                    var msgId = Number(t.dataset.msgId);
                    var msg = deps.getMessages().find(function (m) {
                        return m.id === msgId;
                    });
                    if (msg) deps.setReply(msg);
                }
                resetSwipe(t);
            }
        });

        messagesInner.addEventListener('touchcancel', function () {
            cancelPressTimer();
            if (pressTarget) resetSwipe(pressTarget);
            pressTarget = null;
        });
    }

    function init(options) {
        deps = options;
        msgContextMenu = $('#msgContextMenu');
        messagesArea = $('#messagesArea');
        messagesInner = $('#messagesInner');
        mqlMobile = window.matchMedia('(max-width: 768px), (pointer: coarse)');

        if (msgContextMenu) {
            msgContextMenu.addEventListener('click', onMenuClick);
            msgContextMenu.addEventListener('keydown', function (e) {
                BF.utils.handleMenuKeydown(e, msgContextMenu, close);
            });
            msgContextMenu.addEventListener('contextmenu', function (e) {
                e.preventDefault();
            });
        }

        // --- Global close handlers for context menu ---
        document.addEventListener(
            'click',
            function (e) {
                if (!msgContextMenu || !msgContextMenu.classList.contains('visible')) return;
                if (msgContextMenu.contains(e.target)) return;
                if (Date.now() - cmenuShownAt < 300) return;
                close();
            },
            true
        );
        document.addEventListener('keydown', function (e) {
            if (!msgContextMenu || !msgContextMenu.classList.contains('visible')) return;
            if (e.key === 'Escape') close();
            // Меню открыто мышью — стрелки переводят фокус в него.
            else if (
                (e.key === 'ArrowDown' || e.key === 'ArrowUp') &&
                !msgContextMenu.contains(document.activeElement)
            ) {
                e.preventDefault();
                BF.utils.focusMenuItem(msgContextMenu, e.key === 'ArrowDown' ? 0 : -1);
            }
        });
        window.addEventListener('resize', close);
        if (messagesArea) messagesArea.addEventListener('scroll', close);

        // --- contextmenu (desktop right-click + system long-press) ---
        if (messagesInner) {
            messagesInner.addEventListener('contextmenu', function (e) {
                var grp = e.target.closest('.msg-group');
                if (!grp || !grp.dataset.msgId) return;
                e.preventDefault();
                if (Date.now() < suppressContextMenuUntil) return;
                open(e.clientX, e.clientY, grp);
            });
            messagesInner.addEventListener('keydown', function (e) {
                var grp = e.target;
                if (!grp.classList || !grp.classList.contains('msg-group') || !grp.dataset.msgId) return;
                // Enter тоже открывает меню: на macOS нет клавиши ContextMenu.
                if (!isContextMenuKey(e) && e.key !== 'Enter') return;
                e.preventDefault();
                openFromKeyboard(grp);
            });
        }

        initTouch();
    }

    window.BF.messageMenu = { init: init, close: close };
})();
