/**
 * Send queue: optimistic pending messages (text and file uploads), their durable metadata restore on
 * reload, reconciliation with the server echo, retry/cancel, and the attach button / file input wiring.
 * Requires: BF.api, BF.composer, BF.drafts, BF.feed, BF.files, BF.i18n, BF.messageEvents, BF.messages,
 *           BF.pendingSends, BF.privateChatUI, BF.sound, BF.utils
 * Exposes: BF.sendQueue
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var messagesArea;
    var sendBtn;
    var attachBtn;
    var fileInput;

    var pendingUploads = new Map(); // local message id -> optimistic upload state
    var pendingFileSelectionEntry = null;
    var GENERIC_MESSAGE_TYPE = 1;
    var IMAGE_UPLOAD_TYPE = 2;
    var GIF_UPLOAD_TYPE = 4;

    function newOperationId() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') return window.crypto.randomUUID();
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = (Math.random() * 16) | 0;
            return (c === 'x' ? r : (r & 3) | 8).toString(16);
        });
    }

    function releasePendingPreviews(entry) {
        if (!entry || !entry.previewUrls) return;
        entry.previewUrls.forEach(function (url) {
            URL.revokeObjectURL(url);
        });
        entry.previewUrls = [];
    }

    function removePendingUpload(entry) {
        if (!entry || entry.settled) return;
        entry.settled = true;
        pendingUploads.delete(entry.localId);
        if (BF.pendingSends && entry.operationId) BF.pendingSends.remove(entry.operationId);
        releasePendingPreviews(entry);

        var messages = deps.getMessages();
        var idx = messages.findIndex(function (m) {
            return String(m.id) === String(entry.localId);
        });
        if (idx >= 0) messages.splice(idx, 1);

        var el = BF.feed.findGroup(entry.localId);
        if (el) BF.feed.removeElement(el);
    }

    function messageFileIds(msg) {
        return ((msg && msg.content && msg.content.attachments) || [])
            .map(function (a) {
                return a.fileId;
            })
            .filter(Boolean)
            .map(function (id) {
                return String(id).toLowerCase();
            })
            .sort();
    }

    function pendingUploadMatches(entry, msg) {
        if (
            entry.operationId &&
            msg &&
            msg.clientOperationId &&
            String(entry.operationId).toLowerCase() === String(msg.clientOperationId).toLowerCase()
        ) {
            return true;
        }
        var serverIds = messageFileIds(msg);
        var pendingIds = entry.fileIds
            .map(function (id) {
                return String(id).toLowerCase();
            })
            .sort();
        return (
            pendingIds.length > 0 &&
            pendingIds.length === serverIds.length &&
            pendingIds.every(function (id, index) {
                return id === serverIds[index];
            })
        );
    }

    function mergePendingUploadsIntoMessages(chatId) {
        var messages = deps.getMessages();
        pendingUploads.forEach(function (entry) {
            if (entry.settled || String(entry.chatId) !== String(chatId)) return;
            var serverMessage = messages.find(function (msg) {
                return pendingUploadMatches(entry, msg);
            });
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
        if (!msg || msg.senderId !== deps.getMyUserId()) return null;

        var found = null;
        pendingUploads.forEach(function (entry) {
            if (found || entry.settled || String(entry.chatId) !== String(chatId)) return;
            if (pendingUploadMatches(entry, msg)) found = entry;
        });
        return found;
    }

    function replacePendingElement(entry, msg) {
        var oldEl = BF.feed.findGroup(entry.localId);
        if (!oldEl) {
            releasePendingPreviews(entry);
            return;
        }

        BF.feed
            .buildElement(msg)
            .then(function (newEl) {
                newEl.dataset.date = BF.utils.formatDate(msg.sentAt);
                if (oldEl.isConnected) BF.feed.replaceElement(oldEl, newEl);
                releasePendingPreviews(entry);
            })
            .catch(function () {
                BF.feed.render().then(function () {
                    releasePendingPreviews(entry);
                });
            });
    }

    function reconcilePendingUpload(chatId, msg, hintedEntry) {
        var entry = hintedEntry && !hintedEntry.settled ? hintedEntry : findPendingUpload(chatId, msg);
        if (!entry) return false;

        entry.settled = true;
        pendingUploads.delete(entry.localId);
        if (BF.pendingSends && entry.operationId) BF.pendingSends.remove(entry.operationId);
        if (BF.drafts && entry.draftSnapshot) BF.drafts.clearSent(entry.chatId, entry.draftSnapshot);

        if (String(chatId) !== String(deps.getCurrentChatId())) {
            releasePendingPreviews(entry);
            return true;
        }

        var messages = deps.getMessages();
        var localIdx = messages.findIndex(function (m) {
            return String(m.id) === String(entry.localId);
        });
        var serverIdx = messages.findIndex(function (m) {
            return m.id === msg.id;
        });
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
            var oldEl = BF.feed.findGroup(entry.localId);
            if (oldEl) BF.feed.removeElement(oldEl);
            releasePendingPreviews(entry);
            if (serverIdx < 0)
                BF.feed.append(msg).then(function () {
                    if (wasAtBottom) BF.feed.scrollToBottom();
                });
        }
        if (wasAtBottom && localIdx >= 0 && serverIdx < 0) setTimeout(BF.feed.scrollToBottom, 0);
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
        if (String(entry.chatId) !== String(deps.getCurrentChatId())) return;
        var oldEl = BF.feed.findGroup(entry.localId);
        if (!oldEl) return;
        BF.feed.buildElement(entry.localMessage).then(function (newEl) {
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
        var myUserId = deps.getMyUserId();
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
                pendingState: uploads.some(function (upload) {
                    return upload.state !== 'completed';
                })
                    ? 'waiting-file'
                    : stored.state === 'failed' || stored.state === 'unknown'
                      ? stored.state
                      : 'unknown',
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
            var isImage =
                !asDocuments && (upload.uploadType === IMAGE_UPLOAD_TYPE || upload.uploadType === GIF_UPLOAD_TYPE);
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
        var currentChatId = deps.getCurrentChatId();
        var draftSnapshot = BF.drafts ? BF.drafts.snapshot(currentChatId) : null;
        var localMessage = {
            id: localId,
            senderId: deps.getMyUserId(),
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
        if (String(entry.chatId) === String(deps.getCurrentChatId())) {
            deps.getMessages().push(entry.localMessage);
            BF.feed.append(entry.localMessage).then(BF.feed.scrollToBottom);
        }
    }

    function dispatchPendingSend(entry) {
        if (entry.settled) return Promise.resolve();
        setPendingState(entry, 'sending');
        return BF.api
            .sendMessage({
                chatId: entry.chatId,
                text: entry.caption || entry.text || null,
                fileIds: entry.fileIds.length ? entry.fileIds : null,
                replyToMessageId: entry.replyToMessageId,
                clientOperationId: entry.operationId
            })
            .then(function (resp) {
                if (!resp || !resp.message) throw new Error('send_invalid_response');
                var msg = resp.message;
                reconcilePendingUpload(entry.chatId, msg, entry);
                var chats = deps.getChats();
                var chatIdx = chats.findIndex(function (chat) {
                    return chat.id === entry.chatId;
                });
                if (chatIdx >= 0) {
                    var chat = chats[chatIdx];
                    chat.lastMessage = msg;
                    chats.splice(chatIdx, 1);
                    chats.unshift(chat);
                    deps.renderChatList();
                }
                BF.sound.play('tick');
            })
            .catch(function (error) {
                if (!entry.settled) setPendingState(entry, error && error.outcomeUnknown ? 'unknown' : 'failed');
            })
            .finally(function () {
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

        return chain
            .then(function () {
                entry.abortController = null;
                return dispatchPendingSend(entry);
            })
            .catch(function (error) {
                entry.abortController = null;
                if (entry.settled) return;
                if (error && error.kind === 'file-missing') setPendingState(entry, 'waiting-file');
                else if (error && (error.state === 'processing' || error.message === 'upload_processing'))
                    setPendingState(entry, 'processing');
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
        BF.composer.restoreFromPending(entry);
        removePendingUpload(entry);
        sendBtn.disabled = false;
    }

    function retryPendingSend(localId) {
        var entry = pendingUploads.get(localId);
        if (!entry || entry.settled || entry.retrying) return;
        var hasIncompleteUpload = entry.uploads.some(function (upload) {
            return upload.state !== 'completed';
        });
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
            checks
                .then(function () {
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
                })
                .catch(function (error) {
                    if (!entry.settled) setPendingState(entry, error && error.outcomeUnknown ? 'unknown' : 'failed');
                })
                .finally(function () {
                    entry.retrying = false;
                });
            return;
        }
        entry.retrying = true;
        runPendingSend(entry, true).finally(function () {
            entry.retrying = false;
        });
    }

    function sendMessage() {
        var text = BF.composer.getText().trim();
        var currentChatId = deps.getCurrentChatId();
        if (!currentChatId) return;
        BF.composer.stopTyping(true);

        if (deps.getCurrentChatType() === 1) {
            if (text) BF.privateChatUI.send(text);
            return;
        }

        var edit = BF.composer.getEdit();
        if (edit) {
            var editId = edit.messageId;
            var origMsg = deps.getMessages().find(function (m) {
                return m.id === editId;
            });
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
            BF.api
                .editMessage(editId, text, keepFileIds)
                .then(function (resp) {
                    sendBtn.disabled = false;
                    if (resp && resp.message) BF.messageEvents.applyEdit(deps.getCurrentChatId(), resp.message);
                    BF.composer.clearEdit();
                })
                .catch(function () {
                    sendBtn.disabled = false;
                });
            return;
        }

        if (!text) return;

        var entry = createPendingSend([], false, text, '');
        if (!persistPendingEntry(entry)) {
            deps.showToast(BF.i18n.t('error.pendingStorage'), false);
            return;
        }
        BF.composer.clearForSend();
        showPendingSend(entry);
        runPendingSend(entry, false);
    }

    function sendMessageWithFiles(files, asDocuments, caption) {
        if (BF.composer.getEdit()) {
            // Во время редактирования attach-flow заблокирован, чтобы не отправить новое сообщение
            // вместо правки исходного. Завершите или отмените редактирование.
            return;
        }
        BF.composer.stopTyping(true);
        var text = (caption != null ? caption : BF.composer.getText()).trim();
        var entry = createPendingSend(files, asDocuments, '', text);
        if (!persistPendingEntry(entry)) {
            releasePendingPreviews(entry);
            deps.showToast(BF.i18n.t('error.pendingStorage'), false);
            return;
        }
        BF.composer.clearForSend();
        showPendingSend(entry);
        runPendingSend(entry, false);
    }

    function init(options) {
        deps = options;
        messagesArea = document.querySelector('#messagesArea');
        sendBtn = document.querySelector('#sendBtn');
        attachBtn = document.querySelector('#attachBtn');
        fileInput = document.querySelector('#fileInput');

        sendBtn.addEventListener('click', sendMessage);

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
                var matches =
                    files.length === incomplete.length &&
                    files.every(function (file, index) {
                        return BF.files.matchesPendingUpload(incomplete[index], file);
                    });
                if (!matches) {
                    deps.showToast(BF.i18n.t('error.uploadAttachment'), false);
                    setPendingState(retryEntry, 'waiting-file');
                    return;
                }

                retryEntry.runtimeFiles = new Array(retryEntry.uploads.length);
                incomplete.forEach(function (upload, index) {
                    var file = files[index];
                    retryEntry.runtimeFiles[upload.index] = file;
                    var attachment = retryEntry.localMessage.content.attachments[upload.index];
                    if (
                        attachment &&
                        (upload.uploadType === IMAGE_UPLOAD_TYPE || upload.uploadType === GIF_UPLOAD_TYPE)
                    ) {
                        var previewUrl = URL.createObjectURL(file);
                        retryEntry.previewUrls.push(previewUrl);
                        attachment.localPreviewUrl = previewUrl;
                    }
                });
                refreshPendingElement(retryEntry);
                retryEntry.retrying = true;
                runPendingSend(retryEntry, true).finally(function () {
                    retryEntry.retrying = false;
                });
                return;
            }
            BF.composer.openAttach(files);
        });
    }

    window.BF.sendQueue = {
        init: init,
        sendMessage: sendMessage,
        sendMessageWithFiles: sendMessageWithFiles,
        cancelPendingSend: cancelPendingSend,
        retryPendingSend: retryPendingSend,
        mergePendingUploadsIntoMessages: mergePendingUploadsIntoMessages,
        reconcilePendingUpload: reconcilePendingUpload,
        restorePendingSends: restorePendingSends
    };
})();
