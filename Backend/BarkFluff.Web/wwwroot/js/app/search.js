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
