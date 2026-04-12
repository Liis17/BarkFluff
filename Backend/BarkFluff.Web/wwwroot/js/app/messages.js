/**
 * Message rendering — bubbles, attachments, image grid, audio, video, documents.
 * Requires: BF.utils, BF.files
 * Exposes: BF.messages
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var u = function () { return BF.utils; };

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

    function renderAttachments(attachments, bubble, onMediaClick) {
        if (!attachments || attachments.length === 0) return;

        var images = [], videos = [], audios = [], docs = [];
        attachments.forEach(function (a) {
            switch (a.type) {
                case 'IMAGE': case 'GIF': case 'STICKER': images.push(a); break;
                case 'VIDEO': videos.push(a); break;
                case 'AUDIO': case 'VOICE': audios.push(a); break;
                default: docs.push(a); break;
            }
        });

        var div = document.createElement('div');
        div.className = 'msg-attachments';

        if (images.length > 0) renderImageGrid(images, div, onMediaClick);
        if (videos.length > 0) renderVideos(videos, div, onMediaClick);
        if (audios.length > 0) renderAudios(audios, div);
        if (docs.length > 0) renderDocs(docs, div);

        bubble.appendChild(div);
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
            container.appendChild(img);
            return;
        }

        var grid = document.createElement('div');
        var visible = Math.min(images.length, 4);
        grid.className = 'attach-image-grid grid-' + visible;

        for (var i = 0; i < visible; i++) {
            var a = images[i];
            var fd = BF.files.getCachedFileUrl(a.fileId);
            var url = (fd && fd.url) || '';
            var prev = (fd && fd.previewUrl) || a.previewUrl || '';

            if (i === 3 && images.length > 4) {
                var wrap = document.createElement('div');
                wrap.className = 'more-overlay';
                var im = document.createElement('img');
                im.src = prev || url; im.loading = 'lazy';
                im.onerror = (function (im2, u) { return function () { if (im2.src !== u && u) im2.src = u; }; })(im, url);
                wrap.appendChild(im);
                var cnt = document.createElement('div');
                cnt.className = 'more-count';
                cnt.textContent = '+' + (images.length - 3);
                wrap.appendChild(cnt);
                wrap.addEventListener('click', (function (u2) { return function () { if (onMediaClick) onMediaClick('image', u2); }; })(url || prev));
                grid.appendChild(wrap);
            } else {
                var im = document.createElement('img');
                im.src = prev || url; im.loading = 'lazy';
                im.onerror = (function (im2, u) { return function () { if (im2.src !== u && u) im2.src = u; }; })(im, url);
                im.addEventListener('click', (function (u2) { return function () { if (onMediaClick) onMediaClick('image', u2); }; })(url || prev));
                grid.appendChild(im);
            }
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
            var ov = document.createElement('div');
            ov.className = 'video-play-overlay';
            ov.innerHTML = '<span>\u25B6</span>';
            wrap.appendChild(vid);
            wrap.appendChild(ov);
            wrap.addEventListener('click', function () { if (onMediaClick) onMediaClick('video', url); });
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
     * @param {Function} [onMediaClick] — function(type, url)
     * @returns {Promise<HTMLElement>}
     */
    function buildMessageElement(msg, myUserId, isGroupChat, getUserFn, onMediaClick) {
        var isOutgoing = msg.senderId === myUserId;
        var direction = isOutgoing ? 'outgoing' : 'incoming';
        var isSticker = msg.content && msg.content.attachments && msg.content.attachments.length === 1 && msg.content.attachments[0].type === 'STICKER';

        var group = document.createElement('div');
        group.className = 'msg-group ' + direction;
        group.dataset.msgId = msg.id;

        var promise = Promise.resolve();

        if (!isOutgoing && isGroupChat && getUserFn) {
            promise = getUserFn(msg.senderId).then(function (sender) {
                if (sender) {
                    var nameEl = document.createElement('div');
                    nameEl.className = 'msg-sender';
                    nameEl.textContent = ((sender.firstName || '') + ' ' + (sender.lastName || '')).trim() || sender.username;
                    group.appendChild(nameEl);
                }
            });
        }

        return promise.then(function () {
            var bubble = document.createElement('div');
            bubble.className = 'msg-bubble ' + direction + (isSticker ? ' sticker' : '');

            var attachments = (msg.content && msg.content.attachments) || [];
            if (attachments.length > 0) renderAttachments(attachments, bubble, onMediaClick);

            var text = msg.content && msg.content.text;
            if (text) {
                var textEl = document.createElement('div');
                textEl.className = 'msg-text';
                textEl.textContent = text;
                bubble.appendChild(textEl);
            }

            if (!isSticker) {
                var meta = document.createElement('div');
                meta.className = 'msg-meta';
                var timeEl = document.createElement('span');
                timeEl.className = 'msg-time';
                timeEl.textContent = u().formatTime(msg.sentAt);
                meta.appendChild(timeEl);
                if (isOutgoing) {
                    var statusEl = document.createElement('span');
                    statusEl.className = 'msg-status';
                    statusEl.dataset.msgId = msg.id;
                    var readCount = (msg.readBy || []).filter(function (id) { return id !== myUserId; }).length;
                    statusEl.innerHTML = readCount > 0 ? '&#10003;&#10003;' : '&#10003;';
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
        renderAttachments: renderAttachments
    };
})();
