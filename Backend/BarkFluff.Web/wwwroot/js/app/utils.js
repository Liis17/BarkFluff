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

    window.BF.utils = {
        formatTime: formatTime,
        formatDate: formatDate,
        formatFileSize: formatFileSize,
        truncate: truncate,
        escapeHtml: escapeHtml,
        formatDuration: formatDuration,
        attachmentEmoji: attachmentEmoji,
        docIcon: docIcon,
        parseJwtPayload: parseJwtPayload,
        isStatusOnline: isStatusOnline
    };
})();
