/**
 * Full-screen image and video viewer for the current chat.
 * Requires: BF.api, BF.files
 * Exposes: BF.mediaViewer
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var getCurrentChatId = null;
    var imageOverlay;
    var overlayImage;
    var overlayVideo;
    var overlayPrev;
    var overlayNext;
    var overlayFileToken = 0;
    var overlayOpenFrame = null;
    var overlayCloseTimer = null;
    var VIEWER_PAGE = 30;
    var viewerState = emptyViewerState();

    function $(selector) {
        return document.querySelector(selector);
    }

    function emptyViewerState() {
        return {
            chatId: null,
            items: [],
            index: -1,
            offset: 0,
            exhausted: false,
            totalCount: 0,
            loading: null
        };
    }

    function applyOverlaySrc(type, url) {
        overlayImage.removeAttribute('data-bf-refreshed');
        overlayImage.removeAttribute('data-bf-failed');
        overlayVideo.removeAttribute('data-bf-refreshed');
        overlayVideo.removeAttribute('data-bf-failed');
        if (type === 'video') {
            overlayImage.style.display = 'none';
            overlayVideo.style.display = 'block';
            overlayVideo.src = url || '';
            if (url) overlayVideo.play();
        } else {
            overlayVideo.style.display = 'none';
            overlayImage.style.display = 'block';
            overlayImage.src = url || '';
        }
    }

    function show(type, url, fileId) {
        if (overlayCloseTimer) {
            clearTimeout(overlayCloseTimer);
            overlayCloseTimer = null;
        }
        if (overlayOpenFrame) cancelAnimationFrame(overlayOpenFrame);
        if (fileId) {
            overlayImage.setAttribute('data-bf-file-id', fileId);
            overlayVideo.setAttribute('data-bf-file-id', fileId);
        } else {
            overlayImage.removeAttribute('data-bf-file-id');
            overlayVideo.removeAttribute('data-bf-file-id');
        }
        var token = ++overlayFileToken;
        applyOverlaySrc(type, url);
        overlayOpenFrame = requestAnimationFrame(function () {
            overlayOpenFrame = null;
            if (token === overlayFileToken) imageOverlay.classList.add('visible');
        });
        if (fileId) {
            BF.files.refreshFileUrl(fileId).then(function (file) {
                if (!file || token !== overlayFileToken) return;
                var fresh = type === 'video' ? file.url : file.url || file.previewUrl;
                var current = type === 'video' ? overlayVideo.src : overlayImage.src;
                if (fresh && fresh !== current) applyOverlaySrc(type, fresh);
            });
        }
        viewerInit(fileId);
    }

    function cleanup() {
        overlayCloseTimer = null;
        if (imageOverlay.classList.contains('visible')) return;
        overlayImage.removeAttribute('data-bf-file-id');
        overlayVideo.removeAttribute('data-bf-file-id');
        overlayImage.removeAttribute('data-bf-refreshed');
        overlayImage.removeAttribute('data-bf-failed');
        overlayVideo.removeAttribute('data-bf-refreshed');
        overlayVideo.removeAttribute('data-bf-failed');
        overlayImage.src = '';
        overlayVideo.pause();
        overlayVideo.src = '';
        viewerState.index = -1;
        if (overlayPrev) overlayPrev.hidden = true;
        if (overlayNext) overlayNext.hidden = true;
    }

    function close() {
        overlayFileToken++;
        if (overlayOpenFrame) {
            cancelAnimationFrame(overlayOpenFrame);
            overlayOpenFrame = null;
        }
        imageOverlay.classList.remove('visible');
        if (overlayCloseTimer) clearTimeout(overlayCloseTimer);
        overlayCloseTimer = setTimeout(cleanup, 120);
    }

    function viewerReset() {
        viewerState = emptyViewerState();
    }

    function viewerItem(attachment, type) {
        var file = attachment.attachment || {};
        return {
            type: type,
            fileId: file.fileId,
            attachmentId: attachment.attachmentId,
            messageId: attachment.messageId,
            sentAt: attachment.sentAt
        };
    }

    function viewerLoadMore() {
        if (viewerState.loading) return viewerState.loading;
        if (viewerState.exhausted) return Promise.resolve();
        var chatId = viewerState.chatId;
        var offset = viewerState.offset;
        var request = Promise.all([
            BF.api.listChatAttachments(chatId, 1, offset, VIEWER_PAGE),
            BF.api.listChatAttachments(chatId, 2, offset, VIEWER_PAGE)
        ])
            .then(function (result) {
                if (viewerState.chatId !== chatId) return;
                var images = result[0].attachments || [];
                var videos = result[1].attachments || [];
                var batch = images
                    .map(function (attachment) {
                        return viewerItem(attachment, 'image');
                    })
                    .concat(
                        videos.map(function (attachment) {
                            return viewerItem(attachment, 'video');
                        })
                    );
                var seen = {};
                viewerState.items.forEach(function (item) {
                    if (item.fileId) seen[item.fileId] = 1;
                });
                batch.forEach(function (item) {
                    if (item.fileId && !seen[item.fileId]) {
                        seen[item.fileId] = 1;
                        viewerState.items.push(item);
                    }
                });
                viewerState.items.sort(function (left, right) {
                    return (right.sentAt || 0) - (left.sentAt || 0);
                });
                viewerState.offset += VIEWER_PAGE;
                viewerState.totalCount = (result[0].totalCount || 0) + (result[1].totalCount || 0);
                if (images.length < VIEWER_PAGE && videos.length < VIEWER_PAGE) viewerState.exhausted = true;
            })
            .catch(function () {})
            .then(function () {
                viewerState.loading = null;
            });
        viewerState.loading = request;
        return request;
    }

    function updateNavigation() {
        if (overlayPrev) overlayPrev.hidden = viewerState.index <= 0;
        if (overlayNext)
            overlayNext.hidden = viewerState.index >= viewerState.items.length - 1 && viewerState.exhausted;
    }

    function viewerShow(index) {
        if (index < 0 || index >= viewerState.items.length) return;
        viewerState.index = index;
        var item = viewerState.items[index];
        var token = ++overlayFileToken;
        if (item.fileId) {
            overlayImage.setAttribute('data-bf-file-id', item.fileId);
            overlayVideo.setAttribute('data-bf-file-id', item.fileId);
        }
        var file = BF.files.getCachedFileUrl(item.fileId);
        var url = file && (item.type === 'video' ? file.url : file.url || file.previewUrl);
        if (url) applyOverlaySrc(item.type, url);
        BF.files.refreshFileUrl(item.fileId).then(function (freshFile) {
            if (!freshFile || token !== overlayFileToken) return;
            var fresh = item.type === 'video' ? freshFile.url : freshFile.url || freshFile.previewUrl;
            if (fresh) applyOverlaySrc(item.type, fresh);
        });
        updateNavigation();
        if (index >= viewerState.items.length - 2 && !viewerState.exhausted) viewerLoadMore();
    }

    function navigate(direction) {
        var nextIndex = viewerState.index + direction;
        if (nextIndex < 0) return;
        if (nextIndex >= viewerState.items.length) {
            if (viewerState.exhausted) return;
            viewerLoadMore().then(function () {
                if (viewerState.index + direction < viewerState.items.length) viewerShow(viewerState.index + direction);
            });
            return;
        }
        viewerShow(nextIndex);
    }

    function viewerInit(fileId) {
        var chatId = getCurrentChatId();
        if (!chatId || !fileId) {
            viewerState.index = -1;
            updateNavigation();
            return;
        }
        if (viewerState.chatId !== chatId) {
            viewerReset();
            viewerState.chatId = chatId;
        }
        (function locate() {
            for (var index = 0; index < viewerState.items.length; index++) {
                if (viewerState.items[index].fileId === fileId) {
                    viewerState.index = index;
                    updateNavigation();
                    if (index >= viewerState.items.length - 2 && !viewerState.exhausted) viewerLoadMore();
                    return;
                }
            }
            if (viewerState.exhausted) {
                viewerState.index = -1;
                updateNavigation();
                return;
            }
            viewerLoadMore().then(locate);
        })();
    }

    function init(options) {
        getCurrentChatId = options.getCurrentChatId;
        imageOverlay = $('#imageOverlay');
        overlayImage = $('#overlayImage');
        overlayVideo = $('#overlayVideo');
        overlayPrev = $('#overlayPrev');
        overlayNext = $('#overlayNext');

        BF.files.bindResilientMedia(overlayImage, null, false);
        BF.files.bindResilientMedia(overlayVideo, null, false);

        if (overlayPrev)
            overlayPrev.addEventListener('click', function (event) {
                event.stopPropagation();
                navigate(-1);
            });
        if (overlayNext)
            overlayNext.addEventListener('click', function (event) {
                event.stopPropagation();
                navigate(1);
            });
        document.addEventListener('keydown', function (event) {
            if (!imageOverlay.classList.contains('visible')) return;
            if (event.key === 'ArrowLeft') {
                event.preventDefault();
                navigate(-1);
            } else if (event.key === 'ArrowRight') {
                event.preventDefault();
                navigate(1);
            }
        });
        imageOverlay.addEventListener('click', function (event) {
            if (event.target === overlayVideo) return;
            close();
        });
    }

    window.BF.mediaViewer = {
        init: init,
        show: show,
        close: close
    };
})();
