/**
 * Создание чатов: ЛС (через GetPersonChatId), группа (CreateGroupChat),
 * приватный E2E-чат (CreatePrivateChat: salt → Argon2id → verifier, как на Android).
 * UI: FAB в сайдбаре → меню → оверлей с поиском пользователей.
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
    var overlay = $('#newChatOverlay');
    var titleEl = $('#newChatTitle');
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

    var opts = null;          // { openChat, upsertChat, getMyUserId }
    var mode = null;          // 'message' | 'group' | 'private'
    var selected = new Map(); // userId -> user (group: много, private: один)
    var searchTimer = null;
    var lastResults = [];
    var busy = false;

    var MODE_TITLE_KEYS = {
        message: 'newchat.mode.message',
        group: 'newchat.mode.group',
        private: 'newchat.mode.private'
    };

    function openMenu() { menu.classList.toggle('visible'); }
    function closeMenu() { menu.classList.remove('visible'); }

    function openOverlay(newMode) {
        mode = newMode;
        selected = new Map();
        busy = false;
        titleEl.textContent = BF.i18n.t(MODE_TITLE_KEYS[mode]);
        groupTitleInput.value = '';
        groupTitleInput.style.display = mode === 'group' ? '' : 'none';
        chipsEl.style.display = mode === 'group' ? '' : 'none';
        privateFields.style.display = mode === 'private' ? '' : 'none';
        footerEl.style.display = mode === 'message' ? 'none' : '';
        passInput.value = '';
        rememberInput.checked = true;
        searchInput.value = '';
        resultsEl.innerHTML = '';
        errorEl.textContent = '';
        createBtn.disabled = false;
        createBtn.textContent = BF.i18n.t('common.create');
        renderChips();
        overlay.classList.add('visible');
        setTimeout(function () {
            (mode === 'group' ? groupTitleInput : searchInput).focus();
        }, 50);
    }

    function closeOverlay() {
        overlay.classList.remove('visible');
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
            x.textContent = '×';
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
        return ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || ('@' + (user.username || ''));
    }

    function renderResults(users) {
        lastResults = users;
        resultsEl.innerHTML = '';
        if (users.length === 0) {
            resultsEl.innerHTML = '<div class="newchat-empty">' + BF.utils.escapeHtml(BF.i18n.t('common.nothingFound')) + '</div>';
            return;
        }
        users.forEach(function (user) {
            var el = document.createElement('div');
            el.className = 'search-result-item' + (selected.has(user.id) ? ' selected' : '');
            var initial = (user.firstName || user.username || '?')[0].toUpperCase();
            var avHtml = user.profilePicturePreview
                ? '<img src="' + u().escapeHtml(user.profilePicturePreview) + '" alt="">'
                : initial;
            el.innerHTML = '<div class="chat-avatar">' + avHtml + '</div>' +
                '<div class="search-result-info"><div class="user-name">' + u().escapeHtml(displayName(user)) + '</div>' +
                '<div class="user-username">@' + u().escapeHtml(user.username || '') + '</div></div>';
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
            }).catch(function (e) { console.error('[newchat] getPersonChatId failed', e); });
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
        renderResults(lastResults);
    }

    function doSearch() {
        var query = searchInput.value.trim();
        if (!query) { resultsEl.innerHTML = ''; lastResults = []; return; }
        BF.api.searchUsers(query, 0, 20).then(function (data) {
            if (!data || !data.users) return;
            var myId = opts.getMyUserId();
            var users = data.users.filter(function (usr) {
                if (usr.id === myId) return false;
                if (mode === 'private' && usr.isBot) return false; // бот не расшифрует E2E
                return true;
            });
            renderResults(users);
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
            errorEl.textContent = BF.i18n.t('newchat.error.privateFailed');
        });
    }

    function init(o) {
        opts = o;
        if (!fab) return;

        fab.addEventListener('click', function (e) { e.stopPropagation(); openMenu(); });
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

    window.BF.newchat = { init: init };
})();
