/**
 * Sticker picker next to the composer: one feed with «Recent» and every pack the user added,
 * pack tabs (bottom or left, from personalization), emoji search, adding a pack by link, sending.
 * Requires: BF.api, BF.files, BF.icons, BF.i18n, BF.node, BF.utils; optional BF.personalization, BF.stickerPack
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
    var stickerFeed;
    var linkForm;
    var linkInput;

    var RECENT_TAB = '__recent__';
    var RECENT_STICKER_LIMIT = 32;
    var STICKER_LINK_PATTERN = /\/addstickers\/([A-Za-z0-9_]+)/;
    var SHORT_NAME_PATTERN = /^[A-Za-z][A-Za-z0-9_]{2,63}$/;

    var myPacks = null; // [{ pack, stickers }] — добавленные наборы, недавно добавленные первыми
    var loading = null;
    var recentStickerIds = [];
    var searchQuery = '';
    var activeTabId = null;
    var renderVersion = 0;

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

    // Недавние резолвятся по добавленным наборам: стикер из удалённого или убранного набора исчезает.
    function resolveRecentStickers() {
        var byId = {};
        (myPacks || []).forEach(function (entry) {
            entry.stickers.forEach(function (s) {
                byId[s.id] = s;
            });
        });
        return recentStickerIds
            .map(function (id) {
                return byId[id];
            })
            .filter(Boolean);
    }

    function coverFileId(entry) {
        var stickers = entry.stickers || [];
        var cover =
            stickers.find(function (s) {
                return s.id === entry.pack.coverStickerId;
            }) || stickers[0];
        return cover ? cover.previewFileId || cover.fileId : null;
    }

    function matchesSearch(sticker) {
        return !searchQuery || (sticker.emoji || '').indexOf(searchQuery) >= 0;
    }

    // Разделы ленты в порядке отображения: «Недавние», затем наборы.
    function feedSections() {
        var sections = [];
        var recent = resolveRecentStickers();
        if (recent.length > 0) {
            sections.push({ id: RECENT_TAB, title: BF.i18n.t('sticker.recent'), stickers: recent, pack: null });
        }
        (myPacks || []).forEach(function (entry) {
            sections.push({ id: entry.pack.id, title: entry.pack.name, stickers: entry.stickers, pack: entry.pack });
        });
        return sections;
    }

    function applyTabsLayout() {
        if (!stickerPicker) return;
        var layout =
            BF.personalization && BF.personalization.getStickerTabsLayout
                ? BF.personalization.getStickerTabsLayout()
                : 'bottom';
        stickerPicker.dataset.tabs = layout;
    }

    function load(force) {
        if (myPacks && !force) {
            render();
            return Promise.resolve();
        }
        if (loading) return loading;
        loading = BF.api
            .listMyStickerPacks()
            .then(function (packs) {
                myPacks = packs || [];
                var coverIds = myPacks.map(coverFileId).filter(Boolean);
                return coverIds.length > 0 ? BF.files.getFileUrls(coverIds) : null;
            })
            .then(function () {
                loading = null;
                render();
            })
            .catch(function () {
                loading = null;
                if (stickerFeed) stickerFeed.innerHTML = emptyMarkup('common.loadError');
            });
        return loading;
    }

    function emptyMarkup(key) {
        return '<div class="sticker-pack-empty">' + BF.utils.escapeHtml(BF.i18n.t(key)) + '</div>';
    }

    function render() {
        renderTabs();
        renderFeed();
    }

    function makeTab(className, title, onClick) {
        var tab = document.createElement('button');
        tab.type = 'button';
        tab.className = 'sticker-pack-tab' + (className ? ' ' + className : '');
        tab.title = title;
        tab.setAttribute('aria-label', title);
        tab.addEventListener('click', function (event) {
            event.stopPropagation();
            onClick();
        });
        return tab;
    }

    function renderTabs() {
        if (!stickerPacksBar) return;
        stickerPacksBar.innerHTML = '';
        feedSections().forEach(function (section) {
            var tab = makeTab('', section.title, function () {
                scrollToSection(section.id);
            });
            tab.dataset.sectionId = section.id;
            tab.setAttribute('role', 'tab');
            if (section.id === RECENT_TAB) {
                tab.classList.add('recent');
                tab.appendChild(BF.icons.element('history'));
            } else {
                var entry = { pack: section.pack, stickers: section.stickers };
                var fid = coverFileId(entry);
                var fd = fid ? BF.files.getCachedFileUrl(fid) : null;
                var url = fd && (fd.previewUrl || fd.url);
                if (url) {
                    var img = document.createElement('img');
                    img.src = url;
                    img.alt = '';
                    BF.files.bindResilientMedia(img, fid, true);
                    tab.appendChild(img);
                } else {
                    tab.textContent = (section.title || '?')[0].toUpperCase();
                }
            }
            stickerPacksBar.appendChild(tab);
        });
        var addTab = makeTab('add', BF.i18n.t('sticker.addByLink'), toggleLinkForm);
        addTab.appendChild(BF.icons.element('add'));
        stickerPacksBar.appendChild(addTab);
        markActiveTab(activeTabId);
    }

    function markActiveTab(sectionId) {
        activeTabId = sectionId;
        if (!stickerPacksBar) return;
        Array.prototype.forEach.call(stickerPacksBar.children, function (tab) {
            if (!tab.dataset.sectionId) return;
            var isActive = tab.dataset.sectionId === sectionId;
            tab.classList.toggle('active', isActive);
            tab.setAttribute('aria-selected', isActive ? 'true' : 'false');
        });
    }

    function renderEmptyState() {
        var box = document.createElement('div');
        box.className = 'sticker-empty-state';
        box.appendChild(BF.icons.element('stickers'));
        var title = document.createElement('strong');
        title.textContent = BF.i18n.t('sticker.noPacksTitle');
        var hint = document.createElement('p');
        hint.textContent = BF.i18n.t('sticker.noPacksHint');
        var button = document.createElement('button');
        button.type = 'button';
        button.textContent = BF.i18n.t('sticker.addByLink');
        button.addEventListener('click', function (event) {
            event.stopPropagation();
            showLinkForm();
        });
        box.appendChild(title);
        box.appendChild(hint);
        box.appendChild(button);
        stickerFeed.replaceChildren(box);
    }

    function renderFeed() {
        if (!stickerFeed) return;
        var version = ++renderVersion;
        var sections = feedSections();
        if (sections.length === 0) {
            renderEmptyState();
            return;
        }
        var visible = sections
            .map(function (section) {
                return { section: section, stickers: section.stickers.filter(matchesSearch) };
            })
            .filter(function (item) {
                return item.stickers.length > 0;
            });
        if (visible.length === 0) {
            stickerFeed.innerHTML = emptyMarkup('sticker.empty');
            return;
        }

        // Полные версии стикеров (fileId, не preview); ссылки запрашиваются одним пакетом.
        var fileIds = [];
        visible.forEach(function (item) {
            item.stickers.forEach(function (s) {
                if (s.fileId) fileIds.push(s.fileId);
            });
        });
        BF.files.getFileUrls(fileIds).then(function () {
            if (version !== renderVersion) return;
            stickerFeed.innerHTML = '';
            visible.forEach(function (item) {
                stickerFeed.appendChild(renderSection(item.section, item.stickers));
            });
            stickerFeed.scrollTop = 0;
            markActiveTab(visible[0].section.id);
        });
    }

    function renderSection(section, stickers) {
        var el = document.createElement('section');
        el.className = 'sticker-section';
        el.dataset.sectionId = section.id;

        var header = document.createElement('div');
        header.className = 'sticker-section-title';
        var title = document.createElement('span');
        title.textContent = section.title;
        header.appendChild(title);
        if (section.pack && BF.stickerPack && BF.stickerPack.openById) {
            var open = document.createElement('button');
            open.type = 'button';
            open.className = 'sticker-section-open';
            open.textContent = BF.i18n.t('sticker.openPack');
            open.addEventListener('click', function (event) {
                event.stopPropagation();
                closePicker();
                BF.stickerPack.openById(section.pack.id);
            });
            header.appendChild(open);
        }
        el.appendChild(header);

        var grid = document.createElement('div');
        grid.className = 'sticker-section-grid';
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
                send(s);
            });
            BF.files.bindResilientMedia(img, s.fileId, false);
            grid.appendChild(img);
        });
        el.appendChild(grid);
        return el;
    }

    function findSectionElement(sectionId) {
        var children = stickerFeed ? stickerFeed.children : [];
        for (var i = 0; i < children.length; i++) {
            if (children[i].dataset && children[i].dataset.sectionId === sectionId) return children[i];
        }
        return null;
    }

    function scrollToSection(sectionId) {
        var el = findSectionElement(sectionId);
        if (!el) return;
        stickerFeed.scrollTop = el.offsetTop;
        markActiveTab(sectionId);
    }

    // Активная вкладка — последний раздел, верх которого уже прокручен до верха ленты.
    function syncActiveTabWithScroll() {
        var children = stickerFeed.children;
        var current = null;
        for (var i = 0; i < children.length; i++) {
            if (!children[i].dataset || !children[i].dataset.sectionId) continue;
            if (children[i].offsetTop - 8 <= stickerFeed.scrollTop) current = children[i].dataset.sectionId;
        }
        if (!current && children[0] && children[0].dataset) current = children[0].dataset.sectionId;
        if (current && current !== activeTabId) markActiveTab(current);
    }

    function showLinkForm() {
        if (!linkForm) return;
        linkForm.hidden = false;
        linkInput.value = '';
        linkInput.focus();
    }

    function toggleLinkForm() {
        if (!linkForm) return;
        if (linkForm.hidden) showLinkForm();
        else linkForm.hidden = true;
    }

    /** Короткое имя набора из ссылки …/addstickers/<имя> или из самого имени; null — не распознано. */
    function parseStickerLink(value) {
        var text = String(value || '').trim();
        var match = text.match(STICKER_LINK_PATTERN);
        var name = match ? match[1] : text;
        return SHORT_NAME_PATTERN.test(name) ? name : null;
    }

    function submitLink(event) {
        event.preventDefault();
        var name = parseStickerLink(linkInput.value);
        if (!name) {
            deps.showToast(BF.i18n.t('sticker.packNotFound'), true);
            return;
        }
        linkForm.hidden = true;
        closePicker();
        if (BF.stickerPack && BF.stickerPack.openByName) BF.stickerPack.openByName(name);
    }

    function closePicker() {
        stickerPicker.classList.remove('visible');
        stickerBtn.classList.remove('active');
    }

    function send(sticker) {
        var fileId = sticker && sticker.fileId;
        var sentChatId = deps.getCurrentChatId();
        if (!sentChatId || deps.getCurrentChatType() === 1 || !fileId) return;
        addRecentSticker(sticker.id);
        closePicker();
        BF.api
            .sendMessage({ chatId: sentChatId, text: null, fileIds: [fileId] })
            .then(function (resp) {
                if (resp && resp.message) deps.onSent(sentChatId, resp.message);
            })
            .catch(function () {
                deps.showToast(BF.i18n.t('error.sendMessage'), true);
            });
    }

    /** Набор добавлен или убран в окне набора — при следующем открытии список перечитывается с сервера. */
    function invalidate() {
        myPacks = null;
        if (stickerPicker && stickerPicker.classList.contains('visible')) load(true);
    }

    function init(options) {
        deps = options;
        stickerBtn = $('#stickerBtn');
        stickerPicker = $('#stickerPicker');
        stickerSearch = $('#stickerSearch');
        stickerPacksBar = $('#stickerPacksBar');
        stickerFeed = $('#stickerFeed');
        linkForm = $('#stickerLinkForm');
        linkInput = $('#stickerLinkInput');

        try {
            recentStickerIds = JSON.parse(localStorage.getItem(recentStickersKey()) || '[]') || [];
        } catch (_) {
            recentStickerIds = [];
        }

        applyTabsLayout();
        if (BF.personalization && BF.personalization.onChange) BF.personalization.onChange(applyTabsLayout);

        if (stickerBtn) {
            stickerBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                var isOpen = stickerPicker.classList.contains('visible');
                stickerPicker.classList.toggle('visible', !isOpen);
                stickerBtn.classList.toggle('active', !isOpen);
                if (!isOpen) {
                    if (stickerSearch) {
                        stickerSearch.value = '';
                        searchQuery = '';
                    }
                    if (linkForm) linkForm.hidden = true;
                    applyTabsLayout();
                    load(false);
                }
            });
        }

        document.addEventListener('click', function (e) {
            if (!stickerPicker || !stickerPicker.classList.contains('visible')) return;
            if (!stickerPicker.contains(e.target) && !stickerBtn.contains(e.target)) closePicker();
        });

        if (stickerSearch) {
            stickerSearch.addEventListener('input', function () {
                searchQuery = stickerSearch.value.trim();
                renderFeed();
            });
        }

        if (stickerFeed) stickerFeed.addEventListener('scroll', syncActiveTabWithScroll);
        if (linkForm) linkForm.addEventListener('submit', submitLink);
    }

    window.BF.stickerPicker = {
        init: init,
        send: send,
        invalidate: invalidate,
        parseStickerLink: parseStickerLink
    };
})();
