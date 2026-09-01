/**
 * Message rendering — bubbles, attachments, image grid, audio, video, documents.
 * Requires: BF.utils, BF.files
 * Exposes: BF.messages
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var u = function () { return BF.utils; };

    function normalizeProgress(percent) {
        return Math.max(0, Math.min(100, Number(percent) || 0));
    }

    function updateMessageStatus(statusEl, isRead, isPending) {
        statusEl.replaceChildren();
        var icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        icon.setAttribute('class', 'msg-status-icon'
            + (isRead ? ' msg-status-icon--read' : '')
            + (isPending ? ' msg-status-icon--pending' : ''));
        icon.setAttribute('viewBox', isPending ? '0 0 16 16' : (isRead ? '0 0 20 12' : '0 0 12 12'));
        icon.setAttribute('aria-hidden', 'true');
        icon.setAttribute('focusable', 'false');

        var paths = isPending
            ? ['M8 1.5a6.5 6.5 0 1 1-6.5 6.5A6.5 6.5 0 0 1 8 1.5Z', 'M8 4.5V8l2.5 1.5']
            : (isRead
                ? ['M1 6.2 4.5 9.8 11 2', 'M7 6.2 10.5 9.8 17 2']
                : ['M1 6.2 4.5 9.8 11 2']);
        paths.forEach(function (d) {
            var path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            path.setAttribute('d', d);
            icon.appendChild(path);
        });
        statusEl.appendChild(icon);
        statusEl.setAttribute('aria-label', BF.i18n.t(isPending ? 'message.status.sending' : (isRead ? 'message.status.read' : 'message.status.delivered')));
    }

    // --- Audio Player Singleton ---
    var AudioPlayer = {
        current: null,
        play: function (item) {
            if (this.current && this.current !== item) {
                this.current.audio.pause();
                this.current.playBtn.textContent = '\u25B6';
            }
            item.audio.play();
            item.playBtn.textContent = '\u23F8';
            this.current = item;
        },
        pause: function () {
            if (!this.current) return;
            this.current.audio.pause();
            this.current.playBtn.textContent = '\u25B6';
        },
        toggle: function (item) {
            if (this.current === item && !item.audio.paused) this.pause();
            else this.play(item);
        }
    };

    // --- Attachment rendering ---

    function normType(a) {
        if (a.type === 7 || a.type === '7') return 'STICKER';
        if (a.type === 8 || a.type === '8') return 'FORWARDED_MESSAGE';
        return a.type;
    }

    function renderAttachments(attachments, bubble, onMediaClick) {
        if (!attachments || attachments.length === 0) return;

        var images = [], videos = [], audios = [], docs = [];
        attachments.forEach(function (a) {
            var t = normType(a);
            if (t === 'FORWARDED_MESSAGE') return;
            var norm = (t !== a.type) ? Object.assign({}, a, { type: t }) : a;
            switch (t) {
                case 'IMAGE': case 'GIF': case 'STICKER': images.push(norm); break;
                case 'VIDEO': videos.push(norm); break;
                case 'AUDIO': case 'VOICE': audios.push(norm); break;
                default: docs.push(norm); break;
            }
        });

        if (images.length === 0 && videos.length === 0 && audios.length === 0 && docs.length === 0) return;

        var div = document.createElement('div');
        div.className = 'msg-attachments';

        if (images.length > 0) renderImageGrid(images, div, onMediaClick);
        if (videos.length > 0) renderVideos(videos, div, onMediaClick);
        if (audios.length > 0) renderAudios(audios, div);
        if (docs.length > 0) renderDocs(docs, div);

        bubble.appendChild(div);
    }

    // Принимает имя типа: вызывается и по вложению (normType), и по ReplyInfo,
    // где сервер отдаёт только тип первого вложения оригинала, без самого вложения.
    function attachmentSummary(type, fileName) {
        switch (type) {
            case 'IMAGE': case 'GIF': return '\u{1F4F7} ' + BF.i18n.t('attachment.photo');
            case 'VIDEO': return '\u{1F3AC} ' + BF.i18n.t('attachment.video');
            case 'AUDIO': return '\u{1F3B5} ' + BF.i18n.t('attachment.audio');
            case 'VOICE': return '\u{1F3A4} ' + BF.i18n.t('attachment.voice');
            case 'STICKER': return '\u{1F92A} ' + BF.i18n.t('attachment.sticker');
            case 'DOCUMENT': return '\u{1F4C4} ' + (fileName || BF.i18n.t('attachment.document'));
            default: return '\u{1F4CE} ' + BF.i18n.t('attachment.generic');
        }
    }

    function renderForwardedBlock(fwd, container, onMediaClick) {
        if (!fwd) return;
        var box = document.createElement('div');
        box.className = 'fwd-block';
        var author = document.createElement('div');
        author.className = 'fwd-author';
        author.textContent = fwd.authorName || '';
        box.appendChild(author);

        if (fwd.attachments && fwd.attachments.length > 0) {
            renderAttachments(fwd.attachments, box, onMediaClick);
        }

        if (fwd.text) {
            var txt = document.createElement('div');
            txt.className = 'fwd-text md';
            txt.innerHTML = u().renderMarkdown(fwd.text);
            box.appendChild(txt);
        }

        container.appendChild(box);
    }

    // Цитата ответа строится из ReplyInfo, который сервер резолвит из живого оригинала.
    // Раньше сюда приходил снапшот, и правка оригинала в цитате не отражалась.
    function renderReplyQuote(reply, container, onClick) {
        if (!reply) return;
        var q = document.createElement('div');
        q.className = 'reply-quote';

        var au = document.createElement('div');
        au.className = 'rq-author';
        var t = document.createElement('div');
        t.className = 'rq-text';

        if (reply.isDeleted) {
            // Сервер не отдаёт содержимое удалённого оригинала — цитата не должна быть
            // способом его прочитать, и переходить по ней некуда.
            q.classList.add('deleted');
            au.textContent = 'Сообщение удалено';
            t.textContent = '';
            q.appendChild(au);
            q.appendChild(t);
            container.appendChild(q);
            return;
        }

        if (reply.messageId) q.dataset.origId = reply.messageId;
        au.textContent = reply.senderName || '';
        t.textContent = reply.textPreview ||
            (reply.firstAttachmentType ? attachmentSummary(reply.firstAttachmentType) : '');
        q.appendChild(au);
        q.appendChild(t);

        if (typeof onClick === 'function') {
            q.addEventListener('click', function (e) {
                e.stopPropagation();
                onClick(reply.messageId);
            });
        }

        container.appendChild(q);
    }


    function renderImageGrid(images, container, onMediaClick) {
        var isSticker = images.length === 1 && images[0].type === 'STICKER';
        if (isSticker) {
            var sticker = images[0];
            var stickerFile = BF.files.getCachedFileUrl(sticker.fileId);
            var stickerUrl = (stickerFile && stickerFile.url) || (stickerFile && stickerFile.previewUrl) || sticker.previewUrl || '';
            var img = document.createElement('img');
            img.className = 'attach-sticker';
            img.src = stickerUrl;
            img.loading = 'lazy';
            BF.files.bindResilientMedia(img, sticker.fileId, false);
            img.addEventListener('click', function () {
                if (BF.stickerPack) BF.stickerPack.openByFile(sticker.fileId);
            });
            container.appendChild(img);
            return;
        }

        var grid = document.createElement('div');
        var visible = Math.min(images.length, 10);
        grid.className = 'attach-image-grid grid-' + visible;

        if (visible === 1) {
            var w = images[0].imageWidth || 0;
            var h = images[0].imageHeight || 0;
            if (w > 0 && h > 0) {
                grid.style.width = Math.min(w, 400) + 'px';
                grid.style.maxWidth = '100%';
                grid.style.aspectRatio = w + ' / ' + h;
                grid.style.maxHeight = '350px';
            }
        } else {
            grid.style.width = '400px';
            grid.style.maxWidth = '100%';
        }

        for (var i = 0; i < visible; i++) {
            var attachment = images[i];
            var file = BF.files.getCachedFileUrl(attachment.fileId);
            var url = (file && file.url) || '';
            var prev = (file && file.previewUrl) || attachment.previewUrl || '';

            var im = document.createElement('img');
            if (attachment.imageWidth > 0 && attachment.imageHeight > 0) { im.width = attachment.imageWidth; im.height = attachment.imageHeight; }
            im.src = attachment.localPreviewUrl || prev || url;

            if (attachment.isPending) {
                var item = document.createElement('div');
                item.className = 'attach-image-item is-uploading';
                item.dataset.uploadIndex = attachment.uploadIndex;
                im.alt = attachment.fileName || '';
                item.appendChild(im);
                item.appendChild(createCircularProgress(attachment.uploadProgress || 0));
                grid.appendChild(item);
            } else {
                im.loading = 'lazy';
                im.onerror = (function (im2, u) { return function () { if (im2.src !== u && u) im2.src = u; }; })(im, url);
                BF.files.bindResilientMedia(im, attachment.fileId, true);
                im.addEventListener('click', (function (u2, fid) { return function () { if (onMediaClick) onMediaClick('image', u2, fid); }; })(url || prev, attachment.fileId));
                grid.appendChild(im);
            }
        }
        container.appendChild(grid);
    }

    function createCircularProgress(percent) {
        var progress = normalizeProgress(percent);
        var wrap = document.createElement('div');
        wrap.className = 'upload-progress-circle';
        wrap.setAttribute('role', 'progressbar');
        wrap.setAttribute('aria-label', BF.i18n.t('file.uploading'));
        wrap.setAttribute('aria-valuemin', '0');
        wrap.setAttribute('aria-valuemax', '100');
        wrap.setAttribute('aria-valuenow', progress);
        wrap.innerHTML =
            '<svg viewBox="0 0 44 44" aria-hidden="true">' +
            '<circle class="upload-progress-track" cx="22" cy="22" r="18"></circle>' +
            '<circle class="upload-progress-value" cx="22" cy="22" r="18" pathLength="100"></circle>' +
            '</svg><span>' + Math.round(progress) + '%</span>';
        wrap.querySelector('.upload-progress-value').style.strokeDashoffset = String(100 - progress);
        return wrap;
    }

    function renderVideos(videos, container, onMediaClick) {
        videos.forEach(function (a) {
            var fd = BF.files.getCachedFileUrl(a.fileId);
            var url = (fd && fd.url) || '';
            var prev = (fd && fd.previewUrl) || a.previewUrl || '';

            var wrap = document.createElement('div');
            wrap.className = 'attach-video-wrap';
            var vid = document.createElement('video');
            vid.preload = 'metadata';
            if (prev) vid.poster = prev;
            vid.src = url;
            BF.files.bindResilientMedia(vid, a.fileId, false);
            var ov = document.createElement('div');
            ov.className = 'video-play-overlay';
            ov.innerHTML = '<span>\u25B6</span>';
            wrap.appendChild(vid);
            wrap.appendChild(ov);
            wrap.addEventListener('click', function () { if (onMediaClick) onMediaClick('video', url, a.fileId); });
            container.appendChild(wrap);
        });
    }

    function renderAudios(audios, container) {
        audios.forEach(function (a) {
            var fd = BF.files.getCachedFileUrl(a.fileId);
            var url = (fd && fd.url) || '';
            var isVoice = a.type === 'VOICE';

            var wrap = document.createElement('div');
            wrap.className = 'attach-audio';

            var playBtn = document.createElement('button');
            playBtn.className = 'audio-play-btn';
            playBtn.textContent = '\u25B6';

            var info = document.createElement('div');
            info.className = 'audio-info';
            var progress = document.createElement('div');
            progress.className = 'audio-progress';
            var progressFill = document.createElement('div');
            progressFill.className = 'audio-progress-fill';
            progress.appendChild(progressFill);
            var meta = document.createElement('div');
            meta.className = 'audio-meta';
            var timeEl = document.createElement('span');
            timeEl.className = 'audio-time';
            timeEl.textContent = '0:00';
            var nameEl = document.createElement('span');
            nameEl.className = 'audio-name';
            nameEl.textContent = isVoice ? '\u{1F3A4} ' + BF.i18n.t('attachment.voice') : (a.fileName || BF.i18n.t('attachment.audio'));
            meta.appendChild(timeEl);
            meta.appendChild(nameEl);
            info.appendChild(progress);
            info.appendChild(meta);
            wrap.appendChild(playBtn);
            wrap.appendChild(info);

            var audio = new Audio(url);
            audio.preload = 'metadata';
            var refreshed = false;
            audio.addEventListener('error', function () {
                if (refreshed) {
                    playBtn.disabled = true;
                    playBtn.classList.add('bf-load-failed');
                    nameEl.textContent = isVoice ? '\u{1F3A4} ' + BF.i18n.t('attachment.voice.unavailable') : BF.i18n.t('attachment.audio.unavailable');
                    return;
                }
                refreshed = true;
                BF.files.refreshFileUrl(a.fileId).then(function (fd) {
                    var fresh = fd && (fd.url || fd.previewUrl);
                    if (fresh) audio.src = fresh;
                    else {
                        playBtn.disabled = true;
                        playBtn.classList.add('bf-load-failed');
                        nameEl.textContent = isVoice ? '\u{1F3A4} ' + BF.i18n.t('attachment.voice.unavailable') : BF.i18n.t('attachment.audio.unavailable');
                    }
                });
            });
            var item = { audio: audio, playBtn: playBtn, progressFill: progressFill, timeEl: timeEl };

            audio.addEventListener('loadedmetadata', function () { timeEl.textContent = u().formatDuration(audio.duration); });
            audio.addEventListener('timeupdate', function () {
                if (audio.duration) {
                    progressFill.style.width = (audio.currentTime / audio.duration * 100) + '%';
                    timeEl.textContent = u().formatDuration(audio.currentTime);
                }
            });
            audio.addEventListener('ended', function () {
                playBtn.textContent = '\u25B6';
                progressFill.style.width = '0%';
                timeEl.textContent = u().formatDuration(audio.duration);
                AudioPlayer.current = null;
            });
            progress.addEventListener('click', function (e) {
                var rect = progress.getBoundingClientRect();
                var ratio = (e.clientX - rect.left) / rect.width;
                if (audio.duration) audio.currentTime = ratio * audio.duration;
            });
            playBtn.addEventListener('click', function () { AudioPlayer.toggle(item); });
            container.appendChild(wrap);
        });
    }

    function renderDocs(docs, container) {
        docs.forEach(function (a) {
            var fd = BF.files.getCachedFileUrl(a.fileId);
            var url = (fd && fd.url) || '';
            var link = document.createElement(a.isPending ? 'div' : 'a');
            link.className = 'attach-doc';
            if (a.isPending) {
                link.classList.add('is-uploading');
                link.dataset.uploadIndex = a.uploadIndex;
            } else {
                link.href = url;
                link.target = '_blank';
                link.rel = 'noopener';
                link.download = a.fileName || '';
                BF.files.bindResilientLink(link, a.fileId);
            }
            var progress = normalizeProgress(a.uploadProgress);
            link.innerHTML =
                '<span class="attach-doc-icon">' + u().docIcon() + '</span>' +
                '<div class="attach-doc-info">' +
                '<div class="attach-doc-name">' + u().escapeHtml(a.fileName || BF.i18n.t('attachment.file')) + '</div>' +
                (a.isPending
                    ? '<div class="attach-upload-progress" role="progressbar" aria-label="' + u().escapeHtml(BF.i18n.t('file.uploading')) + '" aria-valuemin="0" aria-valuemax="100" aria-valuenow="' + progress + '">' +
                      '<div class="attach-upload-progress-fill" style="width:' + progress + '%"></div></div>'
                    : '') +
                '<div class="attach-doc-size">' + u().formatFileSize(a.attachmentSize || 0) + '</div>' +
                '</div>';
            container.appendChild(link);
        });
    }

    function updateAttachmentProgress(messageId, attachmentIndex, percent) {
        var group = Array.prototype.find.call(document.querySelectorAll('.msg-group'), function (el) {
            return String(el.dataset.msgId) === String(messageId);
        });
        if (!group) return;

        var progress = Math.round(normalizeProgress(percent));
        var item = group.querySelector('[data-upload-index="' + attachmentIndex + '"]');
        if (!item) return;

        var circular = item.querySelector('.upload-progress-circle');
        if (circular) {
            circular.setAttribute('aria-valuenow', progress);
            circular.querySelector('.upload-progress-value').style.strokeDashoffset = String(100 - progress);
            circular.querySelector('span').textContent = progress + '%';
        }

        var linear = item.querySelector('.attach-upload-progress');
        if (linear) {
            linear.setAttribute('aria-valuenow', progress);
            linear.querySelector('.attach-upload-progress-fill').style.width = progress + '%';
        }
    }

    /**
     * Build a single message DOM element.
     * @param {Object} msg — mapped message object from BF.api
     * @param {number} myUserId
     * @param {Function} [getUserFn] — async function(userId) → user
     * @param {Function} [onMediaClick] — function(type, url, fileId)
     * @param {Object} [opts] — { onReplyClick: function(originalMessageId), groupedWithPrevious: boolean, showSenderAvatar: boolean, showSenderGutter: boolean }
     * @returns {Promise<HTMLElement>}
     */
    function buildMessageElement(msg, myUserId, getUserFn, onMediaClick, opts) {
        // Системные сообщения (kick, pin/unpin/unpin-all, и т.п.) — центрированная
        // таблетка без облачка, как в Telegram.
        if (msg.type === 2 || msg.type === 'SYSTEM') {
            var sys = document.createElement('div');
            sys.className = 'msg-group msg-system';
            sys.dataset.msgId = msg.id;
            var pill = document.createElement('span');
            pill.className = 'msg-system-pill';
            pill.textContent = (msg.content && msg.content.text) || '';
            sys.appendChild(pill);
            return Promise.resolve(sys);
        }

        var isOutgoing = msg.senderId === myUserId;
        var direction = isOutgoing ? 'outgoing' : 'incoming';
        var allAtts = (msg.content && msg.content.attachments) || [];
        var forwards = [];
        var mediaAtts = [];
        for (var i = 0; i < allAtts.length; i++) {
            if (normType(allAtts[i]) === 'FORWARDED_MESSAGE') {
                // Переслать можно несколько сообщений сразу — берём все, иначе часть пропадёт.
                if (allAtts[i].forwardedMessage) forwards.push(allAtts[i].forwardedMessage);
            } else {
                mediaAtts.push(allAtts[i]);
            }
        }
        forwards.sort(function (a, b) { return (a.order || 0) - (b.order || 0); });
        var fwd = forwards.length > 0;
        var _firstAtt = mediaAtts[0];
        var _firstAttType = _firstAtt ? normType(_firstAtt) : null;
        var isSticker = mediaAtts.length === 1 && _firstAttType === 'STICKER' && !fwd;

        // Ответ приходит явным полем. Раньше reply и forward различались догадкой «есть ли
        // оригинал среди загруженных», из-за чего ответ превращался в пересылку после прокрутки.
        var replyTo = msg.replyTo || null;

        var group = document.createElement('div');
        group.className = 'msg-group ' + direction + (opts && opts.groupedWithPrevious ? ' grouped-with-previous' : '');
        if (msg.isPending) group.classList.add('pending-send');
        if (msg.pendingState) group.classList.add('pending-' + msg.pendingState);
        if (!isOutgoing && opts && opts.showSenderGutter) group.classList.add('has-sender-gutter');
        group.dataset.msgId = msg.id;

        var promise = Promise.resolve();

        if (!isOutgoing && opts && opts.showSenderAvatar && getUserFn) {
            promise = getUserFn(msg.senderId).then(function (sender) {
                if (sender) {
                    var fullName = ((sender.firstName || '') + ' ' + (sender.lastName || '')).trim() || sender.username;
                    var avEl = document.createElement('span');
                    avEl.className = 'msg-sender-avatar';
                    avEl.dataset.senderName = fullName || BF.i18n.t('common.user');
                    avEl.setAttribute('aria-label', fullName || BF.i18n.t('common.user'));
                    var pic = sender.profilePicturePreview || sender.profilePicture;
                    if (pic) {
                        var img = document.createElement('img');
                        img.src = pic; img.alt = '';
                        avEl.appendChild(img);
                    } else {
                        avEl.textContent = (fullName || '?')[0].toUpperCase();
                    }
                    group.classList.add('has-sender-avatar');
                    group.appendChild(avEl);
                }
            });
        }

        return promise.then(function () {
            var bubble = document.createElement('div');
            var text = msg.content && msg.content.text;

            var hasImg = false, hasVideo = false, hasAudio = false, hasDoc = false;
            mediaAtts.forEach(function (a) {
                switch (normType(a)) {
                    case 'IMAGE': case 'GIF': hasImg = true; break;
                    case 'STICKER': break;
                    case 'VIDEO': hasVideo = true; break;
                    case 'AUDIO': case 'VOICE': hasAudio = true; break;
                    default: hasDoc = true; break;
                }
            });
            var hasImages = hasImg && !isSticker;
            var imageOnly = hasImages && !hasVideo && !hasAudio && !hasDoc && !text && !fwd;
            var videoOnly = hasVideo && !hasImg && !hasAudio && !hasDoc && !text && !fwd;
            var videoWithText = hasVideo && !!text;
            var docsOnly = hasDoc && !hasImg && !hasVideo && !hasAudio && !fwd;
            bubble.className = 'msg-bubble ' + direction + (isSticker ? ' sticker' : '')
                + (hasImages ? ' has-images' : '')
                + (imageOnly ? ' image-only' : '')
                + (hasVideo ? ' has-videos' : '')
                + (videoOnly ? ' video-only' : '')
                + (videoWithText ? ' video-with-text' : '')
                + (docsOnly ? ' docs-only' : '');

            if (replyTo) renderReplyQuote(replyTo, bubble, opts && opts.onReplyClick);
            for (var fi = 0; fi < forwards.length; fi++) {
                renderForwardedBlock(forwards[fi], bubble, onMediaClick);
            }

            if (mediaAtts.length > 0) renderAttachments(mediaAtts, bubble, onMediaClick);

            if (text) {
                var textEl = document.createElement('div');
                textEl.className = 'msg-text md';
                textEl.innerHTML = u().renderMarkdown(text);
                bubble.appendChild(textEl);
            }

            if (!isSticker) {
                if (msg.isPending && msg.pendingState) {
                    var pendingRow = document.createElement('div');
                    pendingRow.className = 'msg-pending-row';
                    var pendingLabel = document.createElement('span');
                    pendingLabel.className = 'msg-pending-label';
                    pendingLabel.textContent = BF.i18n.t('message.pending.' + msg.pendingState);
                    pendingRow.appendChild(pendingLabel);

                    var action = null;
                    if (msg.pendingState === 'uploading' && opts && opts.onPendingCancel) {
                        action = document.createElement('button');
                        action.textContent = BF.i18n.t('message.cancelUpload');
                        action.addEventListener('click', function () { opts.onPendingCancel(msg.id); });
                    } else if ((msg.pendingState === 'failed' || msg.pendingState === 'unknown' ||
                        msg.pendingState === 'processing' || msg.pendingState === 'waiting-file') &&
                        opts && opts.onPendingRetry) {
                        action = document.createElement('button');
                        action.textContent = BF.i18n.t('message.retry');
                        action.addEventListener('click', function () { opts.onPendingRetry(msg.id); });
                    }
                    if (action) {
                        action.type = 'button';
                        action.className = 'msg-pending-action';
                        pendingRow.appendChild(action);
                    }
                    bubble.appendChild(pendingRow);
                }

                var meta = document.createElement('div');
                meta.className = 'msg-meta' + (imageOnly || videoOnly ? ' msg-img-overlay-meta' : '');
                if (msg.isEdited) {
                    var editedEl = document.createElement('span');
                    editedEl.className = 'msg-edited';
                    editedEl.textContent = BF.i18n.t('message.edited');
                    meta.appendChild(editedEl);
                }
                var timeEl = document.createElement('span');
                timeEl.className = 'msg-time';
                timeEl.textContent = u().formatTime(msg.sentAt);
                meta.appendChild(timeEl);
                if (isOutgoing) {
                    var statusEl = document.createElement('span');
                    statusEl.className = 'msg-status';
                    statusEl.dataset.msgId = msg.id;
                    var readCount = (msg.readBy || []).filter(function (id) { return id !== myUserId; }).length;
                    updateMessageStatus(statusEl, readCount > 0, !!msg.isPending);
                    meta.appendChild(statusEl);
                }
                bubble.appendChild(meta);
            }

            group.appendChild(bubble);
            return group;
        });
    }

    window.BF.messages = {
        buildMessageElement: buildMessageElement,
        updateMessageStatus: updateMessageStatus,
        updateAttachmentProgress: updateAttachmentProgress,
        renderAttachments: renderAttachments,
        renderForwardedBlock: renderForwardedBlock,
        renderReplyQuote: renderReplyQuote
    };
})();
