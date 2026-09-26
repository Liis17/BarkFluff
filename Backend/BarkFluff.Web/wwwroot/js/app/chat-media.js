/**
 * Media tabs of the open chat (media / files / audio / voice) shared by the profile and group panels.
 * A "panels" object owns the DOM of one panel; results of superseded requests are dropped.
 * Requires: BF.api, BF.files, BF.i18n
 * Exposes: BF.chatMedia
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var MEDIA_TAB_TYPES = ['media', 'files', 'audio', 'voice'];

    function createPanels(container) {
        var state = { chatId: null, requestIds: {}, panes: {}, contents: {}, fileSearch: null, searchTimer: null };
        container.replaceChildren();
        MEDIA_TAB_TYPES.forEach(function (type) {
            var pane = document.createElement('div');
            pane.className = 'profile-media-pane';
            pane.dataset.type = type;
            var content = document.createElement('div');
            if (type === 'files') {
                var input = document.createElement('input');
                input.className = 'profile-file-search';
                input.type = 'search';
                input.placeholder = BF.i18n.t('media.searchFiles.placeholder');
                input.maxLength = 255;
                input.autocomplete = 'off';
                state.fileSearch = input;
                pane.appendChild(input);
            }
            pane.appendChild(content);
            container.appendChild(pane);
            state.panes[type] = pane;
            state.contents[type] = content;
            state.requestIds[type] = 0;
        });
        state.panes.media.classList.add('active');
        state.fileSearch.addEventListener('input', function () {
            clearTimeout(state.searchTimer);
            state.searchTimer = setTimeout(function () {
                render('files', state);
            }, 300);
        });
        return state;
    }

    function setTabActive(selector, panels, type) {
        document.querySelectorAll(selector).forEach(function (tab) {
            tab.classList.toggle('active', tab.dataset.type === type);
        });
        MEDIA_TAB_TYPES.forEach(function (tabType) {
            panels.panes[tabType].classList.toggle('active', tabType === type);
        });
    }

    function isCurrentMediaRequest(panels, type, chatId, requestId) {
        return panels.chatId === chatId && deps.getCurrentChatId() === chatId && panels.requestIds[type] === requestId;
    }

    function makeStaticGifPreview(fileUrl) {
        if (!fileUrl) return Promise.reject(new Error('GIF URL is missing'));
        return fetch(fileUrl)
            .then(function (response) {
                if (!response.ok) throw new Error('GIF could not be loaded');
                return response.blob();
            })
            .then(function (blob) {
                return new Promise(function (resolve, reject) {
                    var objectUrl = URL.createObjectURL(blob);
                    var image = new Image();
                    image.onload = function () {
                        URL.revokeObjectURL(objectUrl);
                        var maxSide = 1024;
                        var scale = Math.min(1, maxSide / Math.max(image.naturalWidth, image.naturalHeight));
                        var canvas = document.createElement('canvas');
                        canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
                        canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
                        canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
                        resolve(canvas.toDataURL('image/jpeg', 0.82));
                    };
                    image.onerror = function () {
                        URL.revokeObjectURL(objectUrl);
                        reject(new Error('GIF could not be decoded'));
                    };
                    image.src = objectUrl;
                });
            });
    }

    function getMediaUrls(att) {
        return BF.files.getFileUrls([att.fileId]).then(function (urls) {
            var file = urls[0] || {};
            return {
                full: file.url || att.previewUrl || '',
                preview: file.previewUrl || att.previewUrl || ''
            };
        });
    }

    function render(type, panels) {
        var chatId = deps.getCurrentChatId();
        if (!chatId) return;
        if (panels.chatId !== chatId) {
            panels.chatId = chatId;
            MEDIA_TAB_TYPES.forEach(function (tabType) {
                panels.requestIds[tabType]++;
                panels.contents[tabType].replaceChildren();
            });
            panels.fileSearch.value = '';
        }

        var content = panels.contents[type];
        var requestId = ++panels.requestIds[type];
        content.replaceChildren();
        function current() {
            return isCurrentMediaRequest(panels, type, chatId, requestId);
        }
        function showEmpty(text) {
            if (current()) content.innerHTML = '<div class="profile-media-empty">' + text + '</div>';
        }

        if (type === 'media') {
            Promise.all([
                BF.api.listChatAttachments(chatId, 1, 0, 30),
                BF.api.listChatAttachments(chatId, 2, 0, 30),
                BF.api.listChatAttachments(chatId, 3, 0, 30)
            ])
                .then(function (results) {
                    if (!current()) return;
                    var all = (results[0].attachments || []).concat(
                        results[1].attachments || [],
                        results[2].attachments || []
                    );
                    all.sort(function (a, b) {
                        return (b.sentAt || 0) - (a.sentAt || 0);
                    });
                    if (all.length === 0) {
                        showEmpty(BF.i18n.t('media.empty.media'));
                        return;
                    }

                    var grid = document.createElement('div');
                    grid.className = 'profile-media-grid';
                    content.appendChild(grid);
                    var chain = Promise.resolve();
                    all.forEach(function (item) {
                        chain = chain.then(function () {
                            var att = item.attachment;
                            if (!att || !att.fileId) return;
                            return getMediaUrls(att)
                                .then(function (urls) {
                                    var previewPromise =
                                        att.type === 'GIF' && !urls.preview
                                            ? makeStaticGifPreview(urls.full)
                                            : Promise.resolve(urls.preview || urls.full);
                                    return previewPromise.then(function (preview) {
                                        if (!current()) return;
                                        var tile = document.createElement('button');
                                        tile.type = 'button';
                                        tile.className = 'profile-media-tile';
                                        tile.setAttribute(
                                            'aria-label',
                                            BF.i18n.t(att.type === 'VIDEO' ? 'media.openVideo' : 'media.openImage')
                                        );
                                        if (preview) {
                                            var img = document.createElement('img');
                                            img.src = preview;
                                            img.loading = 'lazy';
                                            img.alt = '';
                                            BF.files.bindResilientMedia(img, att.fileId, true);
                                            tile.appendChild(img);
                                        } else {
                                            var placeholder = document.createElement('span');
                                            placeholder.className = 'profile-media-placeholder';
                                            placeholder.textContent = BF.i18n.t('media.noPreview');
                                            tile.appendChild(placeholder);
                                        }
                                        if (att.type === 'VIDEO') {
                                            var play = document.createElement('span');
                                            play.className = 'profile-video-play';
                                            play.textContent = '▶';
                                            tile.appendChild(play);
                                        }
                                        tile.addEventListener('click', function () {
                                            // В profile/group панели GIF должен остаться неподвижным
                                            // и в просмотрщике; чат и его общий просмотрщик не меняем.
                                            if (att.type === 'GIF') {
                                                if (preview) deps.showMediaOverlay('image', preview, null);
                                                return;
                                            }
                                            deps.showMediaOverlay(
                                                att.type === 'VIDEO' ? 'video' : 'image',
                                                urls.full || preview,
                                                att.fileId
                                            );
                                        });
                                        grid.appendChild(tile);
                                    });
                                })
                                .catch(function () {
                                    if (!current()) return;
                                    var tile = document.createElement('div');
                                    tile.className = 'profile-media-placeholder';
                                    tile.textContent = BF.i18n.t('media.noPreview');
                                    grid.appendChild(tile);
                                });
                        });
                    });
                })
                .catch(function () {
                    showEmpty(BF.i18n.t('media.error.media'));
                });
        } else if (type === 'files') {
            var query = panels.fileSearch.value.trim();
            BF.api
                .listChatAttachments(chatId, 4, 0, 30, query)
                .then(function (data) {
                    if (!current()) return;
                    var files = data.attachments || [];
                    if (files.length === 0) {
                        showEmpty(BF.i18n.t(query ? 'media.notFound.files' : 'media.empty.files'));
                        return;
                    }
                    var list = document.createElement('div');
                    list.className = 'profile-file-list';
                    content.appendChild(list);
                    var chain = Promise.resolve();
                    files.forEach(function (item) {
                        chain = chain.then(function () {
                            var att = item.attachment;
                            if (!att || !att.fileId) return;
                            return BF.files.getFileUrls([att.fileId]).then(function (urls) {
                                if (!current()) return;
                                var fileUrl = urls[0] ? urls[0].url : '#';
                                var el = document.createElement('a');
                                el.className = 'profile-file-item';
                                el.href = fileUrl;
                                el.target = '_blank';
                                BF.files.bindResilientLink(el, att.fileId);
                                el.rel = 'noopener';
                                var icon = document.createElement('span');
                                icon.textContent = '\u{1F4C4}';
                                el.appendChild(icon);
                                el.appendChild(
                                    document.createTextNode(' ' + (att.fileName || BF.i18n.t('attachment.file')))
                                );
                                list.appendChild(el);
                            });
                        });
                    });
                })
                .catch(function () {
                    showEmpty(BF.i18n.t('media.error.files'));
                });
        } else if (type === 'audio' || type === 'voice') {
            var attType = type === 'audio' ? 5 : 6;
            var emptyText = BF.i18n.t(type === 'audio' ? 'media.empty.audio' : 'media.empty.voice');
            BF.api
                .listChatAttachments(chatId, attType, 0, 30)
                .then(function (data) {
                    if (!current()) return;
                    var items = data.attachments || [];
                    if (items.length === 0) {
                        showEmpty(emptyText);
                        return;
                    }
                    var list = document.createElement('div');
                    list.className = 'profile-file-list';
                    content.appendChild(list);
                    var chain = Promise.resolve();
                    items.forEach(function (item) {
                        chain = chain.then(function () {
                            var att = item.attachment;
                            if (!att || !att.fileId) return;
                            return BF.files.getFileUrls([att.fileId]).then(function (urls) {
                                if (!current()) return;
                                var fileUrl = urls[0] ? urls[0].url : '';
                                if (!fileUrl) return;
                                var el = document.createElement('div');
                                el.className = 'profile-audio-item';
                                if (type === 'audio') {
                                    var nm = document.createElement('div');
                                    nm.className = 'profile-audio-name';
                                    nm.textContent = att.fileName || BF.i18n.t('attachment.audio');
                                    el.appendChild(nm);
                                }
                                var audio = document.createElement('audio');
                                audio.controls = true;
                                audio.preload = 'none';
                                audio.src = fileUrl;
                                el.appendChild(audio);
                                list.appendChild(el);
                            });
                        });
                    });
                })
                .catch(function () {
                    showEmpty(BF.i18n.t('media.error.attachments'));
                });
        }
    }

    function init(options) {
        deps = options;
    }

    window.BF.chatMedia = { init: init, createPanels: createPanels, setTabActive: setTabActive, render: render };
})();
