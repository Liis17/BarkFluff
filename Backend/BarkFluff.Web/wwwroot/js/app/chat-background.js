/**
 * Per-chat background picker (#chatBackgroundSelector): the "use global background" card plus the user's
 * uploaded backgrounds; picking one saves it for the chat and closes the dialog.
 * Requires: BF.api, BF.files, BF.i18n, BF.personalization, BF.utils
 * Exposes: BF.chatBackground
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    function $(selector) {
        return document.querySelector(selector);
    }

    function open(chatId, title) {
        if (!chatId) return;
        var u = BF.utils;
        var overlay = $('#chatBackgroundSelector');
        var selectorTitle = $('#chatBackgroundSelectorTitle');
        var grid = $('#chatBackgroundSelectorGrid');
        selectorTitle.textContent = BF.i18n.t('chat.background.for', {
            title: title || BF.i18n.t('common.chat').toLowerCase()
        });
        grid.innerHTML = '<div class="sd-hint">' + u.escapeHtml(BF.i18n.t('common.loadingShort')) + '</div>';
        u.openOverlay(overlay);

        BF.api
            .getPersonalization()
            .then(function (data) {
                var ids = ((data && data.personalization) || {}).chatBackgroundFileIds || [];
                var current = BF.personalization.getChatBackgroundFileId(chatId);
                grid.innerHTML = '';
                function addCard(fileId, label) {
                    var card = document.createElement('button');
                    card.type = 'button';
                    card.className =
                        'sd-bg-card' + (current === fileId ? ' active' : '') + (!fileId ? ' none-card' : '');
                    if (fileId) {
                        var image = document.createElement('img');
                        image.alt = '';
                        BF.files.bindResilientMedia(image, fileId, true);
                        card.appendChild(image);
                        BF.files.getFileUrls([fileId]).then(function (urls) {
                            var item = urls && urls[0];
                            if (item) image.src = item.previewUrl || item.url;
                        });
                    } else {
                        card.textContent = label;
                    }
                    card.addEventListener('click', function () {
                        card.disabled = true;
                        BF.personalization
                            .setChatBackgroundFileId(chatId, fileId)
                            .then(function () {
                                u.closeOverlay(overlay);
                            })
                            .catch(function () {
                                card.disabled = false;
                            });
                    });
                    grid.appendChild(card);
                }
                addCard('', BF.i18n.t('chat.background.useGlobal'));
                ids.forEach(function (fileId) {
                    addCard(fileId, '');
                });
            })
            .catch(function () {
                grid.innerHTML =
                    '<div class="sd-hint error">' + u.escapeHtml(BF.i18n.t('chat.background.error')) + '</div>';
            });
    }

    function init() {
        var overlay = $('#chatBackgroundSelector');
        var close = $('#chatBackgroundSelectorClose');
        if (close)
            close.addEventListener('click', function () {
                BF.utils.closeOverlay(overlay);
            });
        if (overlay)
            overlay.addEventListener('click', function (e) {
                if (e.target === overlay) BF.utils.closeOverlay(overlay);
            });
    }

    window.BF.chatBackground = { init: init, open: open };
})();
