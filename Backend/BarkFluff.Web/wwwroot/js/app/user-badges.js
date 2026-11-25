/**
 * Бейджи пользователей: кеш `Users.GetUserBadges` на время сессии, миниатюра основного бейджа
 * рядом с именем и окно просмотра бейджа.
 * Рядом с именем ставится слот `.user-badge-slot[data-badge-user]`; после загрузки бейджей
 * все слоты этого пользователя в документе заполняются, поэтому строки списка, собранные
 * строкой HTML до ответа сервера, тоже получают значок.
 * Requires: BF.api, BF.i18n, BF.utils
 * Exposes: BF.userBadges
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    // Сбой (например, старая нода без RPC) не повторяется при каждой перерисовке списка.
    var RETRY_AFTER_FAILURE_MS = 60 * 1000;

    var cache = new Map(); // userId -> { badges: [] | null, request: Promise | null, failedAt: number }
    var viewer = null;

    function entryFor(userId) {
        var entry = cache.get(userId);
        if (!entry) {
            entry = { badges: null, request: null, failedAt: 0 };
            cache.set(userId, entry);
        }
        return entry;
    }

    function load(userId, force) {
        if (!userId) return Promise.resolve([]);
        var entry = entryFor(userId);
        if (entry.request) return entry.request;
        if (!force) {
            if (entry.badges) return Promise.resolve(entry.badges);
            if (entry.failedAt && Date.now() - entry.failedAt < RETRY_AFTER_FAILURE_MS) return Promise.resolve([]);
        }
        entry.request = BF.api
            .getUserBadges(userId)
            .then(function (data) {
                entry.request = null;
                entry.failedAt = 0;
                set(userId, (data && data.badges) || []);
                return entry.badges;
            })
            .catch(function () {
                entry.request = null;
                entry.failedAt = Date.now();
                return entry.badges || [];
            });
        return entry.request;
    }

    function primaryOf(badges) {
        if (!badges) return null;
        for (var i = 0; i < badges.length; i++) {
            if (badges[i].isPrimary) return badges[i];
        }
        return null;
    }

    // Бейджи из кеша без запроса; null — ещё не загружались.
    function cached(userId) {
        var entry = cache.get(userId);
        return entry ? entry.badges : null;
    }

    function primary(userId) {
        var entry = cache.get(userId);
        return entry ? primaryOf(entry.badges) : null;
    }

    function miniImage(badge) {
        var img = document.createElement('img');
        img.className = 'user-badge-mini';
        img.src = badge.miniImageUrl || badge.imageUrl;
        img.alt = badge.name || '';
        img.title = badge.name || '';
        img.decoding = 'async';
        return img;
    }

    function renderSlot(slot) {
        var badge = primary(Number(slot.dataset.badgeUser));
        var current = slot.firstElementChild;
        if (!badge || !(badge.miniImageUrl || badge.imageUrl)) {
            if (current) slot.replaceChildren();
            return;
        }
        if (current && current.dataset.badgeId === String(badge.id)) return;
        var img = miniImage(badge);
        img.dataset.badgeId = String(badge.id);
        slot.replaceChildren(img);
    }

    function refreshSlots(userId) {
        document.querySelectorAll('.user-badge-slot[data-badge-user="' + Number(userId) + '"]').forEach(renderSlot);
    }

    function set(userId, badges) {
        var entry = entryFor(userId);
        entry.badges = badges || [];
        refreshSlots(userId);
    }

    // Строка HTML для шаблонов: заполняется из кеша сразу или после загрузки.
    function slotHtml(userId) {
        if (!userId) return '';
        var id = Number(userId);
        var badge = primary(id);
        var inner = '';
        if (badge && (badge.miniImageUrl || badge.imageUrl)) {
            var name = BF.utils.escapeHtml(badge.name || '');
            inner =
                '<img class="user-badge-mini" data-badge-id="' +
                badge.id +
                '" src="' +
                BF.utils.escapeHtml(badge.miniImageUrl || badge.imageUrl) +
                '" alt="' +
                name +
                '" title="' +
                name +
                '" decoding="async">';
        }
        load(id);
        return '<span class="user-badge-slot" data-badge-user="' + id + '">' + inner + '</span>';
    }

    // Слот для разметки, собранной через DOM.
    function createSlot(userId) {
        var slot = document.createElement('span');
        slot.className = 'user-badge-slot';
        slot.dataset.badgeUser = String(Number(userId));
        renderSlot(slot);
        load(Number(userId));
        return slot;
    }

    // --- Просмотр бейджа: полное изображение, название, описание и дата получения ---

    function ensureViewer() {
        if (viewer) return viewer;
        var overlay = document.createElement('div');
        overlay.className = 'profile-overlay badge-viewer-overlay';
        overlay.setAttribute('aria-labelledby', 'badgeViewerName');
        overlay.innerHTML =
            '<div class="badge-viewer-panel">' +
            '<button class="profile-close badge-viewer-close" type="button"></button>' +
            '<img class="badge-viewer-image" alt="">' +
            '<div class="badge-viewer-name" id="badgeViewerName"></div>' +
            '<div class="badge-viewer-desc"></div>' +
            '<div class="badge-viewer-date"></div>' +
            '</div>';
        document.body.appendChild(overlay);
        var close = overlay.querySelector('.badge-viewer-close');
        close.innerHTML = BF.icons.html('close');
        close.setAttribute('aria-label', BF.i18n.t('common.close'));
        function hide() {
            BF.utils.closeOverlay(overlay);
        }
        close.addEventListener('click', hide);
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) hide();
        });
        overlay.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                e.stopPropagation();
                hide();
            }
        });
        viewer = {
            overlay: overlay,
            image: overlay.querySelector('.badge-viewer-image'),
            name: overlay.querySelector('.badge-viewer-name'),
            desc: overlay.querySelector('.badge-viewer-desc'),
            date: overlay.querySelector('.badge-viewer-date')
        };
        return viewer;
    }

    function showViewer(badge) {
        if (!badge) return;
        var v = ensureViewer();
        v.image.src = badge.imageUrl || badge.miniImageUrl || '';
        v.image.hidden = !(badge.imageUrl || badge.miniImageUrl);
        v.name.textContent = badge.name || '';
        v.desc.textContent = badge.description || '';
        v.desc.hidden = !badge.description;
        if (badge.assignedAt) {
            var date = new Date(badge.assignedAt).toLocaleDateString(BF.i18n.current(), {
                day: 'numeric',
                month: 'long',
                year: 'numeric'
            });
            v.date.textContent = BF.i18n.t('badge.received', { date: date });
            v.date.hidden = false;
        } else {
            v.date.hidden = true;
        }
        BF.utils.openOverlay(v.overlay);
    }

    window.BF.userBadges = {
        load: load,
        set: set,
        cached: cached,
        primary: primary,
        primaryOf: primaryOf,
        slotHtml: slotHtml,
        createSlot: createSlot,
        miniImage: miniImage,
        showViewer: showViewer
    };
})();
