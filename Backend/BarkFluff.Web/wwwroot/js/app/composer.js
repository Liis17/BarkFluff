/**
 * Message composer: the input field, typing status, reply/edit state, drafts, paste and drag-and-drop.
 * The send pipeline itself (pending sends and uploads) stays in main.js and is reached through deps.
 * Requires: BF.api, BF.attach, BF.drafts, BF.i18n, BF.utils
 * Exposes: BF.composer
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var messageInput;
    var replyPreviewBar;
    var rpbAuthor;
    var rpbText;
    var editPreviewBar;
    var epbText;

    var pendingReply = null;
    var pendingEdit = null; // { messageId, originalText }
    var typingSendActive = false;
    var typingLastInputAt = 0;
    var typingSendTimer = null;

    function $(selector) {
        return document.querySelector(selector);
    }

    function autosize() {
        messageInput.style.height = 'auto';
        messageInput.style.height = Math.min(messageInput.scrollHeight, 120) + 'px';
    }

    function getText() {
        return messageInput.value;
    }

    function getReplyToId() {
        return pendingReply ? pendingReply.messageId : 0;
    }

    function getEdit() {
        return pendingEdit;
    }

    // ========== TYPING (отправка своего статуса) ==========

    function stopTyping(sendCancel) {
        if (typingSendTimer) {
            clearInterval(typingSendTimer);
            typingSendTimer = null;
        }
        if (sendCancel && typingSendActive) {
            BF.api.setTypingStatus(deps.getCurrentChatId(), false).catch(function () {});
        }
        typingSendActive = false;
    }

    function onInput() {
        autosize();
        saveCurrentDraft();

        var currentChatId = deps.getCurrentChatId();
        if (!currentChatId || deps.getCurrentChatType() !== 0) return;
        var value = messageInput.value;
        if (value.trim() === '') {
            stopTyping(true);
            return;
        }
        typingLastInputAt = Date.now();
        if (!typingSendActive) {
            typingSendActive = true;
            BF.api.setTypingStatus(currentChatId, true).catch(function () {});
            typingSendTimer = setInterval(function () {
                if (Date.now() - typingLastInputAt >= 5000) {
                    stopTyping(false);
                } else {
                    BF.api.setTypingStatus(deps.getCurrentChatId(), true).catch(function () {});
                }
            }, 4000);
        }
    }

    // ========== DRAFTS ==========

    function restoreDraft(chatId) {
        if (!BF.drafts || chatId !== deps.getCurrentChatId()) return;
        BF.drafts.load(chatId).then(function (draft) {
            if (!draft || chatId !== deps.getCurrentChatId()) return;
            messageInput.value = draft.text || '';
            autosize();
            if (draft.replyToMessageId) {
                var reply = deps.getMessages().find(function (m) {
                    return Number(m.id) === Number(draft.replyToMessageId);
                });
                if (reply) {
                    setReply(reply, false);
                } else {
                    BF.api
                        .listMessages(chatId, draft.replyToMessageId, 1, 1)
                        .then(function (data) {
                            if (chatId !== deps.getCurrentChatId() || !data || !data.messages) return;
                            var loadedReply = data.messages.find(function (m) {
                                return Number(m.id) === Number(draft.replyToMessageId);
                            });
                            if (loadedReply) setReply(loadedReply, false);
                            else BF.drafts.set(chatId, draft.text || '', 0);
                        })
                        .catch(function () {});
                }
            }
            deps.renderChatList();
        });
    }

    function saveCurrentDraft() {
        var currentChatId = deps.getCurrentChatId();
        if (!currentChatId || deps.getCurrentChatType() !== 0 || pendingEdit || !BF.drafts) return;
        BF.drafts.set(currentChatId, messageInput.value, pendingReply ? pendingReply.messageId : 0);
        deps.renderChatList();
    }

    // Поле очищается сразу при постановке сообщения в очередь отправки.
    function clearForSend() {
        messageInput.value = '';
        messageInput.style.height = 'auto';
        clearReply();
    }

    // Отменённая отправка возвращает текст/подпись и ответ обратно в композер.
    function restoreFromPending(entry) {
        if (String(entry.chatId) !== String(deps.getCurrentChatId())) return;
        messageInput.value = entry.caption || entry.text || '';
        autosize();
        if (entry.replyToMessageId) {
            var reply = deps.getMessages().find(function (message) {
                return Number(message.id) === Number(entry.replyToMessageId);
            });
            if (reply) {
                setReply(reply, false);
            } else {
                pendingReply = {
                    messageId: entry.replyToMessageId,
                    authorName: '',
                    previewText: ''
                };
                renderReplyPreview();
                BF.api
                    .listMessages(entry.chatId, entry.replyToMessageId, 1, 1)
                    .then(function (data) {
                        if (!pendingReply || Number(pendingReply.messageId) !== Number(entry.replyToMessageId)) return;
                        var loaded =
                            data &&
                            data.messages &&
                            data.messages.find(function (message) {
                                return Number(message.id) === Number(entry.replyToMessageId);
                            });
                        if (loaded) setReply(loaded, false);
                    })
                    .catch(function () {});
            }
        }
        saveCurrentDraft();
        messageInput.focus();
    }

    // ========== REPLY / EDIT ==========

    function buildReplyPreviewText(msg) {
        if (msg.content && msg.content.text) return msg.content.text;
        var atts = (msg.content && msg.content.attachments) || [];
        for (var i = 0; i < atts.length; i++) {
            var t = atts[i].type;
            if (t === 8 || t === '8' || t === 'FORWARDED_MESSAGE') continue;
            return BF.utils.attachmentEmoji(t === 7 || t === '7' ? 'STICKER' : t);
        }
        return '';
    }

    function setReply(msg, persist) {
        if (!msg) return;
        pendingReply = {
            messageId: msg.id,
            authorName: '',
            previewText: buildReplyPreviewText(msg)
        };
        if (msg.senderId === deps.getMyUserId()) {
            pendingReply.authorName = BF.i18n.t('call.you');
            renderReplyPreview();
        } else {
            deps.getUser(msg.senderId).then(function (sender) {
                if (!pendingReply || pendingReply.messageId !== msg.id) return;
                if (sender) {
                    pendingReply.authorName =
                        ((sender.firstName || '') + ' ' + (sender.lastName || '')).trim() || sender.username || '';
                }
                renderReplyPreview();
            });
            renderReplyPreview();
        }
        if (messageInput) {
            try {
                messageInput.focus();
            } catch (e) {}
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

    function clearReply(persist) {
        pendingReply = null;
        if (replyPreviewBar) replyPreviewBar.classList.remove('visible');
        if (persist !== false) saveCurrentDraft();
    }

    function setEdit(msg) {
        if (!msg) return;
        clearReply();
        var origText = (msg.content && msg.content.text) || '';
        pendingEdit = { messageId: msg.id, originalText: origText };
        messageInput.value = origText;
        autosize();
        if (epbText) epbText.textContent = origText || BF.i18n.t('composer.edit.attachmentsOnly');
        if (editPreviewBar) editPreviewBar.classList.add('visible');
        try {
            messageInput.focus();
        } catch (e) {}
    }

    function clearEdit() {
        pendingEdit = null;
        if (editPreviewBar) editPreviewBar.classList.remove('visible');
        messageInput.value = '';
        messageInput.style.height = 'auto';
    }

    // Сообщение удалено (нами или собеседником): редактировать его или отвечать на него уже нельзя.
    // Редактирование завершается, набранный текст остаётся в поле как обычный ввод; ответ снимается.
    function onMessageDeleted(messageId) {
        var id = Number(messageId);
        if (pendingEdit && Number(pendingEdit.messageId) === id) {
            pendingEdit = null;
            if (editPreviewBar) editPreviewBar.classList.remove('visible');
            saveCurrentDraft();
        }
        if (pendingReply && Number(pendingReply.messageId) === id) clearReply();
    }

    // ========== ATTACHMENTS (paste, drag-and-drop) ==========

    function openAttach(files) {
        if (!deps.getCurrentChatId() || deps.getCurrentChatType() === 1) return; // в приватных чатах вложения не поддерживаются
        var prefill = messageInput.value;
        BF.attach.open(
            files,
            function (outFiles, asDocuments, caption) {
                // Если пользователь ввёл подпись в модалке — забираем её из неё, а исходный
                // ввод в чате очищаем, чтобы текст не отправился ещё раз отдельным сообщением.
                deps.onSendFiles(outFiles, asDocuments, caption);
            },
            prefill
        );
    }

    function initDragAndDrop() {
        // Глобально блокируем дефолтное открытие файла в браузере при промахе мимо chat-area
        ['dragover', 'drop'].forEach(function (ev) {
            window.addEventListener(ev, function (e) {
                e.preventDefault();
            });
        });

        var chatArea = document.querySelector('.chat-area');
        if (!chatArea) return;
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
            if (!deps.getCurrentChatId() || deps.getCurrentChatType() === 1 || !isFileDrag(e)) return;
            dragCounter++;
            chatArea.classList.add('drag-over');
        });
        chatArea.addEventListener('dragover', function (e) {
            if (!deps.getCurrentChatId() || !isFileDrag(e)) return;
            e.dataTransfer.dropEffect = 'copy';
        });
        chatArea.addEventListener('dragleave', function () {
            dragCounter--;
            if (dragCounter <= 0) {
                dragCounter = 0;
                chatArea.classList.remove('drag-over');
            }
        });
        chatArea.addEventListener('drop', function (e) {
            if (!deps.getCurrentChatId()) return;
            dragCounter = 0;
            chatArea.classList.remove('drag-over');
            var files = Array.from(e.dataTransfer.files || []);
            if (files.length > 0) openAttach(files);
        });
    }

    function init(options) {
        deps = options;
        messageInput = $('#messageInput');
        replyPreviewBar = $('#replyPreviewBar');
        rpbAuthor = $('#rpbAuthor');
        rpbText = $('#rpbText');
        editPreviewBar = $('#editPreviewBar');
        epbText = $('#epbText');
        var rpbCloseBtn = $('#rpbClose');
        var epbCloseBtn = $('#epbClose');

        messageInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                deps.onSubmit();
            }
        });
        messageInput.addEventListener('input', onInput);
        messageInput.addEventListener('paste', function (e) {
            if (!deps.getCurrentChatId()) return;
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
            openAttach(files);
        });
        initDragAndDrop();

        // Кнопка закрытия передаёт Event как persist — черновик сохраняется (как раньше).
        if (rpbCloseBtn) rpbCloseBtn.addEventListener('click', clearReply);
        if (epbCloseBtn) epbCloseBtn.addEventListener('click', clearEdit);
    }

    window.BF.composer = {
        init: init,
        getText: getText,
        getReplyToId: getReplyToId,
        getEdit: getEdit,
        setReply: setReply,
        clearReply: clearReply,
        setEdit: setEdit,
        clearEdit: clearEdit,
        stopTyping: stopTyping,
        restoreDraft: restoreDraft,
        restoreFromPending: restoreFromPending,
        clearForSend: clearForSend,
        onMessageDeleted: onMessageDeleted,
        openAttach: openAttach
    };
})();
