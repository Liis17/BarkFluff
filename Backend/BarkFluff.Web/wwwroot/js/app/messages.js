/**
 * Message rendering — bubbles, attachments, image grid, audio, video, documents.
 * Requires: BF.utils, BF.files
 * Exposes: BF.messages
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var u = function () { return BF.utils; };

    function updateMessageStatus(statusEl, isRead) {
        statusEl.replaceChildren();
        var icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        icon.setAttribute('class', 'msg-status-icon' + (isRead ? ' msg-status-icon--read' : ''));
        icon.setAttribute('viewBox', isRead ? '0 0 20 12' : '0 0 12 12');
        icon.setAttribute('aria-hidden', 'true');
        icon.setAttribute('focusable', 'false');

        var paths = isRead
            ? ['M1 6.2 4.5 9.8 11 2', 'M7 6.2 10.5 9.8 17 2']
            : ['M1 6.2 4.5 9.8 11 2'];
        paths.forEach(function (d) {
            var path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            path.setAttribute('d', d);
            icon.appendChild(path);
        });
        statusEl.appendChild(icon);
        statusEl.setAttribute('aria-label', isRead ? 'Прочитано' : 'Доставлено');
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

    function attachmentSummary(att) {
        var t = normType(att);
        switch (t) {
            case 'IMAGE': case 'GIF': return '\u{1F4F7} Фото';
            case 'VIDEO': return '\u{1F3AC} Видео';
            case 'AUDIO': return '\u{1F3B5} Аудио';
            case 'VOICE': return '\u{1F3A4} Голосовое';
            case 'STICKER': return '\u{1F92A} Стикер';
            case 'DOCUMENT': return '\u{1F4C4} ' + (att.fileName || 'Документ');
            default: return '\u{1F4CE} Вложение';
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
            txt.className = 'fwd-text';
            txt.textContent = fwd.text;
            box.appendChild(txt);
        }

        container.appendChild(box);
    }

    function renderReplyQuote(fwd, container, onClick) {
        if (!fwd) return;
        var q = document.createElement('div');
        q.className = 'reply-quote';
        if (fwd.originalMessageId) q.dataset.origId = fwd.originalMessageId;
        var au = document.createElement('div');
        au.className = 'rq-author';
        au.textContent = fwd.authorName || '';
        q.appendChild(au);

        var preview = '';
        if (fwd.text) preview = fwd.text;
        else if (fwd.attachments && fwd.attachments.length > 0) preview = attachmentSummary(fwd.attachments[0]);
        var t = document.createElement('div');
        t.className = 'rq-text';
        t.textContent = preview;
        q.appendChild(t);

        if (typeof onClick === 'function') {
            q.addEventListener('click', function (e) {
                e.stopPropagation();
                onClick(fwd.originalMessageId);
            });
        }

        container.appendChild(q);
    }

    function renderImageGrid(images, container, onMediaClick) {
        var isSticker = images.length === 1 && images[0].type === 'STICKER';
        if (isSticker) {
            var a = images[0];
            var fd = BF.files.getCachedFileUrl(a.fileId);
            var url = (fd && fd.url) || (fd && fd.previewUrl) || a.previewUrl || '';
            var img = document.createElement('img');
            img.className = 'attach-sticker';
            img.src = url;
            img.loading = 'lazy';
            BF.files.bindResilientMedia(img, a.fileId, false);
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
            var a = images[i];
            var fd = BF.files.getCachedFileUrl(a.fileId);
            var url = (fd && fd.url) || '';
            var prev = (fd && fd.previewUrl) || a.previewUrl || '';

            var im = document.createElement('img');
            if (a.imageWidth > 0 && a.imageHeight > 0) { im.width = a.imageWidth; im.height = a.imageHeight; }
            im.src = prev || url; im.loading = 'lazy';
            im.onerror = (function (im2, u) { return function () { if (im2.src !== u && u) im2.src = u; }; })(im, url);
            BF.files.bindResilientMedia(im, a.fileId, true);
            im.addEventListener('click', (function (u2, fid) { return function () { if (onMediaClick) onMediaClick('image', u2, fid); }; })(url || prev, a.fileId));
            grid.appendChild(im);
        }
        container.appendChild(grid);
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
            nameEl.textContent = isVoice ? '\u{1F3A4} Голосовое' : (a.fileName || 'Аудио');
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
                    nameEl.textContent = isVoice ? '\u{1F3A4} Голосовое недоступно' : 'Аудио недоступно';
                    return;
                }
                refreshed = true;
                BF.files.refreshFileUrl(a.fileId).then(function (fd) {
                    var fresh = fd && (fd.url || fd.previewUrl);
                    if (fresh) audio.src = fresh;
                    else {
                        playBtn.disabled = true;
                        playBtn.classList.add('bf-load-failed');
                        nameEl.textContent = isVoice ? '\u{1F3A4} Голосовое недоступно' : 'Аудио недоступно';
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
            var link = document.createElement('a');
            link.className = 'attach-doc';
            link.href = url;
            link.target = '_blank';
            link.rel = 'noopener';
            link.download = a.fileName || '';
            BF.files.bindResilientLink(link, a.fileId);
            link.innerHTML =
                '<span class="attach-doc-icon">' + u().docIcon(a.fileName) + '</span>' +
                '<div class="attach-doc-info">' +
                '<div class="attach-doc-name">' + u().escapeHtml(a.fileName || 'Файл') + '</div>' +
                '<div class="attach-doc-size">' + u().formatFileSize(a.attachmentSize || 0) + '</div>' +
                '</div>';
            container.appendChild(link);
        });
    }

    /**
     * Build a single message DOM element.
     * @param {Object} msg — mapped message object from BF.api
     * @param {number} myUserId
     * @param {boolean} isGroupChat
     * @param {Function} [getUserFn] — async function(userId) → user
     * @param {Function} [onMediaClick] — function(type, url, fileId)
     * @param {Object} [opts] — { knownMessageIds: Set, onReplyClick: function(originalMessageId) }
     * @returns {Promise<HTMLElement>}
     */
    function buildMessageElement(msg, myUserId, isGroupChat, getUserFn, onMediaClick, opts) {
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
        var fwdAtt = null;
        var mediaAtts = [];
        for (var i = 0; i < allAtts.length; i++) {
            if (normType(allAtts[i]) === 'FORWARDED_MESSAGE') {
                if (!fwdAtt) fwdAtt = allAtts[i];
            } else {
                mediaAtts.push(allAtts[i]);
            }
        }
        var fwd = fwdAtt && fwdAtt.forwardedMessage;
        var _firstAtt = mediaAtts[0];
        var _firstAttType = _firstAtt ? normType(_firstAtt) : null;
        var isSticker = mediaAtts.length === 1 && _firstAttType === 'STICKER' && !fwd;

        var knownIds = opts && opts.knownMessageIds;
        var isReply = !!(fwd && knownIds && fwd.originalMessageId && knownIds.has(fwd.originalMessageId));

        var group = document.createElement('div');
        group.className = 'msg-group ' + direction;
        group.dataset.msgId = msg.id;

        var promise = Promise.resolve();

        if (!isOutgoing && isGroupChat && getUserFn) {
            promise = getUserFn(msg.senderId).then(function (sender) {
                if (sender) {
                    var fullName = ((sender.firstName || '') + ' ' + (sender.lastName || '')).trim() || sender.username;
                    var nameEl = document.createElement('div');
                    nameEl.className = 'msg-sender';

                    var avEl = document.createElement('span');
                    avEl.className = 'msg-sender-avatar';
                    var pic = sender.profilePicturePreview || sender.profilePicture;
                    if (pic) {
                        var img = document.createElement('img');
                        img.src = pic; img.alt = '';
                        avEl.appendChild(img);
                    } else {
                        avEl.textContent = (fullName || '?')[0].toUpperCase();
                    }
                    nameEl.appendChild(avEl);

                    var nameText = document.createElement('span');
                    nameText.textContent = fullName;
                    nameEl.appendChild(nameText);

                    group.appendChild(nameEl);
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

            if (fwd) {
                if (isReply) {
                    renderReplyQuote(fwd, bubble, opts && opts.onReplyClick);
                } else {
                    renderForwardedBlock(fwd, bubble, onMediaClick);
                }
            }

            if (mediaAtts.length > 0) renderAttachments(mediaAtts, bubble, onMediaClick);

            if (text) {
                var textEl = document.createElement('div');
                textEl.className = 'msg-text';
                textEl.textContent = text;
                bubble.appendChild(textEl);
            }

            if (!isSticker) {
                var meta = document.createElement('div');
                meta.className = 'msg-meta' + (imageOnly || videoOnly ? ' msg-img-overlay-meta' : '');
                if (msg.isEdited) {
                    var editedEl = document.createElement('span');
                    editedEl.className = 'msg-edited';
                    editedEl.textContent = 'изм.';
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
                    updateMessageStatus(statusEl, readCount > 0);
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
        renderAttachments: renderAttachments,
        renderForwardedBlock: renderForwardedBlock,
        renderReplyQuote: renderReplyQuote
    };
})();
