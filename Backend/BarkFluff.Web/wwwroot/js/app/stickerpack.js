/**
 * Sticker pack overlay: opens the pack containing a sticker from the message list.
 * Requires: BF.api, BF.files, BF.utils
 * Exposes: BF.stickerPack
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var overlay;
    var closeButton;
    var cover;
    var name;
    var desc;
    var grid;

    function $(selector) {
        return document.querySelector(selector);
    }

    function renderPack(data) {
        var pack = (data && data.pack) || {};
        var stickers = (data && data.stickers) || [];

        name.textContent = pack.name || '';
        desc.textContent = pack.description || '';
        grid.innerHTML = '';

        var coverSticker = stickers.find(function (s) { return s.id === pack.coverStickerId; }) || stickers[0];
        if (coverSticker) {
            var coverImg = document.createElement('img');
            coverImg.alt = '';
            BF.files.bindResilientMedia(coverImg, coverSticker.fileId, false);
            cover.replaceChildren(coverImg);
        } else {
            cover.textContent = (pack.name || '?')[0].toUpperCase();
        }

        var fileIds = stickers.map(function (s) { return s.fileId; }).filter(Boolean);
        BF.files.getFileUrls(fileIds).then(function () {
            stickers.forEach(function (s) {
                var fd = BF.files.getCachedFileUrl(s.fileId);
                var url = fd && fd.url;
                if (!url) return;
                var img = document.createElement('img');
                img.src = url;
                img.alt = '';
                img.title = s.emoji || '';
                img.loading = 'lazy';
                img.addEventListener('click', function () {
                    if (deps && typeof deps.onStickerSend === 'function') {
                        BF.utils.closeOverlay(overlay);
                        deps.onStickerSend(s);
                    }
                });
                BF.files.bindResilientMedia(img, s.fileId, false);
                grid.appendChild(img);
            });
        });
    }

    function openByFile(fileId) {
        if (!fileId) return;
        grid.innerHTML = '<div class="sp-status">' + BF.utils.escapeHtml(BF.i18n.t('common.loadingShort')) + '</div>';
        cover.replaceChildren();
        name.textContent = '';
        desc.textContent = '';
        BF.utils.openOverlay(overlay);
        BF.api.getStickerPackByFile(fileId).then(renderPack).catch(function () {
            grid.innerHTML = '<div class="sp-status">' + BF.utils.escapeHtml(BF.i18n.t('sticker.packNotFound')) + '</div>';
        });
    }

    function init(options) {
        deps = options || {};
        overlay = $('#stickerPackOverlay');
        if (!overlay) return;
        closeButton = $('#spClose');
        cover = $('#spCover');
        name = $('#spName');
        desc = $('#spDesc');
        grid = $('#spGrid');

        closeButton.addEventListener('click', function () {
            BF.utils.closeOverlay(overlay);
        });
        overlay.addEventListener('click', function (event) {
            if (event.target === overlay) BF.utils.closeOverlay(overlay);
        });
    }

    window.BF.stickerPack = {
        init: init,
        openByFile: openByFile
    };
})();
