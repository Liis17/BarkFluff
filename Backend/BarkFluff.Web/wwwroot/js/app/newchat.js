/**
 * Создание чатов: ЛС (через GetPersonChatId), группа (CreateGroupChat),
 * приватный E2E-чат (CreatePrivateChat: salt → Argon2id → verifier, как на Android).
 * UI: кнопка у дока → меню (список под ним размывается) → диалог с переключателем режима,
 * «Недавними» собеседниками из личных чатов и выбором результата стрелками.
 * Requires: BF.api, BF.privateChat, BF.utils
 * Exposes: BF.newchat
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var $ = function (sel) { return document.querySelector(sel); };
    var u = function () { return BF.utils; };

    var fab = $('#newChatFab');
    var menu = $('#newChatMenu');
    var scrim = $('#newChatScrim');
    var overlay = $('#newChatOverlay');
    var modesEl = $('#newChatModes');
    var closeBtn = $('#newChatClose');
    var groupTitleInput = $('#newChatGroupTitle');
    var chipsEl = $('#newChatChips');
    var searchInput = $('#newChatSearch');
    var resultsEl = $('#newChatResults');
    var privateFields = $('#newChatPrivateFields');
    var passInput = $('#newChatPass');
    var rememberInput = $('#newChatRemember');
    var errorEl = $('#newChatError');
    var footerEl = $('#newChatFooter');
    var createBtn = $('#newChatCreate');
    var cancelBtn = $('#newChatCancel');

    var opts = null;          // { openChat, upsertChat, getMyUserId, getChats, getCachedUser, showToast }
    var mode = null;          // 'message' | 'group' | 'private'
    var selected = new Map(); // userId -> user (group: много, private: один)
    var searchTimer = null;
    var lastResults = [];
    var activeIndex = -1;     // результат, выбранный стрелками
    var busy = false;
    var RECENT_LIMIT = 5;

    function setMenuOpen(open) {
        menu.classList.toggle('visible', open);
        if (scrim) scrim.classList.toggle('visible', open);
        fab.classList.toggle('open', open);
        fab.setAttribute('aria-expanded', open ? 'true' : 'false');
    }
    function openMenu() { setMenuOpen(!menu.classList.contains('visible')); }
    function closeMenu() { setMenuOpen(false); }

    // Переключение режима в открытом диалоге сохраняет запрос; выбранные участники сбрасываются.
    function setMode(newMode) {
        mode = newMode;
        selected = new Map();
        Array.prototype.forEach.call(modesEl.querySelectorAll('[data-mode]'), function (btn) {
            var active = btn.dataset.mode === mode;
            btn.classList.toggle('active', active);
            btn.setAttribute('aria-selected', active ? 'true' : 'false');
        });
        groupTitleInput.style.display = mode === 'group' ? '' : 'none';
        chipsEl.style.display = mode === 'group' ? '' : 'none';
        privateFields.style.display = mode === 'private' ? '' : 'none';
        footerEl.style.display = mode === 'message' ? 'none' : '';
        errorEl.textContent = '';
        renderChips();
        doSearch();
    }

    function openOverlay(newMode) {
        busy = false;
        groupTitleInput.value = '';
        passInput.value = '';
        rememberInput.checked = true;
        searchInput.value = '';
        createBtn.disabled = false;
        createBtn.textContent = BF.i18n.t('common.create');
        setMode(newMode);
        BF.utils.openOverlay(overlay, { focus: mode === 'group' ? groupTitleInput : searchInput });
    }

    function closeOverlay() {
        BF.utils.closeOverlay(overlay);
        mode = null;
    }

    function renderChips() {
        chipsEl.innerHTML = '';
        selected.forEach(function (user) {
            var chip = document.createElement('span');
            chip.className = 'newchat-chip';
            chip.textContent = displayName(user);
            var x = document.createElement('button');
            x.className = 'newchat-chip-remove';
            x.innerHTML = BF.icons.html('close');
            x.addEventListener('click', function () {
                selected.delete(user.id);
                renderChips();
                renderResults(lastResults);
            });
            chip.appendChild(x);
            chipsEl.appendChild(chip);
        });
    }

    function displayName(user) {
        if (u().isRestrictedUser(user)) return u().restrictedUserName(user);
        return ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || ('@' + (user.username || ''));
    }

    function isAllowed(user) {
        if (!user || user.id === opts.getMyUserId()) return false;
        return !(mode === 'private' && user.isBot); // бот не расшифрует E2E
    }

    // Собеседники последних личных чатов из загруженного списка — подсказка до ввода запроса.
    function recentUsers() {
        var myId = opts.getMyUserId();
        var seen = new Set();
        var users = [];
        (opts.getChats ? opts.getChats() : []).some(function (chat) {
            if (chat.isGroupChat || (chat.chatType || 0) !== 0 || !chat.members) return false;
            var member = chat.members.find(function (m) { return m.userId !== myId; });
            var user = member && opts.getCachedUser(member.userId);
            if (user && !seen.has(user.id) && isAllowed(user) && !u().isRestrictedUser(user)) {
                seen.add(user.id);
                users.push(user);
            }
            return users.length >= RECENT_LIMIT;
        });
        return users;
    }

    function setActive(index) {
        var items = resultsEl.querySelectorAll('.search-result-item');
        activeIndex = items.length ? Math.max(0, Math.min(items.length - 1, index)) : -1;
        Array.prototype.forEach.call(items, function (el, i) {
            el.classList.toggle('kb-active', i === activeIndex);
            el.setAttribute('aria-selected', i === activeIndex ? 'true' : 'false');
            if (i === activeIndex && el.scrollIntoView) el.scrollIntoView({ block: 'nearest' });
        });
    }

    function onSearchKeydown(e) {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            setActive(activeIndex + (e.key === 'ArrowDown' ? 1 : -1));
        } else if (e.key === 'Enter' && lastResults.length && !e.isComposing) {
            e.preventDefault();
            onUserClick(lastResults[activeIndex >= 0 ? activeIndex : 0]);
        }
    }

    function renderResults(users, sectionKey) {
        lastResults = users;
        activeIndex = -1;
        resultsEl.innerHTML = '';
        if (users.length === 0) {
            if (!sectionKey) resultsEl.innerHTML = '<div class="newchat-empty">' + BF.utils.escapeHtml(BF.i18n.t('common.nothingFound')) + '</div>';
            return;
        }
        if (sectionKey) {
            var header = document.createElement('div');
            header.className = 'newchat-section';
            header.textContent = BF.i18n.t(sectionKey);
            resultsEl.appendChild(header);
        }
        users.forEach(function (user) {
            var el = document.createElement('div');
            el.className = 'search-result-item' + (selected.has(user.id) ? ' selected' : '');
            el.setAttribute('role', 'option');
            var restricted = u().isRestrictedUser(user);
            var name = displayName(user);
            var initial = (name || '?')[0].toUpperCase();
            var avHtml = !restricted && user.profilePicturePreview
                ? '<img src="' + u().escapeHtml(user.profilePicturePreview) + '" alt="">'
                : u().escapeHtml(initial);
            el.innerHTML = '<div class="chat-avatar">' + avHtml + '</div>' +
                '<div class="search-result-info"><div class="user-name">' + u().escapeHtml(displayName(user)) + '</div>' +
                (restricted ? '' : '<div class="user-username">@' + u().escapeHtml(user.username || '') + '</div>') + '</div>';
            el.addEventListener('click', function () { onUserClick(user); });
            resultsEl.appendChild(el);
        });
    }

    function onUserClick(user) {
        errorEl.textContent = '';
        if (mode === 'message') {
            closeOverlay();
            BF.api.getPersonChatId(user.id).then(function (d) {
                if (d && d.chatId) {
                    opts.openChat(d.chatId);
                    if (window.__mobileShowChat) window.__mobileShowChat();
                }
            }).catch(function (e) {
                if (e && e.errorCode === '6BC0E7A1-5117-4910-9717-1784E6921F6F') {
                    opts.showToast(BF.i18n.t('accountRestriction.userUnavailable'), true);
                } else console.error('[newchat] getPersonChatId failed', e);
            });
            return;
        }
        if (mode === 'private') {
            // Приватный чат — 1-к-1: выбор одного собеседника
            selected = selected.has(user.id) ? new Map() : new Map([[user.id, user]]);
        } else if (selected.has(user.id)) {
            selected.delete(user.id);
        } else {
            selected.set(user.id, user);
        }
        renderChips();
        var active = activeIndex;
        renderResults(lastResults, searchInput.value.trim() ? null : 'newchat.recent');
        if (active >= 0) setActive(active);
    }

    var searchToken = 0;

    function doSearch() {
        clearTimeout(searchTimer);
        var query = searchInput.value.trim();
        var token = ++searchToken;
        if (!query) { renderResults(recentUsers(), 'newchat.recent'); return; }
        BF.api.searchUsers(query, 0, 20).then(function (data) {
            if (token !== searchToken || !data || !data.users) return;
            renderResults(data.users.filter(isAllowed));
        }).catch(function () {});
    }

    function create() {
        if (busy) return;
        if (mode === 'group') createGroup();
        else if (mode === 'private') createPrivate();
    }

    function setBusy(label) {
        busy = true;
        createBtn.disabled = true;
        createBtn.textContent = label;
    }

    function clearBusy() {
        busy = false;
        createBtn.disabled = false;
        createBtn.textContent = BF.i18n.t('common.create');
    }

    function createGroup() {
        var title = groupTitleInput.value.trim();
        if (!title) { errorEl.textContent = BF.i18n.t('newchat.error.noGroupTitle'); return; }
        if (selected.size === 0) { errorEl.textContent = BF.i18n.t('newchat.error.noMembers'); return; }
        setBusy(BF.i18n.t('newchat.creating'));
        BF.api.createGroupChat(Array.from(selected.keys()), title).then(function (resp) {
            closeOverlay();
            if (resp && resp.chat) opts.upsertChat(resp.chat);
        }).catch(function (e) {
            console.error('[newchat] createGroupChat failed', e);
            clearBusy();
            errorEl.textContent = BF.i18n.t('newchat.error.groupFailed');
        });
    }

    function createPrivate() {
        if (selected.size !== 1) { errorEl.textContent = BF.i18n.t('newchat.error.noPeer'); return; }
        var pass = passInput.value;
        if (pass.length < 6) { errorEl.textContent = BF.i18n.t('newchat.error.shortPassword'); return; }
        var peer = selected.values().next().value;
        var remember = rememberInput.checked;
        setBusy(BF.i18n.t('newchat.creating'));
        var salt = BF.privateChat.generateSalt();
        BF.privateChat.deriveKey(pass, salt).then(function (key) {
            return BF.privateChat.computeVerifier(key).then(function (verifier) {
                return BF.api.createPrivateChat(peer.id, salt, verifier).then(function (resp) {
                    if (!resp || !resp.chat) { throw new Error('empty_response'); }
                    // Ключ сохраняем только для нового чата: у существующего свой salt/пароль
                    if (resp.created) BF.privateChat.saveKey(resp.chat.id, key, remember);
                    closeOverlay();
                    opts.upsertChat(resp.chat);
                });
            });
        }).catch(function (e) {
            console.error('[newchat] createPrivateChat failed', e);
            clearBusy();
            errorEl.textContent = e && e.errorCode === '6BC0E7A1-5117-4910-9717-1784E6921F6F'
                ? BF.i18n.t('accountRestriction.userUnavailable')
                : BF.i18n.t('newchat.error.privateFailed');
        });
    }

    function init(o) {
        opts = o;
        if (!fab) return;

        fab.addEventListener('click', function (e) { e.stopPropagation(); openMenu(); });
        if (scrim) scrim.addEventListener('click', closeMenu);
        modesEl.addEventListener('click', function (e) {
            var btn = e.target.closest('button[data-mode]');
            if (!btn || btn.dataset.mode === mode || busy) return;
            setMode(btn.dataset.mode);
            (mode === 'group' ? groupTitleInput : searchInput).focus();
        });
        searchInput.addEventListener('keydown', onSearchKeydown);
        menu.addEventListener('click', function (e) {
            var btn = e.target.closest('button[data-mode]');
            if (!btn) return;
            closeMenu();
            openOverlay(btn.dataset.mode);
        });
        document.addEventListener('click', function (e) {
            if (menu.classList.contains('visible') && !menu.contains(e.target)) closeMenu();
        });
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            if (menu.classList.contains('visible')) closeMenu();
            else if (overlay.classList.contains('visible')) closeOverlay();
        });

        closeBtn.addEventListener('click', closeOverlay);
        cancelBtn.addEventListener('click', closeOverlay);
        overlay.addEventListener('click', function (e) { if (e.target === overlay) closeOverlay(); });
        createBtn.addEventListener('click', create);

        searchInput.addEventListener('input', function () {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(doSearch, 300);
        });
    }

    window.BF.newchat = { init: init, open: openOverlay };
})();
