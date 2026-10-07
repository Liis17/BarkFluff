/**
 * Sticker picker next to the composer: pack tabs, «Recent» tab, emoji search, sending.
 * Requires: BF.api, BF.files, BF.icons, BF.i18n, BF.node, BF.utils
 * Exposes: BF.stickerPicker
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var stickerBtn;
    var stickerPicker;
    var stickerSearch;
    var stickerPacksBar;
    var stickerGrid;

    var stickerPacksCache = null;
    var stickerPacksContentCache = {}; // packId → { stickers, coverFileId }
    var currentStickerPackId = null;
    var RECENT_TAB = '__recent__';
    var RECENT_STICKER_LIMIT = 32;
    var recentStickerIds = [];
    var stickerSearchQuery = '';
    var stickerGridRenderVersion = 0;

    function $(selector) {
        return document.querySelector(selector);
    }

    function recentStickersKey() {
        return BF.node.key('bf_recent_stickers_' + deps.getMyUserId());
    }

    function addRecentSticker(stickerId) {
        if (!stickerId) return;
        var i = recentStickerIds.indexOf(stickerId);
        if (i >= 0) recentStickerIds.splice(i, 1);
        recentStickerIds.unshift(stickerId);
        if (recentStickerIds.length > RECENT_STICKER_LIMIT) recentStickerIds.length = RECENT_STICKER_LIMIT;
        try {
            localStorage.setItem(recentStickersKey(), JSON.stringify(recentStickerIds));
        } catch (_) {}
    }

    // Локальные id резолвятся против закешированных паков: стикер, удалённый из пака, просто исчезает.
    function resolveRecentStickers() {
        var result = [];
        recentStickerIds.forEach(function (id) {
            for (var packId in stickerPacksContentCache) {
                var stickers = stickerPacksContentCache[packId].stickers || [];
                for (var i = 0; i < stickers.length; i++) {
                    if (stickers[i].id === id) {
                        result.push(stickers[i]);
                        return;
                    }
                }
            }
        });
        return result;
    }

    function emptyMarkup(key) {
        return '<div class="sticker-pack-empty">' + BF.utils.escapeHtml(BF.i18n.t(key)) + '</div>';
    }

    function defaultStickerTabId() {
        return resolveRecentStickers().length > 0 ? RECENT_TAB : stickerPacksCache[0].id;
    }

    function loadStickerPacks() {
        if (stickerPacksCache) {
            if (stickerPacksCache.length === 0) return;
            renderStickerPackTabs();
            if (!currentStickerPackId) loadStickerPackContent(defaultStickerTabId());
            else if (currentStickerPackId === RECENT_TAB) loadStickerPackContent(RECENT_TAB);
            return;
        }
        BF.api
            .listStickerPacks(0, 50)
            .then(function (data) {
                stickerPacksCache = data.packs || [];
                if (stickerPacksCache.length === 0) {
                    if (stickerGrid) stickerGrid.innerHTML = emptyMarkup('sticker.noPacks');
                    return;
                }
                // Prefetch всех паков для обложек и кэша контента
                var loads = stickerPacksCache.map(function (p) {
                    return BF.api
                        .getStickerPack(p.id)
                        .then(function (d) {
                            var stickers = d.stickers || [];
                            var cover =
                                stickers.find(function (s) {
                                    return s.id === p.coverStickerId;
                                }) || stickers[0];
                            stickerPacksContentCache[p.id] = {
                                stickers: stickers,
                                coverFileId: cover ? cover.previewFileId || cover.fileId : null
                            };
                        })
                        .catch(function () {
                            stickerPacksContentCache[p.id] = { stickers: [], coverFileId: null };
                        });
                });
                Promise.all(loads)
                    .then(function () {
                        var coverIds = stickerPacksCache
                            .map(function (p) {
                                return stickerPacksContentCache[p.id].coverFileId;
                            })
                            .filter(Boolean);
                        return coverIds.length > 0 ? BF.files.getFileUrls(coverIds) : Promise.resolve();
                    })
                    .then(function () {
                        renderStickerPackTabs();
                        loadStickerPackContent(defaultStickerTabId());
                    });
            })
            .catch(function () {
                if (stickerGrid) stickerGrid.innerHTML = emptyMarkup('common.loadError');
            });
    }

    function renderStickerPackTabs() {
        if (!stickerPacksBar) return;
        stickerPacksBar.innerHTML = '';
        if (resolveRecentStickers().length > 0) {
            var recentTab = document.createElement('div');
            recentTab.className = 'sticker-pack-tab recent' + (currentStickerPackId === RECENT_TAB ? ' active' : '');
            recentTab.title = BF.i18n.t('sticker.recent');
            recentTab.appendChild(BF.icons.element('history'));
            recentTab.addEventListener('click', function (event) {
                event.stopPropagation();
                loadStickerPackContent(RECENT_TAB);
            });
            stickerPacksBar.appendChild(recentTab);
        }
        stickerPacksCache.forEach(function (pack) {
            var tab = document.createElement('div');
            tab.className = 'sticker-pack-tab' + (pack.id === currentStickerPackId ? ' active' : '');
            tab.title = pack.name || '';
            var cached = stickerPacksContentCache[pack.id];
            var coverFid = cached && cached.coverFileId;
            var fd = coverFid ? BF.files.getCachedFileUrl(coverFid) : null;
            var url = fd && (fd.previewUrl || fd.url);
            if (url) {
                var img = document.createElement('img');
                img.src = url;
                img.alt = pack.name || '';
                BF.files.bindResilientMedia(img, coverFid, true);
                tab.appendChild(img);
            } else {
                tab.textContent = (pack.name || '?')[0].toUpperCase();
            }
            tab.addEventListener('click', function (event) {
                event.stopPropagation();
                loadStickerPackContent(pack.id);
            });
            stickerPacksBar.appendChild(tab);
        });
    }

    function currentTabStickers() {
        if (currentStickerPackId === RECENT_TAB) return resolveRecentStickers();
        var cached = stickerPacksContentCache[currentStickerPackId];
        return cached ? cached.stickers : [];
    }

    function renderStickerGrid() {
        if (!stickerGrid) return;
        var renderVersion = ++stickerGridRenderVersion;
        stickerGrid.innerHTML = '';
        var stickers = currentTabStickers();
        if (stickers.length === 0) {
            stickerGrid.innerHTML = emptyMarkup(
                currentStickerPackId === RECENT_TAB ? 'sticker.empty' : 'sticker.packEmpty'
            );
            return;
        }
        if (stickerSearchQuery) {
            stickers = stickers.filter(function (s) {
                return (s.emoji || '').indexOf(stickerSearchQuery) >= 0;
            });
            if (stickers.length === 0) {
                stickerGrid.innerHTML = emptyMarkup('sticker.empty');
                return;
            }
        }
        // Показываем full-версии стикеров (fileId, не preview)
        var fileIds = stickers
            .map(function (s) {
                return s.fileId;
            })
            .filter(Boolean);
        BF.files.getFileUrls(fileIds).then(function () {
            if (renderVersion !== stickerGridRenderVersion) return;
            stickers.forEach(function (s) {
                var fd = BF.files.getCachedFileUrl(s.fileId);
                var url = fd && fd.url;
                if (!url) return;
                var img = document.createElement('img');
                img.src = url;
                img.title = s.emoji || '';
                img.loading = 'lazy';
                img.addEventListener('click', function () {
                    send(s);
                });
                BF.files.bindResilientMedia(img, s.fileId, false);
                stickerGrid.appendChild(img);
            });
        });
    }

    function loadStickerPackContent(packId) {
        currentStickerPackId = packId;
        renderStickerPackTabs();
        renderStickerGrid();
    }

    function send(sticker) {
        var fileId = sticker && sticker.fileId;
        var sentChatId = deps.getCurrentChatId();
        if (!sentChatId || deps.getCurrentChatType() === 1 || !fileId) return;
        addRecentSticker(sticker.id);
        stickerPicker.classList.remove('visible');
        stickerBtn.classList.remove('active');
        BF.api
            .sendMessage({ chatId: sentChatId, text: null, fileIds: [fileId] })
            .then(function (resp) {
                if (resp && resp.message) deps.onSent(sentChatId, resp.message);
            })
            .catch(function () {
                deps.showToast(BF.i18n.t('error.sendMessage'), true);
            });
    }

    function init(options) {
        deps = options;
        stickerBtn = $('#stickerBtn');
        stickerPicker = $('#stickerPicker');
        stickerSearch = $('#stickerSearch');
        stickerPacksBar = $('#stickerPacksBar');
        stickerGrid = $('#stickerGrid');

        try {
            recentStickerIds = JSON.parse(localStorage.getItem(recentStickersKey()) || '[]') || [];
        } catch (_) {
            recentStickerIds = [];
        }

        if (stickerBtn) {
            stickerBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                var isOpen = stickerPicker.classList.contains('visible');
                stickerPicker.classList.toggle('visible', !isOpen);
                stickerBtn.classList.toggle('active', !isOpen);
                if (!isOpen) {
                    if (stickerSearch) {
                        stickerSearch.value = '';
                        stickerSearchQuery = '';
                    }
                    loadStickerPacks();
                }
            });
        }

        document.addEventListener('click', function (e) {
            if (!stickerPicker || !stickerPicker.classList.contains('visible')) return;
            if (!stickerPicker.contains(e.target) && !stickerBtn.contains(e.target)) {
                stickerPicker.classList.remove('visible');
                stickerBtn.classList.remove('active');
            }
        });

        if (stickerSearch) {
            stickerSearch.addEventListener('input', function () {
                stickerSearchQuery = stickerSearch.value.trim();
                renderStickerGrid();
            });
        }
    }

    window.BF.stickerPicker = { init: init, send: send };
})();
