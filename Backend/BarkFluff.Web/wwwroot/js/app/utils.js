/**
 * Shared UI utility functions.
 * Requires: BF.i18n, BF.icons
 * Exposes: BF.utils
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    function locale() {
        return BF.i18n.current();
    }

    function timeOf(d) {
        return d.toLocaleTimeString(locale(), { hour: '2-digit', minute: '2-digit' });
    }

    function formatTime(ts) {
        if (!ts) return '';
        return timeOf(new Date(ts));
    }

    function formatChatListTime(ts) {
        if (!ts) return '';
        var d = new Date(ts);
        var today = new Date();
        if (d.toDateString() === today.toDateString()) {
            return timeOf(d);
        }
        var yesterday = new Date(today);
        yesterday.setDate(yesterday.getDate() - 1);
        if (d.toDateString() === yesterday.toDateString()) {
            return BF.i18n.t('date.yesterdayAt', { time: timeOf(d) });
        }
        return d.toLocaleDateString(locale(), { day: '2-digit', month: '2-digit', year: 'numeric' });
    }

    function formatDate(ts) {
        if (!ts) return '';
        var d = new Date(ts);
        var today = new Date();
        if (d.toDateString() === today.toDateString()) return BF.i18n.t('date.today');
        var yesterday = new Date(today);
        yesterday.setDate(yesterday.getDate() - 1);
        if (d.toDateString() === yesterday.toDateString()) return BF.i18n.t('date.yesterday');
        return d.toLocaleDateString(locale(), { day: 'numeric', month: 'long', year: 'numeric' });
    }

    function formatFileSize(bytes) {
        if (bytes < 1024) return BF.i18n.t('unit.bytes', { value: bytes });
        if (bytes < 1024 * 1024) return BF.i18n.t('unit.kb', { value: (bytes / 1024).toFixed(1) });
        return BF.i18n.t('unit.mb', { value: (bytes / (1024 * 1024)).toFixed(1) });
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

    // --- Markdown rendering (safe) ---
    // Модель безопасности: текст экранируется ПЕРВЫМ (escapeHtml), затем добавляются только
    // наши теги. URL дополнительно экранируется в контексте HTML-атрибута.
    // Ссылки — allowlist схем; javascript:/data: и т.п. не становятся ссылками.

    var URL_SCHEME_ALLOW = /^(?:https?:|mailto:)/i;

    // url приходит УЖЕ экранированным (после escapeHtml). Схема (https:/mailto:) спецсимволов
    // не содержит, поэтому проверка работает и на экранированной строке.
    function sanitizeUrl(url) {
        if (!url) return null;
        var s = url.trim();
        return URL_SCHEME_ALLOW.test(s) ? s : null;
    }

    // URL уже прошёл escapeHtml, поэтому &amp; должен остаться без повторного экранирования.
    // Кавычки escapeHtml для текстового узла не гарантирует, а в href они закрывают атрибут.
    function escapeHtmlAttribute(value) {
        return value.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function escapeRawHtmlAttribute(value) {
        return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function htmlAttributes(raw) {
        var attributes = {};
        var attrRe = /([a-z][a-z0-9-]*)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+))/gi;
        var match;
        while ((match = attrRe.exec(raw)) !== null) {
            attributes[match[1].toLowerCase()] = match[2] || match[3] || match[4] || '';
        }
        return attributes;
    }

    function sanitizeImageUrl(url) {
        if (!url) return null;
        var value = url.trim();
        if (/^https?:/i.test(value)) return value;
        return null;
    }

    function htmlAlignment(value) {
        value = (value || '').toLowerCase();
        return value === 'center' || value === 'right' || value === 'left' ? value : null;
    }

    // HTML в сообщениях не передаётся в innerHTML как есть. Разрешённые теги
    // пересобираются из allowlist, все остальные далее экранируются как текст.
    function renderSafeHtmlTag(tag) {
        var parsed = tag.match(/^<\s*(\/?)\s*([a-z][a-z0-9]*)\b([^>]*)>$/i);
        if (!parsed) return null;
        var closing = parsed[1] === '/';
        var name = parsed[2].toLowerCase();
        var attrs = htmlAttributes(parsed[3]);

        if ((name === 'strong' || name === 'sub') && /^\s*$/.test(parsed[3])) {
            return closing ? '</' + name + '>' : '<' + name + '>';
        }
        if (name === 'a' && !closing) {
            var href = sanitizeUrl(attrs.href);
            return href
                ? '<a href="' + escapeRawHtmlAttribute(href) + '" target="_blank" rel="noopener noreferrer">'
                : null;
        }
        if (name === 'a' && closing) return '</a>';
        if (name === 'img' && !closing) {
            var src = sanitizeImageUrl(attrs.src);
            if (!src) return '<span class="md-image-alt">' + escapeHtml(attrs.alt || '') + '</span>';
            var width = parseInt(attrs.width, 10);
            var height = parseInt(attrs.height, 10);
            var size = '';
            if (width > 0 && width <= 2048) size += ' width="' + width + '"';
            if (height > 0 && height <= 2048) size += ' height="' + height + '"';
            return '<img class="md-image" src="' + escapeRawHtmlAttribute(src) + '" alt="'
                + escapeRawHtmlAttribute(attrs.alt || '') + '" loading="lazy"' + size + '>';
        }
        return null;
    }

    // Emphasis на «обычном» (уже экранированном) сегменте. bold до italic.
    function emphasis(s) {
        s = s.replace(/\*\*([\s\S]+?)\*\*/g, '<strong>$1</strong>');
        s = s.replace(/~~([^~]+)~~/g, '<del>$1</del>');
        s = s.replace(/(^|[^*])\*([^*\s][^*]*?)\*/g, '$1<em>$2</em>');
        // Граница слова Unicode-aware (флаг u), чтобы _ внутри слова любого языка не курсивился.
        s = s.replace(/(^|[^_\p{L}\p{N}])_([^_\s][^_]*?)_/gu, '$1<em>$2</em>');
        return s;
    }

    // Инлайн-разбор одной строки. На входе — сырой текст, на выходе — безопасный HTML.
    // Сегментный подход: защищённые куски (код/ссылки) не проходят emphasis, обычные — проходят.
    function inlineMd(raw) {
        var htmlTokens = [];
        var tokenPrefix = '\uE000bfhtml';
        var suppressedAnchor = false;
        var protectedHtml = raw.replace(/<\/?[a-z][^>]*>/gi, function (tag) {
            var anchor = tag.match(/^<\s*(\/?)\s*a\b/i);
            if (anchor && anchor[1] && suppressedAnchor) {
                suppressedAnchor = false;
                var closingToken = tokenPrefix + htmlTokens.length + '\uE001';
                htmlTokens.push('');
                return closingToken;
            }
            var safeTag = renderSafeHtmlTag(tag);
            if (safeTag === null) {
                if (anchor && !anchor[1]) {
                    suppressedAnchor = true;
                    var openingToken = tokenPrefix + htmlTokens.length + '\uE001';
                    htmlTokens.push('');
                    return openingToken;
                }
                return tag;
            }
            var token = tokenPrefix + htmlTokens.length + '\uE001';
            htmlTokens.push(safeTag);
            return token;
        });
        var esc = escapeHtml(protectedHtml);
        // Защищённые: `код`, [текст](url), голый http(s)://…
        var protectRe = /(`[^`]+`)|(\[[^\]]+\]\([^)\s]+\))|(https?:\/\/[^\s<]+)/g;
        var out = '';
        var last = 0;
        var m;
        while ((m = protectRe.exec(esc)) !== null) {
            out += emphasis(esc.slice(last, m.index));
            if (m[1]) {
                out += '<code>' + m[1].slice(1, -1) + '</code>';
            } else if (m[2]) {
                var lm = m[2].match(/^\[([^\]]+)\]\(([^)\s]+)\)$/);
                var lsafe = lm && sanitizeUrl(lm[2]);
                out += lsafe
                    ? '<a href="' + escapeHtmlAttribute(lsafe) + '" target="_blank" rel="noopener noreferrer">' + lm[1] + '</a>'
                    : m[2];
            } else {
                var asafe = sanitizeUrl(m[3]);
                out += asafe
                    ? '<a href="' + escapeHtmlAttribute(asafe) + '" target="_blank" rel="noopener noreferrer">' + m[3] + '</a>'
                    : m[3];
            }
            last = protectRe.lastIndex;
        }
        out += emphasis(esc.slice(last));
        return out.replace(new RegExp(tokenPrefix + '(\\d+)\\uE001', 'g'), function (_, index) {
            return htmlTokens[Number(index)];
        });
    }

    // Разделяет GFM-строку таблицы, не принимая \| внутри inline-кода и экранированные \|
    // за границу ячейки. Внешние | у таблицы опциональны.
    function splitTableRow(line) {
        var cells = [];
        var cell = '';
        var inCode = false;
        for (var j = 0; j < line.length; j++) {
            var ch = line[j];
            if (ch === '\\' && line[j + 1] === '|') {
                cell += '|';
                j++;
            } else if (ch === '`') {
                inCode = !inCode;
                cell += ch;
            } else if (ch === '|' && !inCode) {
                cells.push(cell.trim());
                cell = '';
            } else {
                cell += ch;
            }
        }
        cells.push(cell.trim());
        if (/^\s*\|/.test(line)) cells.shift();
        if (/\|\s*$/.test(line)) cells.pop();
        return cells;
    }

    function tableAlignClass(separator) {
        var value = separator.trim();
        var left = value[0] === ':';
        var right = value[value.length - 1] === ':';
        if (left && right) return ' md-align-center';
        if (right) return ' md-align-right';
        if (left) return ' md-align-left';
        return '';
    }

    function normalizeTableRow(cells, count) {
        var normalized = cells.slice(0, count);
        while (normalized.length < count) normalized.push('');
        return normalized;
    }

    function isTableStart(lines, index) {
        if (index + 1 >= lines.length || lines[index].indexOf('|') < 0) return false;
        var headers = splitTableRow(lines[index]);
        var separators = splitTableRow(lines[index + 1]);
        return headers.length > 0 && headers.length === separators.length
            && separators.every(function (cell) { return /^:?-+:?$/.test(cell); });
    }

    function renderTable(lines, start) {
        var headers = splitTableRow(lines[start]);
        var separators = splitTableRow(lines[start + 1]);
        var rows = [];
        var i = start + 2;
        while (i < lines.length && lines[i].indexOf('|') >= 0 && !/^\s*$/.test(lines[i])) {
            var cells = splitTableRow(lines[i]);
            rows.push(normalizeTableRow(cells, headers.length));
            i++;
        }

        var headHtml = headers.map(function (cell, index) {
            return '<th scope="col" class="md-table-cell' + tableAlignClass(separators[index]) + '">' + inlineMd(cell) + '</th>';
        }).join('');
        var bodyHtml = rows.map(function (row) {
            return '<tr>' + row.map(function (cell, index) {
                return '<td class="md-table-cell' + tableAlignClass(separators[index]) + '">' + inlineMd(cell) + '</td>';
            }).join('') + '</tr>';
        }).join('');

        return {
            html: '<div class="md-table-wrap"><table class="md-table"><thead><tr>' + headHtml
                + '</tr></thead><tbody>' + bodyHtml + '</tbody></table></div>',
            next: i
        };
    }

    // Блочный + инлайн разбор. Возвращает безопасную HTML-строку для innerHTML.
    function renderMarkdown(text) {
        if (!text) return '';
        var lines = String(text).replace(/\r\n?/g, '\n').split('\n');
        var out = [];
        var i = 0;
        while (i < lines.length) {
            var line = lines[i];

            // Блок кода ```…```
            if (/^```/.test(line)) {
                var buf = [];
                i++;
                while (i < lines.length && !/^```/.test(lines[i])) { buf.push(lines[i]); i++; }
                i++; // закрывающая ``` (или EOF)
                out.push('<pre class="md-pre"><code>' + escapeHtml(buf.join('\n')) + '</code></pre>');
                continue;
            }

            // Пустая строка
            if (/^\s*$/.test(line)) { i++; continue; }

            // GFM-таблица: строка заголовков + строка-разделитель (-, :---:, ---:).
            if (isTableStart(lines, i)) {
                var table = renderTable(lines, i);
                out.push(table.html);
                i = table.next;
                continue;
            }

            // HTML-абзац из README: поддерживается только безопасный align и
            // только при явном закрывающем </p>, чтобы не менять правила markdown.
            var htmlParagraph = line.match(/^\s*<p(?:\s+align\s*=\s*(?:"(left|center|right)"|'(left|center|right)'|(left|center|right)))?\s*>\s*$/i);
            if (htmlParagraph) {
                var paragraphLines = [];
                var next = i + 1;
                while (next < lines.length && !/^\s*<\/p>\s*$/i.test(lines[next])) {
                    paragraphLines.push(lines[next]);
                    next++;
                }
                if (next < lines.length) {
                    var alignment = htmlAlignment(htmlParagraph[1] || htmlParagraph[2] || htmlParagraph[3]);
                    out.push('<p class="md-p' + (alignment ? ' md-html-align-' + alignment : '') + '">'
                        + paragraphLines.map(inlineMd).join('<br>') + '</p>');
                    i = next + 1;
                    continue;
                }
            }

            // HTML-заголовки из README. Уровни h1…h6 соответствуют markdown-заголовкам.
            var htmlHeading = line.match(/^\s*<h([1-6])(?:\s+align\s*=\s*(?:"(left|center|right)"|'(left|center|right)'|(left|center|right)))?\s*>([\s\S]*?)<\/h\1>\s*$/i);
            if (htmlHeading) {
                var htmlLevel = htmlHeading[1];
                var headingAlignment = htmlAlignment(htmlHeading[2] || htmlHeading[3] || htmlHeading[4]);
                out.push('<h' + htmlLevel + ' class="md-h' + (headingAlignment ? ' md-html-align-' + headingAlignment : '')
                    + '">' + inlineMd(htmlHeading[5]) + '</h' + htmlLevel + '>');
                i++;
                continue;
            }

            // Заголовок # … ######
            var h = line.match(/^(#{1,6})\s+(.*)$/);
            if (h) {
                var lvl = h[1].length;
                out.push('<h' + lvl + ' class="md-h">' + inlineMd(h[2]) + '</h' + lvl + '>');
                i++;
                continue;
            }

            // Цитата (подряд идущие > …)
            if (/^>\s?/.test(line)) {
                var qbuf = [];
                while (i < lines.length && /^>\s?/.test(lines[i])) {
                    qbuf.push(inlineMd(lines[i].replace(/^>\s?/, '')));
                    i++;
                }
                out.push('<blockquote class="md-quote">' + qbuf.join('<br>') + '</blockquote>');
                continue;
            }

            // Маркированный список (- * +)
            if (/^[-*+]\s+/.test(line)) {
                var ubuf = [];
                while (i < lines.length && /^[-*+]\s+/.test(lines[i])) {
                    ubuf.push('<li>' + inlineMd(lines[i].replace(/^[-*+]\s+/, '')) + '</li>');
                    i++;
                }
                out.push('<ul class="md-list">' + ubuf.join('') + '</ul>');
                continue;
            }

            // Нумерованный список (1. 2. …)
            if (/^\d+\.\s+/.test(line)) {
                var obuf = [];
                while (i < lines.length && /^\d+\.\s+/.test(lines[i])) {
                    obuf.push('<li>' + inlineMd(lines[i].replace(/^\d+\.\s+/, '')) + '</li>');
                    i++;
                }
                out.push('<ol class="md-list">' + obuf.join('') + '</ol>');
                continue;
            }

            // Абзац: строки подряд до пустой/начала блока, мягкий перенос → <br>
            var pbuf = [];
            while (i < lines.length && !/^\s*$/.test(lines[i])
                && !/^```/.test(lines[i])
                && !/^#{1,6}\s+/.test(lines[i])
                && !/^>\s?/.test(lines[i])
                && !/^[-*+]\s+/.test(lines[i])
                && !/^\d+\.\s+/.test(lines[i])
                && !isTableStart(lines, i)) {
                pbuf.push(inlineMd(lines[i]));
                i++;
            }
            out.push('<p class="md-p">' + pbuf.join('<br>') + '</p>');
        }
        return out.join('');
    }

    function formatDuration(sec) {
        if (!sec || isNaN(sec)) return '0:00';
        var m = Math.floor(sec / 60);
        var s = Math.floor(sec % 60);
        return m + ':' + s.toString().padStart(2, '0');
    }

    function attachmentEmoji(type) {
        switch (type) {
            case 'IMAGE': case 'GIF': return '\u{1F4F7} ' + BF.i18n.t('attachment.photo');
            case 'VIDEO': return '\u{1F3AC} ' + BF.i18n.t('attachment.video');
            case 'AUDIO': return '\u{1F3B5} ' + BF.i18n.t('attachment.audio');
            case 'VOICE': return '\u{1F3A4} ' + BF.i18n.t('attachment.voice');
            case 'DOCUMENT': return '\u{1F4C4} ' + BF.i18n.t('attachment.document');
            case 'STICKER': return '\u{1F92A} ' + BF.i18n.t('attachment.sticker');
            default: return '\u{1F4CE} ' + BF.i18n.t('attachment.generic');
        }
    }

    // Общие SVG-иконки для превью списка чатов (24×24, currentColor).
    var PREVIEW_ICONS = {
        image: BF.icons.html('chat', 'image', 'preview-icon'),
        video: BF.icons.html('chat', 'video', 'preview-icon'),
        gif: BF.icons.html('chat', 'gif', 'preview-icon'),
        audio: BF.icons.html('chat', 'audio', 'preview-icon'),
        voice: BF.icons.html('chat', 'voice', 'preview-icon'),
        document: BF.icons.html('chat', 'document', 'preview-icon'),
        sticker: BF.icons.html('chat', 'sticker', 'preview-icon'),
        forwarded: BF.icons.html('chat', 'forwarded-message', 'preview-icon'),
        unknown: BF.icons.html('chat', 'unknown-attachment', 'preview-icon'),
        callIn: BF.icons.html('services', 'calls', 'preview-icon'),
        callOut: BF.icons.html('services', 'calls', 'preview-icon')
    };

    // HTML превью вложения: SVG-иконка + текстовая подпись.
    function attachmentPreviewHtml(type) {
        var icon, label;
        switch (type) {
            case 'IMAGE': icon = PREVIEW_ICONS.image; label = BF.i18n.t('attachment.photo'); break;
            case 'GIF': icon = PREVIEW_ICONS.gif; label = BF.i18n.t('attachment.photo'); break;
            case 'VIDEO': icon = PREVIEW_ICONS.video; label = BF.i18n.t('attachment.video'); break;
            case 'AUDIO': icon = PREVIEW_ICONS.audio; label = BF.i18n.t('attachment.audio'); break;
            case 'VOICE': icon = PREVIEW_ICONS.voice; label = BF.i18n.t('attachment.voice'); break;
            case 'DOCUMENT': icon = PREVIEW_ICONS.document; label = BF.i18n.t('attachment.document'); break;
            case 'STICKER': icon = PREVIEW_ICONS.sticker; label = BF.i18n.t('attachment.sticker'); break;
            case 'FORWARDED_MESSAGE': icon = PREVIEW_ICONS.forwarded; label = BF.i18n.t('attachment.generic'); break;
            default: icon = PREVIEW_ICONS.unknown; label = BF.i18n.t('attachment.generic');
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

    function docIcon() {
        return BF.icons.html('chat', 'document', 'document-icon');
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
        if (!lastSeenTs) return BF.i18n.t('lastSeen.offline');
        var now = Date.now();
        var diff = now - lastSeenTs;
        if (diff < 0) diff = 0;

        var seconds = Math.floor(diff / 1000);
        var minutes = Math.floor(seconds / 60);
        var hours = Math.floor(minutes / 60);
        var days = Math.floor(hours / 24);

        if (seconds < 60) return BF.i18n.t('lastSeen.justNow');
        if (minutes < 60) return BF.i18n.tp('lastSeen.minutes', minutes);
        if (hours < 24) return BF.i18n.tp('lastSeen.hours', hours);
        if (days === 1) return BF.i18n.t('lastSeen.yesterday');
        if (days < 7) return BF.i18n.tp('lastSeen.days', days);

        var d = new Date(lastSeenTs);
        return BF.i18n.t('lastSeen.date', {
            date: d.toLocaleDateString(locale(), { day: 'numeric', month: 'short' })
        });
    }

    // ===== Overlay focus management (a11y) =====
    // openOverlay/closeOverlay: focus-trap + inert фона + возврат фокуса на триггер.
    var overlayStack = [];
    var trapInstalled = false;
    var FOCUSABLE_SELECTOR = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), ' +
        'select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    function isFocusVisibleEl(el) {
        return !!(el.offsetWidth || el.offsetHeight) || el.getClientRects().length > 0;
    }

    function focusables(rootEl) {
        return Array.prototype.filter.call(rootEl.querySelectorAll(FOCUSABLE_SELECTOR), isFocusVisibleEl);
    }

    function trapKeydown(e) {
        if (e.key !== 'Tab') return;
        var top = overlayStack[overlayStack.length - 1];
        if (!top) return;
        var items = focusables(top.overlay);
        if (items.length === 0) { e.preventDefault(); return; }
        var first = items[0];
        var last = items[items.length - 1];
        var active = document.activeElement;
        if (e.shiftKey) {
            if (active === first || !top.overlay.contains(active)) { e.preventDefault(); last.focus(); }
        } else {
            if (active === last || !top.overlay.contains(active)) { e.preventDefault(); first.focus(); }
        }
    }

    function inertBackground(overlayEl) {
        var inerted = [];
        Array.prototype.forEach.call(document.body.children, function (child) {
            if (child === overlayEl || child.contains(overlayEl)) return;
            var tag = child.tagName;
            if (tag === 'SCRIPT' || tag === 'LINK' || tag === 'NOSCRIPT' || tag === 'STYLE') return;
            if (child.classList && child.classList.contains('blobs')) return;
            child.inert = true;
            inerted.push(child);
        });
        return inerted;
    }

    function openOverlay(overlayEl, opts) {
        opts = opts || {};
        for (var i = 0; i < overlayStack.length; i++) {
            if (overlayStack[i].overlay === overlayEl) return;
        }
        var entry = {
            overlay: overlayEl,
            inerted: inertBackground(overlayEl),
            prevFocus: document.activeElement,
            prevRole: overlayEl.getAttribute('role'),
            prevModal: overlayEl.getAttribute('aria-modal')
        };
        overlayStack.push(entry);
        overlayEl.setAttribute('role', opts.role || 'dialog');
        overlayEl.setAttribute('aria-modal', 'true');
        overlayEl.classList.add('visible');
        if (!trapInstalled) {
            document.addEventListener('keydown', trapKeydown, true);
            trapInstalled = true;
        }
        var target = opts.focus;
        if (!target) {
            var items = focusables(overlayEl);
            target = items[0];
        }
        if (target && typeof target.focus === 'function') {
            setTimeout(function () { target.focus(); }, 30);
        }
    }

    function closeOverlay(overlayEl) {
        overlayEl.classList.remove('visible');
        var idx = -1;
        for (var i = overlayStack.length - 1; i >= 0; i--) {
            if (overlayStack[i].overlay === overlayEl) { idx = i; break; }
        }
        if (idx === -1) return;
        var entry = overlayStack.splice(idx, 1)[0];
        entry.inerted.forEach(function (el) {
            var stillHeld = overlayStack.some(function (other) { return other.inerted.indexOf(el) >= 0; });
            if (!stillHeld) el.inert = false;
        });
        if (entry.prevRole === null) overlayEl.removeAttribute('role');
        else overlayEl.setAttribute('role', entry.prevRole);
        if (entry.prevModal === null) overlayEl.removeAttribute('aria-modal');
        else overlayEl.setAttribute('aria-modal', entry.prevModal);
        var prev = entry.prevFocus;
        if (prev && document.contains(prev) && typeof prev.focus === 'function') {
            setTimeout(function () { prev.focus(); }, 0);
        }
        if (overlayStack.length === 0 && trapInstalled) {
            document.removeEventListener('keydown', trapKeydown, true);
            trapInstalled = false;
        }
    }

    window.BF.utils = {
        formatTime: formatTime,
        formatChatListTime: formatChatListTime,
        formatDate: formatDate,
        formatFileSize: formatFileSize,
        truncate: truncate,
        escapeHtml: escapeHtml,
        renderMarkdown: renderMarkdown,
        formatDuration: formatDuration,
        attachmentEmoji: attachmentEmoji,
        attachmentPreviewHtml: attachmentPreviewHtml,
        callPreviewHtml: callPreviewHtml,
        docIcon: docIcon,
        parseJwtPayload: parseJwtPayload,
        isStatusOnline: isStatusOnline,
        formatLastSeen: formatLastSeen,
        openOverlay: openOverlay,
        closeOverlay: closeOverlay
    };
})();
