/**
 * Sidebar search: an instant filter over the loaded chats plus debounced user (SearchUsers)
 * and message (SearchMessages) searches.
 * Requires: BF.api, BF.i18n, BF.utils
 * Exposes: BF.search
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var searchInput;
    var searchResults;
    var searchTimer = null;
    var searchToken = 0;
    // Серверный поиск сообщений: короче — слишком широкий запрос, длиннее — отказ сервера.
    var MESSAGE_QUERY_MIN = 2;
    var MESSAGE_QUERY_MAX = 256;
    var MESSAGE_PAGE_SIZE = 20;

    function clearSearch() {
        // Запоздалые ответы (в том числе «Показать ещё») не должны снова открыть закрытый поиск.
        clearTimeout(searchTimer);
        searchToken++;
        searchInput.value = '';
        searchResults.classList.remove('visible');
        searchResults.innerHTML = '';
    }

    function onInput() {
        var u = BF.utils;
        clearTimeout(searchTimer);
        var query = searchInput.value.trim();
        var qLower = query.toLowerCase();
        if (!query) {
            searchResults.classList.remove('visible');
            searchResults.innerHTML = '';
            return;
        }

        // Локальный фильтр по уже загруженным чатам (синхронно, как в cmdpalette).
        var matchedChats = deps.getChats().filter(function (c) {
            return (c.title || '').toLowerCase().indexOf(qLower) >= 0;
        });

        var foundUsers = null;
        var foundHits = null;
        // Курсор следующей страницы и состояние «Показать ещё» для текущего запроса.
        var nextCursor = null;
        var loadingMore = false;
        var moreFailed = false;

        function renderHit(hit) {
            var el = document.createElement('div');
            el.className = 'search-result-item search-hit';
            var chat = deps.getChats().find(function (c) {
                return String(c.id) === String(hit.chatId);
            });
            var peer =
                !hit.isGroupChat && hit.otherUserId && deps.getCachedUser ? deps.getCachedUser(hit.otherUserId) : null;
            var restricted = u.isRestrictedUser(peer);
            var title = restricted
                ? u.restrictedUserName(peer)
                : hit.chatTitle || (chat && chat.title) || BF.i18n.t('common.chat');
            var picture = !restricted && chat && chat.picture ? chat.picture : null;
            var avHtml = picture ? '<img src="' + u.escapeHtml(picture) + '" alt="">' : (title || '?')[0].toUpperCase();
            var body = hit.text
                ? u.escapeHtml(hit.text)
                : hit.attachmentTypes.length
                  ? u.attachmentPreviewHtml(hit.attachmentTypes[0])
                  : '';
            var author = hit.isGroupChat && hit.authorName ? u.escapeHtml(hit.authorName) + ': ' : '';
            el.innerHTML =
                '<div class="chat-avatar">' +
                avHtml +
                '</div>' +
                '<div class="search-result-info"><div class="user-name">' +
                u.escapeHtml(title) +
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
            searchResults.classList.add('visible');
            searchResults.innerHTML = '';
            if (matchedChats.length === 0 && (!users || users.length === 0) && hits.length === 0) {
                searchResults.innerHTML =
                    '<div style="padding:16px;text-align:center;color:var(--text-sub);font-size:14px;">' +
                    u.escapeHtml(BF.i18n.t('common.nothingFound')) +
                    '</div>';
                return;
            }
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
                var title = restricted ? u.restrictedUserName(peer) : chat.title || BF.i18n.t('common.chat');
                var initial = (title || '?')[0].toUpperCase();
                var avHtml =
                    !restricted && chat.picture ? '<img src="' + u.escapeHtml(chat.picture) + '" alt="">' : initial;
                el.innerHTML =
                    '<div class="chat-avatar">' +
                    avHtml +
                    '</div>' +
                    '<div class="search-result-info"><div class="user-name">' +
                    u.escapeHtml(title) +
                    '</div></div>';
                el.addEventListener('click', function () {
                    clearSearch();
                    deps.openChat(chat.id);
                });
                searchResults.appendChild(el);
            });
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
                        u.escapeHtml(name) +
                        '</div>' +
                        (restricted
                            ? ''
                            : '<div class="user-username">@' + u.escapeHtml(user.username || '') + '</div>') +
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
            if (hits.length) {
                var header = document.createElement('div');
                header.className = 'search-section-title';
                header.textContent = BF.i18n.t('search.messages');
                searchResults.appendChild(header);
                hits.forEach(function (hit) {
                    searchResults.appendChild(renderHit(hit));
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
                .searchMessages(query, { pageSize: MESSAGE_PAGE_SIZE, cursor: nextCursor })
                .then(function (data) {
                    if (token !== searchToken) return;
                    var known = {};
                    foundHits.forEach(function (hit) {
                        known[hit.messageId] = true;
                    });
                    foundHits = foundHits.concat(
                        ((data && data.hits) || []).filter(function (hit) {
                            return !known[hit.messageId];
                        })
                    );
                    nextCursor = (data && data.nextCursor) || null;
                    loadingMore = false;
                    render();
                })
                .catch(function () {
                    if (token !== searchToken) return;
                    loadingMore = false;
                    moreFailed = true;
                    render();
                });
        }

        render();

        var token = ++searchToken;
        searchTimer = setTimeout(function () {
            BF.api.searchUsers(query, 0, 20).then(function (data) {
                if (token !== searchToken) return;
                foundUsers = data && data.users ? data.users : [];
                render();
            });
            if (query.length < MESSAGE_QUERY_MIN || query.length > MESSAGE_QUERY_MAX) return;
            BF.api
                .searchMessages(query, { pageSize: MESSAGE_PAGE_SIZE })
                .then(function (data) {
                    if (token !== searchToken) return;
                    foundHits = data && data.hits ? data.hits : [];
                    nextCursor = (data && data.nextCursor) || null;
                    render();
                })
                .catch(function () {
                    // Поиск сообщений — дополнение к чатам и пользователям: ошибка не мешает им.
                });
        }, 300);
    }

    function init(options) {
        deps = options;
        searchInput = document.querySelector('#searchInput');
        searchResults = document.querySelector('#searchResults');
        searchInput.addEventListener('input', onInput);
    }

    window.BF.search = { init: init };
})();
