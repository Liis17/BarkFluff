/**
 * Shared UI utility functions.
 * Exposes: BF.utils
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    function formatTime(ts) {
        if (!ts) return '';
        var d = new Date(ts);
        return d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
    }

    function formatChatListTime(ts) {
        if (!ts) return '';
        var d = new Date(ts);
        var today = new Date();
        if (d.toDateString() === today.toDateString()) {
            return d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
        }
        var yesterday = new Date(today);
        yesterday.setDate(yesterday.getDate() - 1);
        if (d.toDateString() === yesterday.toDateString()) {
            return 'вчера в ' + d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
        }
        return d.toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' });
    }

    function formatDate(ts) {
        if (!ts) return '';
        var d = new Date(ts);
        var today = new Date();
        if (d.toDateString() === today.toDateString()) return 'Сегодня';
        var yesterday = new Date(today);
        yesterday.setDate(yesterday.getDate() - 1);
        if (d.toDateString() === yesterday.toDateString()) return 'Вчера';
        return d.toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' });
    }

    function formatFileSize(bytes) {
        if (bytes < 1024) return bytes + ' Б';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' КБ';
        return (bytes / (1024 * 1024)).toFixed(1) + ' МБ';
    }

    function truncate(text, len) {
        if (!text) return '';
        return text.length > len ? text.slice(0, len) + '...' : text;
    }

    function escapeHtml(str) {
        if (!str) return '';
        var d = document.createElement('div');
        d.textContent = str;
        return d.innerHTML;
    }

    function formatDuration(sec) {
        if (!sec || isNaN(sec)) return '0:00';
        var m = Math.floor(sec / 60);
        var s = Math.floor(sec % 60);
        return m + ':' + s.toString().padStart(2, '0');
    }

    function attachmentEmoji(type) {
        switch (type) {
            case 'IMAGE': case 'GIF': return '\u{1F4F7} Фото';
            case 'VIDEO': return '\u{1F3AC} Видео';
            case 'AUDIO': return '\u{1F3B5} Аудио';
            case 'VOICE': return '\u{1F3A4} Голосовое';
            case 'DOCUMENT': return '\u{1F4C4} Документ';
            case 'STICKER': return '\u{1F92A} Стикер';
            default: return '\u{1F4CE} Вложение';
        }
    }

    // Inline-SVG иконки для превью списка чатов (24×24, currentColor).
    var PREVIEW_ICONS = {
        image: '<svg class="preview-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>',
        video: '<svg class="preview-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M23 7l-7 5 7 5V7z"/><rect x="1" y="5" width="15" height="14" rx="2" ry="2"/></svg>',
        audio: '<svg class="preview-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/></svg>',
        voice: '<svg class="preview-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><line x1="12" y1="19" x2="12" y2="23"/><line x1="8" y1="23" x2="16" y2="23"/></svg>',
        document: '<svg class="preview-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>',
        sticker: '<svg class="preview-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12a9 9 0 1 1-9-9"/><path d="M21 12v3a6 6 0 0 1-6 6h-3"/><path d="M12 21a9 9 0 0 0 9-9"/></svg>',
        attach: '<svg class="preview-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"/></svg>',
        // Трубка со стрелкой направления.
        callIn: '<svg class="preview-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/><polyline points="15 9 21 3"/><polyline points="21 8 21 3 16 3"/></svg>',
        callOut: '<svg class="preview-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/><polyline points="16 8 22 2"/><polyline points="17 2 22 2 22 7"/></svg>'
    };

    // HTML превью вложения: SVG-иконка + текстовая подпись.
    function attachmentPreviewHtml(type) {
        var icon, label;
        switch (type) {
            case 'IMAGE': case 'GIF': icon = PREVIEW_ICONS.image; label = 'Фото'; break;
            case 'VIDEO': icon = PREVIEW_ICONS.video; label = 'Видео'; break;
            case 'AUDIO': icon = PREVIEW_ICONS.audio; label = 'Аудио'; break;
            case 'VOICE': icon = PREVIEW_ICONS.voice; label = 'Голосовое'; break;
            case 'DOCUMENT': icon = PREVIEW_ICONS.document; label = 'Документ'; break;
            case 'STICKER': icon = PREVIEW_ICONS.sticker; label = 'Стикер'; break;
            default: icon = PREVIEW_ICONS.attach; label = 'Вложение';
        }
        return icon + '<span class="preview-text">' + escapeHtml(label) + '</span>';
    }

    // HTML превью звонка по тексту системного сообщения. null — если это не звонок.
    function callPreviewHtml(text, isOutgoing) {
        if (!text) return null;
        var isMissed = text.indexOf('Пропущенный звонок') === 0 || text.indexOf('Звонок отклонён') === 0;
        var isEnded = text.indexOf('Звонок') === 0;
        if (!isMissed && !isEnded) return null;
        var icon = isOutgoing ? PREVIEW_ICONS.callOut : PREVIEW_ICONS.callIn;
        var cls = isMissed ? 'preview-call-missed' : 'preview-call';
        return '<span class="' + cls + '">' + icon + '</span>' +
            '<span class="preview-text">' + escapeHtml(truncate(text, 50)) + '</span>';
    }

    function docIcon(fileName) {
        if (!fileName) return '\u{1F4C4}';
        var ext = fileName.split('.').pop().toLowerCase();
        if (ext === 'pdf') return '\u{1F4D1}';
        if (ext === 'doc' || ext === 'docx') return '\u{1F4DD}';
        if (ext === 'xls' || ext === 'xlsx') return '\u{1F4CA}';
        if (['zip', 'rar', '7z', 'tar', 'gz'].indexOf(ext) >= 0) return '\u{1F4E6}';
        if (ext === 'apk') return '\u{1F4F1}';
        return '\u{1F4C4}';
    }

    function parseJwtPayload(token) {
        try {
            var payload = token.split('.')[1];
            return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
        } catch (e) { return null; }
    }

    // StatusTypeId enum from onliner.proto
    // 0 = UNKNOWN, 1 = STATUS_ONLINE, 2 = STATUS_OFFLINE
    function isStatusOnline(status) {
        return status === 1 || status === 'STATUS_ONLINE';
    }

    function formatLastSeen(lastSeenTs) {
        if (!lastSeenTs) return 'не в сети';
        var now = Date.now();
        var diff = now - lastSeenTs;
        if (diff < 0) diff = 0;

        var seconds = Math.floor(diff / 1000);
        var minutes = Math.floor(seconds / 60);
        var hours = Math.floor(minutes / 60);
        var days = Math.floor(hours / 24);

        if (seconds < 60) return 'был(а) в сети только что';
        if (minutes < 60) {
            if (minutes === 1) return 'был(а) в сети 1 минуту назад';
            if (minutes < 5) return 'был(а) в сети ' + minutes + ' минуты назад';
            if (minutes < 21) return 'был(а) в сети ' + minutes + ' минут назад';
            var m10 = minutes % 10;
            if (m10 === 1) return 'был(а) в сети ' + minutes + ' минуту назад';
            if (m10 >= 2 && m10 <= 4) return 'был(а) в сети ' + minutes + ' минуты назад';
            return 'был(а) в сети ' + minutes + ' минут назад';
        }
        if (hours < 24) {
            if (hours === 1) return 'был(а) в сети 1 час назад';
            if (hours < 5) return 'был(а) в сети ' + hours + ' часа назад';
            if (hours < 21) return 'был(а) в сети ' + hours + ' часов назад';
            var h10 = hours % 10;
            if (h10 === 1) return 'был(а) в сети ' + hours + ' час назад';
            if (h10 >= 2 && h10 <= 4) return 'был(а) в сети ' + hours + ' часа назад';
            return 'был(а) в сети ' + hours + ' часов назад';
        }
        if (days === 1) return 'был(а) в сети вчера';
        if (days < 7) return 'был(а) в сети ' + days + ' дн. назад';

        var d = new Date(lastSeenTs);
        return 'был(а) в сети ' + d.toLocaleDateString('ru-RU', { day: 'numeric', month: 'short' });
    }

    window.BF.utils = {
        formatTime: formatTime,
        formatChatListTime: formatChatListTime,
        formatDate: formatDate,
        formatFileSize: formatFileSize,
        truncate: truncate,
        escapeHtml: escapeHtml,
        formatDuration: formatDuration,
        attachmentEmoji: attachmentEmoji,
        attachmentPreviewHtml: attachmentPreviewHtml,
        callPreviewHtml: callPreviewHtml,
        docIcon: docIcon,
        parseJwtPayload: parseJwtPayload,
        isStatusOnline: isStatusOnline,
        formatLastSeen: formatLastSeen
    };
})();
