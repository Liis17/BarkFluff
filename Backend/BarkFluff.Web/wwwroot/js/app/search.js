/**
 * Sidebar search: an instant filter over the loaded chats plus a debounced user search (SearchUsers).
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

    function clearSearch() {
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

        function render(users) {
            searchResults.classList.add('visible');
            searchResults.innerHTML = '';
            if (matchedChats.length === 0 && (!users || users.length === 0)) {
                searchResults.innerHTML =
                    '<div style="padding:16px;text-align:center;color:var(--text-sub);font-size:14px;">' +
                    u.escapeHtml(BF.i18n.t('common.nothingFound')) +
                    '</div>';
                return;
            }
            matchedChats.forEach(function (chat) {
                var el = document.createElement('div');
                el.className = 'search-result-item';
                var initial = (chat.title || '?')[0].toUpperCase();
                var avHtml = chat.picture ? '<img src="' + u.escapeHtml(chat.picture) + '" alt="">' : initial;
                el.innerHTML =
                    '<div class="chat-avatar">' +
                    avHtml +
                    '</div>' +
                    '<div class="search-result-info"><div class="user-name">' +
                    u.escapeHtml(chat.title || BF.i18n.t('common.chat')) +
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
                    var initial = (user.firstName || user.username || '?')[0].toUpperCase();
                    var avHtml = user.profilePicturePreview
                        ? '<img src="' + u.escapeHtml(user.profilePicturePreview) + '" alt="">'
                        : initial;
                    el.innerHTML =
                        '<div class="chat-avatar">' +
                        avHtml +
                        '</div>' +
                        '<div class="search-result-info"><div class="user-name">' +
                        u.escapeHtml(((user.firstName || '') + ' ' + (user.lastName || '')).trim() || user.username) +
                        '</div>' +
                        '<div class="user-username">@' +
                        u.escapeHtml(user.username || '') +
                        '</div></div>';
                    el.addEventListener('click', function () {
                        clearSearch();
                        BF.api.getPersonChatId(user.id).then(function (d) {
                            if (d && d.chatId) deps.openChat(d.chatId);
                        });
                    });
                    searchResults.appendChild(el);
                });
            }
        }

        render(null);

        var token = ++searchToken;
        searchTimer = setTimeout(function () {
            BF.api.searchUsers(query, 0, 20).then(function (data) {
                if (token !== searchToken) return;
                render(data && data.users ? data.users : []);
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
