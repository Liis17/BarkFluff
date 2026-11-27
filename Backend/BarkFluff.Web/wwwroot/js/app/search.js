/**
 * Sidebar search: an instant filter over the loaded chats plus debounced user (SearchUsers)
 * and message (SearchMessages) searches. Message search pages by cursor and has author,
 * period and attachment filters.
 * Requires: BF.api, BF.i18n, BF.utils, BF.userBadges
 * Exposes: BF.search
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var searchInput;
    var searchResults;
    var searchFilters;
    var filterChips;
    var filterPanel;
    var searchTimer = null;
    var searchToken = 0;
    // Серверный поиск сообщений: короче — слишком широкий запрос, длиннее — отказ сервера.
    var MESSAGE_QUERY_MIN = 2;
    var MESSAGE_QUERY_MAX = 256;
    var MESSAGE_PAGE_SIZE = 20;
    var ATTACHMENT_TYPES = [
        ['IMAGE', 'attachment.photo'],
        ['VIDEO', 'attachment.video'],
        ['GIF', 'search.attachments.gif'],
        ['DOCUMENT', 'attachment.document'],
        ['AUDIO', 'attachment.audio'],
        ['VOICE', 'attachment.voice'],
        ['STICKER', 'attachment.sticker']
    ];

    var filters = emptyFilters();
    var openPanel = null;
    // Совпадения последнего поиска: из них предлагаются авторы для фильтра «От кого».
    var lastHits = [];
    var authorTimer = null;
    var authorToken = 0;

    // author: { userId, userUuid, name }; fromDay/throughDay — 'YYYY-MM-DD' включительно;
    // presence: '' (любые), 'WITH', 'WITHOUT'; types — имена MessageAttachmentType.
    function emptyFilters() {
        return { author: null, fromDay: '', throughDay: '', presence: '', types: [] };
    }

    function filtersActive() {
        return !!(filters.author || filters.fromDay || filters.throughDay || filters.presence || filters.types.length);
    }

    function localDayStart(day, shiftDays) {
        var p = day.split('-');
        return new Date(Number(p[0]), Number(p[1]) - 1, Number(p[2]) + shiftDays).getTime();
    }

    // Параметры SearchMessages: дни берутся в локальной зоне, «по» включительно — сервер ждёт [from, before).
    function messageSearchOptions() {
        var o = { pageSize: MESSAGE_PAGE_SIZE };
        if (filters.author) {
            if (filters.author.userId) o.authorUserId = filters.author.userId;
            else o.authorUserUuid = filters.author.userUuid;
        }
        if (filters.fromDay) o.sentFrom = localDayStart(filters.fromDay, 0);
        if (filters.throughDay) o.sentBefore = localDayStart(filters.throughDay, 1);
        if (filters.presence) o.attachmentPresence = filters.presence;
        if (filters.types.length) o.attachmentTypes = filters.types.slice();
        return o;
    }

    function authorKey(author) {
        return author.userId ? 'id:' + author.userId : 'uuid:' + author.userUuid;
    }

    function formatDay(day) {
        return new Date(localDayStart(day, 0)).toLocaleDateString(BF.i18n.current(), {
            day: 'numeric',
            month: 'short',
            year: 'numeric'
        });
    }

    function filterButton(className, label, onClick) {
        var b = document.createElement('button');
        b.type = 'button';
        b.className = className;
        b.textContent = label;
        b.addEventListener('click', onClick);
        return b;
    }

    function chip(label, active, panel) {
        var b = filterButton('search-filter-chip' + (active ? ' active' : ''), label, function () {
            openPanel = openPanel === panel ? null : panel;
            renderFilters();
        });
        b.setAttribute('aria-expanded', String(openPanel === panel));
        return b;
    }

    function renderChips() {
        var t = BF.i18n.t;
        var period =
            filters.fromDay || filters.throughDay
                ? (filters.fromDay ? formatDay(filters.fromDay) : '…') +
                  ' – ' +
                  (filters.throughDay ? formatDay(filters.throughDay) : '…')
                : t('search.filter.period');
        var attachments = filters.types.length
            ? ATTACHMENT_TYPES.filter(function (type) {
                  return filters.types.indexOf(type[0]) >= 0;
              })
                  .map(function (type) {
                      return t(type[1]);
                  })
                  .join(', ')
            : filters.presence
              ? t(filters.presence === 'WITH' ? 'search.attachments.with' : 'search.attachments.without')
              : t('search.filter.attachments');
        filterChips.innerHTML = '';
        filterChips.appendChild(
            chip(
                filters.author ? filters.author.name || t('search.author.unknown') : t('search.filter.author'),
                !!filters.author,
                'author'
            )
        );
        filterChips.appendChild(chip(period, !!(filters.fromDay || filters.throughDay), 'period'));
        filterChips.appendChild(chip(attachments, !!(filters.presence || filters.types.length), 'attachments'));
        if (filtersActive()) {
            filterChips.appendChild(
                filterButton('search-filter-reset', t('search.filter.reset'), function () {
                    filters = emptyFilters();
                    openPanel = null;
                    renderFilters();
                    onInput();
                })
            );
        }
    }

    function renderFilters() {
        renderChips();
        filterPanel.innerHTML = '';
        authorToken++;
        clearTimeout(authorTimer);
        filterPanel.hidden = !openPanel;
        if (openPanel === 'author') renderAuthorPanel();
        else if (openPanel === 'period') renderPeriodPanel();
        else if (openPanel === 'attachments') renderAttachmentsPanel();
    }

    // Изменение периода и вложений не пересоздаёт панель — фокус и выбор в ней сохраняются.
    function applyFilters() {
        renderChips();
        onInput();
    }

    function pickAuthor(author) {
        filters.author = author;
        openPanel = null;
        renderFilters();
        onInput();
    }

    function authorOption(author, label) {
        var selected = filters.author ? !!author && authorKey(filters.author) === authorKey(author) : !author;
        var b = filterButton('search-filter-option' + (selected ? ' selected' : ''), label, function () {
            pickAuthor(author);
        });
        b.setAttribute('aria-pressed', String(selected));
        return b;
    }

    function renderAuthorPanel() {
        var t = BF.i18n.t;
        var u = BF.utils;
        filterPanel.appendChild(authorOption(null, t('search.author.any')));
        var seen = {};
        [{ userId: deps.getMyUserId(), userUuid: '', name: t('search.author.me') }]
            .concat(filters.author ? [filters.author] : [])
            .concat(
                lastHits.map(function (hit) {
                    return { userId: hit.authorUserId, userUuid: hit.authorUserUuid || '', name: hit.authorName };
                })
            )
            .forEach(function (author) {
                if (!author.userId && !author.userUuid) return;
                var key = authorKey(author);
                if (seen[key]) return;
                seen[key] = true;
                filterPanel.appendChild(authorOption(author, author.name || t('search.author.unknown')));
            });

        var input = document.createElement('input');
        input.type = 'search';
        input.className = 'search-filter-input';
        input.placeholder = t('search.author.find');
        input.setAttribute('aria-label', t('search.author.find'));
        var found = document.createElement('div');
        input.addEventListener('input', function () {
            clearTimeout(authorTimer);
            var q = input.value.trim();
            var token = ++authorToken;
            found.innerHTML = '';
            if (!q) return;
            authorTimer = setTimeout(function () {
                BF.api
                    .searchUsers(q, 0, 10)
                    .then(function (data) {
                        if (token !== authorToken) return;
                        found.innerHTML = '';
                        ((data && data.users) || []).forEach(function (user) {
                            if (u.isRestrictedUser(user)) return;
                            var name = ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || user.username;
                            found.appendChild(authorOption({ userId: user.id, userUuid: '', name: name }, name));
                        });
                    })
                    .catch(function () {
                        // Без подсказок остаются автор «Я» и авторы из найденного.
                    });
            }, 300);
        });
        filterPanel.appendChild(input);
        filterPanel.appendChild(found);
    }

    function dateField(labelKey, value, onChange) {
        var label = document.createElement('label');
        label.className = 'search-filter-date';
        var caption = document.createElement('span');
        caption.textContent = BF.i18n.t(labelKey);
        var input = document.createElement('input');
        input.type = 'date';
        input.value = value;
        input.addEventListener('change', onChange);
        label.appendChild(caption);
        label.appendChild(input);
        filterPanel.appendChild(label);
        return input;
    }

    function renderPeriodPanel() {
        var fromInput = dateField('search.period.from', filters.fromDay, onChange);
        var throughInput = dateField('search.period.through', filters.throughDay, onChange);
        function onChange() {
            var from = fromInput.value;
            var through = throughInput.value;
            // Перепутанные границы меняем местами: сервер отклоняет sent_from >= sent_before.
            if (from && through && from > through) {
                fromInput.value = through;
                throughInput.value = from;
            }
            filters.fromDay = fromInput.value;
            filters.throughDay = throughInput.value;
            applyFilters();
        }
    }

    function renderAttachmentsPanel() {
        var t = BF.i18n.t;
        var radios = [];
        var checks = [];
        function sync() {
            radios.forEach(function (radio) {
                radio.checked = radio.value === filters.presence;
            });
            checks.forEach(function (check) {
                check.checked = filters.types.indexOf(check.value) >= 0;
                // Без вложений типы не выбираются: сервер отклоняет такое сочетание.
                check.disabled = filters.presence === 'WITHOUT';
            });
        }
        function option(type, value, labelText, onChange) {
            var label = document.createElement('label');
            label.className = 'search-filter-check';
            var input = document.createElement('input');
            input.type = type;
            if (type === 'radio') input.name = 'searchAttachmentPresence';
            input.value = value;
            input.addEventListener('change', function () {
                onChange(input);
                sync();
                applyFilters();
            });
            var caption = document.createElement('span');
            caption.textContent = labelText;
            label.appendChild(input);
            label.appendChild(caption);
            filterPanel.appendChild(label);
            return input;
        }
        [
            ['', 'search.attachments.any'],
            ['WITH', 'search.attachments.with'],
            ['WITHOUT', 'search.attachments.without']
        ].forEach(function (p) {
            radios.push(
                option('radio', p[0], t(p[1]), function (input) {
                    filters.presence = input.value;
                    // Типы уточняют только «С вложениями», как в Android.
                    if (input.value !== 'WITH') filters.types = [];
                })
            );
        });
        ATTACHMENT_TYPES.forEach(function (type) {
            checks.push(
                option('checkbox', type[0], t(type[1]), function (input) {
                    filters.types = filters.types.filter(function (name) {
                        return name !== input.value;
                    });
                    if (input.checked) {
                        filters.types.push(input.value);
                        filters.presence = 'WITH';
                    }
                })
            );
        });
        sync();
    }

    function hideFilters() {
        clearTimeout(authorTimer);
        authorToken++;
        searchFilters.hidden = true;
    }

    function clearSearch() {
        // Запоздалые ответы (в том числе «Показать ещё») не должны снова открыть закрытый поиск.
        clearTimeout(searchTimer);
        searchToken++;
        searchInput.value = '';
        searchResults.classList.remove('visible');
        searchResults.innerHTML = '';
        filters = emptyFilters();
        openPanel = null;
        lastHits = [];
        renderFilters();
        hideFilters();
    }

    // Экранированный текст с совпадениями запроса в <mark>. Если смена регистра меняет длину строки,
    // позиции совпадений не сходятся с исходным текстом — тогда без подсветки.
    function highlight(text, query) {
        var u = BF.utils;
        var source = String(text || '');
        var lower = source.toLowerCase();
        var needle = String(query || '').toLowerCase();
        if (!needle || lower.length !== source.length) return u.escapeHtml(source);
        var html = '';
        var from = 0;
        var at;
        while ((at = lower.indexOf(needle, from)) >= 0) {
            html +=
                u.escapeHtml(source.slice(from, at)) +
                '<mark>' +
                u.escapeHtml(source.slice(at, at + needle.length)) +
                '</mark>';
            from = at + needle.length;
        }
        return html + u.escapeHtml(source.slice(from));
    }

    function sectionTitle(key) {
        var header = document.createElement('div');
        header.className = 'search-section-title';
        header.textContent = BF.i18n.t(key);
        return header;
    }

    function isSavedChat(chat) {
        return !!(BF.chatList && BF.chatList.isSavedMessages(chat, deps.getMyUserId()));
    }

    function chatTitle(chat) {
        return isSavedChat(chat) ? BF.i18n.t('savedMessages.title') : chat.title || '';
    }

    function onInput() {
        var u = BF.utils;
        clearTimeout(searchTimer);
        var query = searchInput.value.trim();
        var qLower = query.toLowerCase();
        var withFilters = filtersActive();
        // Фильтры видны, когда запрос годится для поиска сообщений, и пока хоть один из них выбран.
        if (query.length >= MESSAGE_QUERY_MIN || withFilters) searchFilters.hidden = false;
        else hideFilters();
        if (!query && !withFilters) {
            searchToken++;
            searchResults.classList.remove('visible');
            searchResults.innerHTML = '';
            return;
        }
        var searchesMessages = query.length <= MESSAGE_QUERY_MAX && (query.length >= MESSAGE_QUERY_MIN || withFilters);
        // Снимок фильтров: «Показать ещё» продолжает именно этот запрос.
        var options = messageSearchOptions();

        // Локальный фильтр по уже загруженным чатам (синхронно, как в cmdpalette).
        var matchedChats = query
            ? deps.getChats().filter(function (c) {
                  return chatTitle(c).toLowerCase().indexOf(qLower) >= 0;
              })
            : [];

        // Без текста ищутся только сообщения по фильтрам.
        var foundUsers = query ? null : [];
        var foundHits = null;
        var messagesFailed = false;
        // Курсор следующей страницы и состояние «Показать ещё» для текущего запроса.
        var nextCursor = null;
        var loadingMore = false;
        var moreFailed = false;
        // Индекс первой строки, дописанной «Показать ещё»: только на одном рендере эти строки получают появление.
        var appendedFrom = -1;

        function renderHit(hit) {
            var el = document.createElement('div');
            el.className = 'search-result-item search-hit';
            var chat = deps.getChats().find(function (c) {
                return String(c.id) === String(hit.chatId);
            });
            var peer =
                !hit.isGroupChat && hit.otherUserId && deps.getCachedUser ? deps.getCachedUser(hit.otherUserId) : null;
            var restricted = u.isRestrictedUser(peer);
            var saved = !!chat && isSavedChat(chat);
            var title = saved
                ? BF.i18n.t('savedMessages.title')
                : restricted
                  ? u.restrictedUserName(peer)
                  : hit.chatTitle || (chat && chat.title) || BF.i18n.t('common.chat');
            var picture = !restricted && !saved && chat && chat.picture ? chat.picture : null;
            var avHtml = saved
                ? BF.icons.html('bookmark')
                : picture
                  ? '<img src="' + u.escapeHtml(picture) + '" alt="">'
                  : (title || '?')[0].toUpperCase();
            var body = hit.text
                ? highlight(hit.text, query)
                : hit.attachmentTypes.length
                  ? u.attachmentPreviewHtml(hit.attachmentTypes[0])
                  : '';
            var author = hit.isGroupChat && hit.authorName ? u.escapeHtml(hit.authorName) + ': ' : '';
            el.innerHTML =
                '<div class="chat-avatar' +
                (saved ? ' saved' : '') +
                '">' +
                avHtml +
                '</div>' +
                '<div class="search-result-info"><div class="user-name">' +
                u.escapeHtml(title) +
                (peer && !restricted ? BF.userBadges.slotHtml(hit.otherUserId) : '') +
                '<span class="search-result-time">' +
                u.escapeHtml(u.formatChatListTime(hit.sentAt)) +
                '</span></div><div class="search-result-text">' +
                author +
                body +
                '</div></div>';
            el.addEventListener('click', function () {
                clearSearch();
                deps.openChat(hit.chatId, hit.messageId);
            });
            return el;
        }

        // Перерисовка пересоздаёт список: прокрутку сохраняем, чтобы ответы и новые страницы не сбрасывали позицию.
        function render() {
            var scrollTop = searchResults.scrollTop;
            renderContent();
            searchResults.scrollTop = scrollTop;
        }

        function renderContent() {
            var users = foundUsers;
            var hits = foundHits || [];
            lastHits = hits;
            // С фильтрами ответ поиска сообщений виден всегда: пустой — «ничего», ошибка — отдельной строкой.
            var messagesNote = withFilters && hits.length === 0 && (foundHits !== null || messagesFailed);
            searchResults.classList.add('visible');
            searchResults.innerHTML = '';
            if (
                matchedChats.length === 0 &&
                (!users || users.length === 0) &&
                hits.length === 0 &&
                !(messagesNote && messagesFailed) &&
                // Пока идёт поиск сообщений, «ничего не найдено» преждевременно.
                !(searchesMessages && foundHits === null && !messagesFailed)
            ) {
                searchResults.innerHTML =
                    '<div style="padding:16px;text-align:center;color:var(--text-sub);font-size:14px;">' +
                    u.escapeHtml(BF.i18n.t('common.nothingFound')) +
                    '</div>';
                return;
            }
            if (matchedChats.length) searchResults.appendChild(sectionTitle('search.chats'));
            matchedChats.forEach(function (chat) {
                var el = document.createElement('div');
                el.className = 'search-result-item';
                var peer =
                    !chat.isGroupChat && chat.members && chat.members.length
                        ? deps.getCachedUser &&
                          deps.getCachedUser(
                              chat.members.find(function (member) {
                                  return member.userId !== deps.getMyUserId();
                              })?.userId
                          )
                        : null;
                var restricted = u.isRestrictedUser(peer);
                var saved = isSavedChat(chat);
                var title = restricted ? u.restrictedUserName(peer) : chatTitle(chat) || BF.i18n.t('common.chat');
                var initial = (title || '?')[0].toUpperCase();
                var avHtml = saved
                    ? BF.icons.html('bookmark')
                    : !restricted && chat.picture
                      ? '<img src="' + u.escapeHtml(chat.picture) + '" alt="">'
                      : u.escapeHtml(initial);
                el.innerHTML =
                    '<div class="chat-avatar' +
                    (saved ? ' saved' : '') +
                    '">' +
                    avHtml +
                    '</div>' +
                    '<div class="search-result-info"><div class="user-name">' +
                    (restricted ? u.escapeHtml(title) : highlight(title, query)) +
                    (peer && !restricted ? BF.userBadges.slotHtml(peer.id) : '') +
                    '</div></div>';
                el.addEventListener('click', function () {
                    clearSearch();
                    deps.openChat(chat.id);
                });
                searchResults.appendChild(el);
            });
            if (users && users.length) searchResults.appendChild(sectionTitle('search.people'));
            if (users) {
                users.forEach(function (user) {
                    var el = document.createElement('div');
                    el.className = 'search-result-item';
                    var restricted = u.isRestrictedUser(user);
                    var name = restricted
                        ? u.restrictedUserName(user)
                        : ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || user.username;
                    var initial = (name || '?')[0].toUpperCase();
                    var avHtml =
                        !restricted && user.profilePicturePreview
                            ? '<img src="' + u.escapeHtml(user.profilePicturePreview) + '" alt="">'
                            : initial;
                    el.innerHTML =
                        '<div class="chat-avatar">' +
                        avHtml +
                        '</div>' +
                        '<div class="search-result-info"><div class="user-name">' +
                        (restricted ? u.escapeHtml(name) : highlight(name, query)) +
                        (restricted ? '' : BF.userBadges.slotHtml(user.id)) +
                        '</div>' +
                        (restricted
                            ? ''
                            : '<div class="user-username">@' + highlight(user.username || '', query) + '</div>') +
                        '</div>';
                    el.addEventListener('click', function () {
                        clearSearch();
                        BF.api
                            .getPersonChatId(user.id)
                            .then(function (d) {
                                if (d && d.chatId) deps.openChat(d.chatId);
                            })
                            .catch(function (error) {
                                if (
                                    error &&
                                    error.errorCode === '6BC0E7A1-5117-4910-9717-1784E6921F6F' &&
                                    deps.showToast
                                ) {
                                    deps.showToast(BF.i18n.t('accountRestriction.userUnavailable'), true);
                                }
                            });
                    });
                    searchResults.appendChild(el);
                });
            }
            if (hits.length || messagesNote) {
                searchResults.appendChild(sectionTitle('search.messages'));
                if (messagesNote) {
                    var note = document.createElement('div');
                    note.className = 'search-section-note';
                    note.textContent = BF.i18n.t(messagesFailed ? 'search.messagesFailed' : 'common.nothingFound');
                    searchResults.appendChild(note);
                }
                hits.forEach(function (hit, i) {
                    var el = renderHit(hit);
                    if (appendedFrom >= 0 && i >= appendedFrom) el.classList.add('search-hit-enter');
                    searchResults.appendChild(el);
                });
                if (nextCursor) {
                    var more = document.createElement('button');
                    more.type = 'button';
                    more.className = 'search-more';
                    more.disabled = loadingMore;
                    more.textContent = BF.i18n.t(moreFailed ? 'search.retry' : 'search.loadMore');
                    more.addEventListener('click', loadMore);
                    searchResults.appendChild(more);
                }
            }
        }

        // Следующая страница того же запроса: новые совпадения дописываются в конец, дубли отбрасываются.
        function loadMore() {
            if (!nextCursor || loadingMore || token !== searchToken) return;
            loadingMore = true;
            moreFailed = false;
            render();
            BF.api
                .searchMessages(query, Object.assign({}, options, { cursor: nextCursor }))
                .then(function (data) {
                    if (token !== searchToken) return;
                    var known = {};
                    foundHits.forEach(function (hit) {
                        known[hit.messageId] = true;
                    });
                    var fromIndex = foundHits.length;
                    foundHits = foundHits.concat(
                        ((data && data.hits) || []).filter(function (hit) {
                            return !known[hit.messageId];
                        })
                    );
                    nextCursor = (data && data.nextCursor) || null;
                    loadingMore = false;
                    appendedFrom = fromIndex;
                    render();
                    appendedFrom = -1;
                })
                .catch(function () {
                    if (token !== searchToken) return;
                    loadingMore = false;
                    moreFailed = true;
                    render();
                });
        }

        // Новый запрос начинается с начала списка; дальше render() позицию сохраняет.
        searchResults.scrollTop = 0;
        render();

        var token = ++searchToken;
        searchTimer = setTimeout(function () {
            if (query) {
                BF.api.searchUsers(query, 0, 20).then(function (data) {
                    if (token !== searchToken) return;
                    foundUsers = data && data.users ? data.users : [];
                    render();
                });
            }
            if (!searchesMessages) return;
            BF.api
                .searchMessages(query, options)
                .then(function (data) {
                    if (token !== searchToken) return;
                    foundHits = data && data.hits ? data.hits : [];
                    nextCursor = (data && data.nextCursor) || null;
                    render();
                })
                .catch(function () {
                    // Поиск сообщений — дополнение к чатам и пользователям: ошибка не мешает им.
                    if (token !== searchToken) return;
                    messagesFailed = true;
                    render();
                });
        }, 300);
    }

    function init(options) {
        deps = options;
        searchInput = document.querySelector('#searchInput');
        searchResults = document.querySelector('#searchResults');
        searchFilters = document.querySelector('#searchFilters');
        filterChips = document.createElement('div');
        filterChips.className = 'search-filter-chips';
        filterPanel = document.createElement('div');
        filterPanel.className = 'search-filter-panel';
        searchFilters.appendChild(filterChips);
        searchFilters.appendChild(filterPanel);
        renderFilters();
        hideFilters();
        searchInput.addEventListener('input', onInput);
    }

    window.BF.search = { init: init };
})();
