/**
 * Sticker pack window: opened from a sticker in the message list, from the picker or by a /addstickers/<name> link.
 * Shows the pack, sends a sticker on click, adds the pack (footer button) or removes it and copies the link (⋯ menu).
 * Requires: BF.api, BF.files, BF.i18n, BF.utils; optional BF.stickerPicker
 * Exposes: BF.stickerPack
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var overlay;
    var heroBg;
    var cover;
    var name;
    var desc;
    var grid;
    var footer;
    var addButton;
    var moreButton;
    var menu;
    var removeItem;

    var current = null; // { pack, stickers, isAdded }
    var openVersion = 0;

    function $(selector) {
        return document.querySelector(selector);
    }

    function coverSticker(data) {
        var stickers = data.stickers || [];
        return (
            stickers.find(function (s) {
                return s.id === data.pack.coverStickerId;
            }) || stickers[0]
        );
    }

    function packLink(pack) {
        return pack && pack.shortName ? window.location.origin + '/addstickers/' + pack.shortName : '';
    }

    function renderControls() {
        var isAdded = !!(current && current.isAdded);
        footer.hidden = !current || isAdded;
        addButton.disabled = false;
        removeItem.hidden = !isAdded;
        moreButton.hidden = !current || (!isAdded && !packLink(current.pack));
    }

    function renderCover(data) {
        var sticker = coverSticker(data);
        cover.replaceChildren();
        heroBg.replaceChildren();
        if (!sticker) {
            cover.textContent = (data.pack.name || '?')[0].toUpperCase();
            return;
        }
        var fileId = sticker.previewFileId || sticker.fileId;
        BF.files.getFileUrls([fileId]).then(function () {
            var fd = BF.files.getCachedFileUrl(fileId);
            var url = fd && (fd.previewUrl || fd.url);
            if (!url) return;
            [cover, heroBg].forEach(function (container) {
                var img = document.createElement('img');
                img.alt = '';
                img.src = url;
                BF.files.bindResilientMedia(img, fileId, true);
                container.replaceChildren(img);
            });
        });
    }

    function renderPack(data, version) {
        if (version !== openVersion) return;
        current = data;
        var pack = data.pack || {};
        var stickers = data.stickers || [];

        name.textContent = pack.name || '';
        var count = BF.i18n.tp ? BF.i18n.tp('sticker.count', stickers.length) : String(stickers.length);
        desc.textContent = pack.description ? pack.description + ' · ' + count : count;
        renderCover(data);
        renderControls();

        grid.innerHTML = '';
        if (stickers.length === 0) {
            grid.innerHTML = statusMarkup('sticker.packEmpty');
            return;
        }
        var fileIds = stickers
            .map(function (s) {
                return s.fileId;
            })
            .filter(Boolean);
        BF.files.getFileUrls(fileIds).then(function () {
            if (version !== openVersion) return;
            stickers.forEach(function (s) {
                var fd = BF.files.getCachedFileUrl(s.fileId);
                var url = fd && fd.url;
                if (!url) return;
                var img = document.createElement('img');
                img.src = url;
                img.alt = s.emoji || '';
                img.title = s.emoji || '';
                img.loading = 'lazy';
                img.addEventListener('click', function () {
                    if (deps && typeof deps.onStickerSend === 'function') {
                        close();
                        deps.onStickerSend(s);
                    }
                });
                BF.files.bindResilientMedia(img, s.fileId, false);
                grid.appendChild(img);
            });
        });
    }

    function statusMarkup(key) {
        return '<div class="sp-status">' + BF.utils.escapeHtml(BF.i18n.t(key)) + '</div>';
    }

    // Сетка стикеров: квадратные заглушки ячеек; текст загрузки виден только скринридеру.
    function skeletonHtml() {
        var html = '<span class="sr-only">' + BF.utils.escapeHtml(BF.i18n.t('common.loading')) + '</span>';
        for (var i = 0; i < 8; i++) html += '<span class="skeleton sp-skeleton" aria-hidden="true"></span>';
        return html;
    }

    function open(request) {
        var version = ++openVersion;
        current = null;
        grid.innerHTML = skeletonHtml();
        cover.replaceChildren();
        heroBg.replaceChildren();
        name.textContent = '';
        desc.textContent = '';
        hideMenu();
        renderControls();
        BF.utils.openOverlay(overlay);
        request
            .then(function (data) {
                renderPack(data, version);
            })
            .catch(function () {
                if (version !== openVersion) return;
                grid.innerHTML = statusMarkup('sticker.packNotFound');
            });
    }

    function openByFile(fileId) {
        if (!fileId) return;
        open(BF.api.getStickerPackByFile(fileId));
    }

    function openById(packId) {
        if (!packId) return;
        open(BF.api.getStickerPack(packId));
    }

    function openByName(shortName) {
        if (!shortName) return;
        open(BF.api.getStickerPackByName(shortName));
    }

    function close() {
        hideMenu();
        BF.utils.closeOverlay(overlay);
    }

    function showMenu() {
        menu.hidden = false;
        moreButton.setAttribute('aria-expanded', 'true');
        var first = menu.querySelector('button:not([hidden])');
        if (first) first.focus();
    }

    function hideMenu() {
        if (!menu) return;
        menu.hidden = true;
        moreButton.setAttribute('aria-expanded', 'false');
    }

    function notifyPicker() {
        if (BF.stickerPicker && BF.stickerPicker.invalidate) BF.stickerPicker.invalidate();
    }

    function addPack() {
        if (!current) return;
        var target = current;
        addButton.disabled = true;
        BF.api
            .addMyStickerPack(target.pack.id)
            .then(function () {
                target.isAdded = true;
                if (current === target) renderControls();
                notifyPicker();
                deps.showToast(BF.i18n.t('sticker.packAdded'));
            })
            .catch(function () {
                addButton.disabled = false;
                deps.showToast(BF.i18n.t('sticker.actionFailed'), true);
            });
    }

    function removePack() {
        hideMenu();
        if (!current) return;
        var target = current;
        BF.api
            .removeMyStickerPack(target.pack.id)
            .then(function () {
                target.isAdded = false;
                if (current === target) renderControls();
                notifyPicker();
                deps.showToast(BF.i18n.t('sticker.packRemoved'));
            })
            .catch(function () {
                deps.showToast(BF.i18n.t('sticker.actionFailed'), true);
            });
    }

    function copyLink() {
        hideMenu();
        var link = current && packLink(current.pack);
        if (!link || !navigator.clipboard) return;
        navigator.clipboard.writeText(link).then(
            function () {
                deps.showToast(BF.i18n.t('common.copied'));
            },
            function () {}
        );
    }

    function init(options) {
        deps = options || {};
        overlay = $('#stickerPackOverlay');
        if (!overlay) return;
        heroBg = $('#spHeroBg');
        cover = $('#spCover');
        name = $('#spName');
        desc = $('#spDesc');
        grid = $('#spGrid');
        footer = $('#spFooter');
        addButton = $('#spAdd');
        moreButton = $('#spMore');
        menu = $('#spMenu');
        removeItem = $('#spRemove');

        $('#spClose').addEventListener('click', close);
        overlay.addEventListener('click', function (event) {
            if (event.target === overlay) close();
            else if (!menu.hidden && !menu.contains(event.target) && !moreButton.contains(event.target)) hideMenu();
        });
        overlay.addEventListener('keydown', function (event) {
            if (!menu.hidden) {
                BF.utils.handleMenuKeydown(event, menu, function (reason) {
                    hideMenu();
                    if (reason === 'escape') moreButton.focus();
                });
                return;
            }
            if (event.key === 'Escape' && overlay.classList.contains('visible')) close();
        });
        moreButton.addEventListener('click', function () {
            if (menu.hidden) showMenu();
            else hideMenu();
        });
        addButton.addEventListener('click', addPack);
        removeItem.addEventListener('click', removePack);
        $('#spCopyLink').addEventListener('click', copyLink);
    }

    window.BF.stickerPack = {
        init: init,
        openByFile: openByFile,
        openById: openById,
        openByName: openByName
    };
})();
