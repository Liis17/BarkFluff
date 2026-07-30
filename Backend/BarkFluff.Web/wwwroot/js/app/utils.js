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
        renderMarkdown: renderMarkdown,
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
