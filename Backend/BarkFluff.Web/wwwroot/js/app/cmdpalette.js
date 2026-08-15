/**
 * Command palette (Ctrl+K / Cmd+K): быстрые действия + переход к открытому чату по имени.
 * Requires: BF.i18n, BF.utils, BF.newchat, BF.settings, window.__setTheme
 * Exposes: BF.cmdPalette
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var overlay, input, resultsEl;
    var opts = null;       // { getChats, openChat }
    var items = [];        // текущий отфильтрованный список
    var activeIndex = 0;

    var BLOCKING_SELECTOR = '.confirm-overlay.visible, .settings-overlay.visible, ' +
        '.profile-overlay.visible, .call-permission-overlay.visible, .image-overlay.visible, ' +
        '.newchat-menu.visible, .msg-context-menu.visible';

    function actions() {
        var t = BF.i18n.t;
        return [
            { id: 'newchat.message', group: 'action', label: t('newchat.mode.message'), run: function () { BF.newchat.open('message'); } },
            { id: 'newchat.group', group: 'action', label: t('newchat.mode.group'), run: function () { BF.newchat.open('group'); } },
            { id: 'newchat.private', group: 'action', label: t('newchat.mode.private'), run: function () { BF.newchat.open('private'); } },
            { id: 'settings.profile', group: 'action', label: t('settings.profilePhoto'), run: function () { BF.settings.open('profile'); } },
            { id: 'settings.sessions', group: 'action', label: t('settings.sessions'), run: function () { BF.settings.open('sessions'); } },
            { id: 'settings.twofa', group: 'action', label: t('settings.twofa'), run: function () { BF.settings.open('twofa'); } },
            { id: 'settings.privacy', group: 'action', label: t('settings.privacy'), run: function () { BF.settings.open('privacy'); } },
            { id: 'settings.personalization', group: 'action', label: t('settings.personalization'), run: function () { BF.settings.open('personalization'); } },
            { id: 'settings.language', group: 'action', label: t('settings.language.item'), run: function () { BF.settings.open('language'); } },
            { id: 'settings.about', group: 'action', label: t('settings.about'), run: function () { BF.settings.open('about'); } },
            { id: 'theme.light', group: 'action', label: t('theme.light'), run: function () { window.__setTheme('light'); } },
            { id: 'theme.dark', group: 'action', label: t('theme.dark'), run: function () { window.__setTheme('dark'); } },
            { id: 'theme.midnight', group: 'action', label: t('theme.midnight'), run: function () { window.__setTheme('midnight'); } }
        ];
    }

    function matchingChats(query) {
        if (!query || !opts.getChats) return [];
        var chats = opts.getChats() || [];
        return chats.filter(function (c) {
            return (c.title || '').toLowerCase().indexOf(query) >= 0;
        }).slice(0, 6).map(function (c) {
            return { id: 'chat:' + c.id, group: 'chat', label: c.title || ('#' + c.id), run: function () { opts.openChat(c.id); } };
        });
    }

    function computeItems(rawQuery) {
        var query = (rawQuery || '').trim().toLowerCase();
        var matchedActions = !query ? actions() : actions().filter(function (a) {
            return a.label.toLowerCase().indexOf(query) >= 0;
        });
        return matchedActions.concat(matchingChats(query));
    }

    function render() {
        resultsEl.innerHTML = '';
        if (items.length === 0) {
            resultsEl.innerHTML = '<div class="newchat-empty">' + BF.utils.escapeHtml(BF.i18n.t('common.nothingFound')) + '</div>';
            return;
        }
        var lastGroup = null;
        items.forEach(function (item, index) {
            if (item.group !== lastGroup) {
                lastGroup = item.group;
                var caption = document.createElement('div');
                caption.className = 'cmdpalette-section';
                caption.textContent = BF.i18n.t(item.group === 'chat' ? 'cmdPalette.sectionChats' : 'cmdPalette.sectionActions');
                resultsEl.appendChild(caption);
            }
            var row = document.createElement('div');
            row.className = 'cmdpalette-item' + (index === activeIndex ? ' active' : '');
            row.textContent = item.label;
            row.addEventListener('mouseenter', function () { activeIndex = index; updateActive(); });
            row.addEventListener('click', function () { pick(item); });
            resultsEl.appendChild(row);
        });
    }

    function updateActive() {
        var rows = resultsEl.querySelectorAll('.cmdpalette-item');
        rows.forEach(function (row, i) { row.classList.toggle('active', i === activeIndex); });
        var activeRow = rows[activeIndex];
        if (activeRow) activeRow.scrollIntoView({ block: 'nearest' });
    }

    function pick(item) {
        closePalette();
        item.run();
    }

    function move(delta) {
        if (items.length === 0) return;
        activeIndex = Math.max(0, Math.min(items.length - 1, activeIndex + delta));
        updateActive();
    }

    function isBlocked() {
        return !!document.querySelector(BLOCKING_SELECTOR);
    }

    function isOpenPalette() {
        return overlay.classList.contains('visible');
    }

    function openPalette() {
        if (isOpenPalette() || isBlocked()) return;
        input.value = '';
        activeIndex = 0;
        items = computeItems('');
        render();
        BF.utils.openOverlay(overlay, { focus: input });
    }

    function closePalette() {
        BF.utils.closeOverlay(overlay);
    }

    function onKeydown(e) {
        var key = e.key ? e.key.toLowerCase() : '';
        if ((e.ctrlKey || e.metaKey) && !e.altKey && key === 'k') {
            e.preventDefault();
            if (isOpenPalette()) closePalette(); else openPalette();
            return;
        }
        if (!isOpenPalette()) return;
        if (e.key === 'Escape') { e.preventDefault(); closePalette(); }
        else if (e.key === 'ArrowDown') { e.preventDefault(); move(1); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); move(-1); }
        else if (e.key === 'Enter') { e.preventDefault(); if (items[activeIndex]) pick(items[activeIndex]); }
    }

    function init(o) {
        opts = o;
        overlay = document.querySelector('#cmdPaletteOverlay');
        input = document.querySelector('#cmdPaletteInput');
        resultsEl = document.querySelector('#cmdPaletteResults');
        if (!overlay || !input || !resultsEl) return;

        overlay.addEventListener('click', function (e) { if (e.target === overlay) closePalette(); });
        input.addEventListener('input', function () {
            activeIndex = 0;
            items = computeItems(input.value);
            render();
        });
        document.addEventListener('keydown', onKeydown);
    }

    window.BF.cmdPalette = { init: init };
})();
