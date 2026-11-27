/**
 * Call history overlay (Calls.ListCallHistory): all or missed calls, cursor pagination by started_at,
 * opening the chat of a call on click.
 * Requires: BF.api, BF.i18n, BF.utils
 * Exposes: BF.callHistory
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var PAGE_SIZE = 30;
    var SKELETON_ROWS = 4;
    // CallsApi enums (calls_api.proto)
    var DIRECTION_INCOMING = 1;
    var MEDIA_VIDEO = 2;

    var deps;
    var overlay;
    var listEl;
    var moreBtn;
    var filterButtons = [];
    var missedOnly = false;
    var items = [];
    var hasMore = false;
    var loading = false;
    var loadToken = 0;

    function isMissed(item) {
        return item.direction === DIRECTION_INCOMING && !item.answeredAt;
    }

    function formatDuration(seconds) {
        var s = Math.max(0, Math.floor(seconds || 0));
        var m = Math.floor(s / 60);
        var rest = s % 60;
        return m + ':' + (rest < 10 ? '0' : '') + rest;
    }

    function setName(el, item) {
        var u = BF.utils;
        if (item.isGroup) {
            var chat = deps.getChats().find(function (c) {
                return String(c.id) === String(item.chatId);
            });
            el.textContent = (chat && chat.title) || BF.i18n.t('call.group');
            return;
        }
        el.textContent = BF.i18n.t('common.user');
        deps.getUser(item.peerUserId)
            .then(function (user) {
                if (!user) return;
                el.textContent = u.isRestrictedUser(user)
                    ? u.restrictedUserName(user)
                    : ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || user.username || el.textContent;
            })
            .catch(function () {});
    }

    function renderItem(item) {
        var u = BF.utils;
        var el = document.createElement('button');
        el.type = 'button';
        el.className = 'call-history-item' + (isMissed(item) ? ' missed' : '');
        var kind = isMissed(item)
            ? 'callHistory.missedCall'
            : item.direction === DIRECTION_INCOMING
              ? 'callHistory.incoming'
              : 'callHistory.outgoing';
        var media = BF.i18n.t(item.mediaType === MEDIA_VIDEO ? 'attachment.video' : 'attachment.audio');
        var meta = BF.i18n.t(kind) + ' · ' + media + ' · ' + u.formatChatListTime(item.startedAt);
        if (item.answeredAt) meta += ' · ' + formatDuration(item.durationSeconds);
        el.innerHTML =
            '<span class="call-history-name"></span><span class="call-history-meta">' + u.escapeHtml(meta) + '</span>';
        setName(el.querySelector('.call-history-name'), item);
        el.addEventListener('click', function () {
            openCallChat(item);
        });
        return el;
    }

    function openCallChat(item) {
        if (item.isGroup) {
            close();
            deps.openChat(item.chatId);
            return;
        }
        BF.api
            .getPersonChatId(item.peerUserId)
            .then(function (d) {
                if (!d || !d.chatId) return;
                close();
                deps.openChat(d.chatId);
            })
            .catch(function () {
                deps.showToast(BF.i18n.t('common.loadError'), true);
            });
    }

    // Первая страница: заглушки формы записи; текст загрузки виден только скринридеру.
    function skeletonHtml() {
        var rows = '';
        for (var i = 0; i < SKELETON_ROWS; i++) {
            rows +=
                '<div class="call-history-skeleton" aria-hidden="true"><span class="skeleton call-history-skeleton-name"></span>' +
                '<span class="skeleton call-history-skeleton-meta"></span></div>';
        }
        return '<span class="sr-only">' + BF.utils.escapeHtml(BF.i18n.t('common.loading')) + '</span>' + rows;
    }

    function render() {
        listEl.innerHTML = '';
        if (!items.length && loading) listEl.innerHTML = skeletonHtml();
        if (!items.length && !loading) {
            var empty = document.createElement('div');
            empty.className = 'call-history-empty';
            empty.textContent = BF.i18n.t('callHistory.empty');
            listEl.appendChild(empty);
        }
        items.forEach(function (item) {
            listEl.appendChild(renderItem(item));
        });
        moreBtn.hidden = !hasMore;
        moreBtn.disabled = loading;
    }

    function load(reset) {
        if (loading && !reset) return;
        if (reset) {
            items = [];
            hasMore = false;
        }
        var before = items.length ? items[items.length - 1].startedAt : null;
        var token = ++loadToken;
        loading = true;
        render();
        BF.api
            .listCallHistory(missedOnly, before, PAGE_SIZE)
            .then(function (data) {
                if (token !== loadToken) return;
                items = items.concat(data.items);
                hasMore = data.hasMore;
            })
            .catch(function () {
                if (token === loadToken) deps.showToast(BF.i18n.t('common.loadError'), true);
            })
            .then(function () {
                if (token !== loadToken) return;
                loading = false;
                render();
            });
    }

    function setFilter(missed) {
        missedOnly = missed;
        filterButtons.forEach(function (btn) {
            var active = (btn.dataset.filter === 'missed') === missed;
            btn.classList.toggle('active', active);
            btn.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
        load(true);
    }

    function open() {
        BF.pages.show('calls');
        setFilter(missedOnly);
    }

    function close() {
        loadToken++;
        loading = false;
        BF.pages.hide('calls');
    }

    function init(options) {
        deps = options;
        overlay = document.getElementById('callHistoryOverlay');
        listEl = document.getElementById('callHistoryList');
        moreBtn = document.getElementById('callHistoryMore');
        filterButtons = Array.prototype.slice.call(overlay.querySelectorAll('[data-filter]'));
        filterButtons.forEach(function (btn) {
            btn.addEventListener('click', function () {
                setFilter(btn.dataset.filter === 'missed');
            });
        });
        moreBtn.addEventListener('click', function () {
            load(false);
        });
        BF.pages.register('calls', overlay, close);
        document.getElementById('callHistoryClose').addEventListener('click', function () {
            close();
            if (window.__mobileShowList) window.__mobileShowList();
        });
    }

    window.BF.callHistory = { init: init, open: open, close: close };
})();
