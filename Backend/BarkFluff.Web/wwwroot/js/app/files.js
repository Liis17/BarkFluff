/**
 * File upload/download URL caching.
 * Requires: BF.api, BF.clients
 * Exposes: BF.files
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var urlCache = new Map();

    /**
     * Отдельный публичный адрес файлового HTTP ноды (Beacon `files_media_endpoint`):
     * загрузка и скачивание в обход CDN с его лимитом на размер файла.
     * Пусто — работаем по адресам, которые выдал сервер, как раньше.
     */
    function mediaOrigin() {
        var meta = BF.node.meta();
        return (meta && meta.filesMediaEndpoint) || '';
    }

    /** Подменяет хост в ссылке Files на отдельный файловый адрес ноды; путь сохраняется. */
    function mediaUrl(url) {
        var origin = mediaOrigin();
        if (!url || !origin) return url;
        try {
            var source = new URL(url);
            var target = new URL(origin);
            source.protocol = target.protocol;
            source.host = target.host;
            return source.toString();
        } catch (e) {
            return url;
        }
    }

    function cacheFile(f) {
        urlCache.set(f.fileId, {
            fileId: f.fileId,
            url: mediaUrl(f.url),
            previewUrl: mediaUrl(f.previewUrl)
        });
    }

    /**
     * Get download URLs for file IDs, using cache.
     * @param {string[]} fileIds
     * @returns {Promise<Object[]>} — array of {fileId, url, previewUrl}
     */
    function getFileUrls(fileIds) {
        var missing = fileIds.filter(function (id) { return !urlCache.has(id); });
        var p = missing.length > 0
            ? BF.api.getTempDownloadUrl(missing).then(function (data) {
                if (data && data.files) {
                    data.files.forEach(cacheFile);
                }
            })
            : Promise.resolve();

        return p.then(function () {
            return fileIds.map(function (id) { return urlCache.get(id); }).filter(Boolean);
        });
    }

    /**
     * Get a single file's URL info from cache.
     */
    function getCachedFileUrl(fileId) {
        return urlCache.get(fileId) || null;
    }

    /**
     * Куда слать multipart: на отдельный файловый адрес ноды, если он объявлен
     * (там тот же путь, что у Files за nginx), иначе — через шлюз своей ноды.
     */
    function uploadEndpoint(fileId) {
        var origin = mediaOrigin();
        return origin
            ? origin + '/web/upload/' + fileId
            : BF.node.origin() + '/api/files/upload/' + fileId;
    }

    /**
     * Upload a file: reserve upload slot → upload via REST.
     * Returns the file ID assigned by the server.
     * @param {File} file — browser File object
     * @param {number} uploadFileType — UploadFileType enum value
     * @param {Function} [onProgress] — function(percent) for bytes sent to the server
     * @returns {Promise<string>} — file ID
     */
    function uploadFile(file, uploadFileType, onProgress) {
        return BF.api.getUploadUrl(uploadFileType).then(function (data) {
            if (!data || !data.fileId) return Promise.reject(new Error('no_upload_url'));

            var formData = new FormData();
            formData.append('file', file, file.name);

            return new Promise(function (resolve, reject) {
                var xhr = new XMLHttpRequest();
                xhr.open('POST', uploadEndpoint(data.fileId));

                xhr.upload.addEventListener('progress', function (event) {
                    if (!event.lengthComputable || typeof onProgress !== 'function') return;
                    onProgress(Math.round(event.loaded / event.total * 100));
                });
                xhr.addEventListener('load', function () {
                    if (xhr.status < 200 || xhr.status >= 300) {
                        reject(new Error('upload_failed_' + xhr.status));
                        return;
                    }
                    var body;
                    try {
                        body = JSON.parse(xhr.responseText);
                    } catch (e) {
                        reject(new Error('upload_invalid_response'));
                        return;
                    }
                    if (typeof onProgress === 'function') onProgress(100);
                    resolve(body.fileId);
                });
                xhr.addEventListener('error', function () { reject(new Error('upload_failed_network')); });
                xhr.addEventListener('abort', function () { reject(new Error('upload_aborted')); });
                xhr.send(formData);
            });
        });
    }

    /**
     * Map MIME type → UploadFileType enum value.
     * Если MIME пустой или нераспознан (напр. application/octet-stream при drag-drop на Windows),
     * используем расширение файла как fallback.
     * @param {string} mimeType
     * @param {boolean} [asDocument] — force DOCUMENT type (кроме видео)
     * @param {string} [fileName] — для fallback по расширению
     */
    function getUploadFileType(mimeType, asDocument, fileName) {
        var mime = mimeType || '';
        var ext = (fileName || '').split('.').pop().toLowerCase();
        // MP4 открывается модальным окном в режиме «Файлы», но должен остаться
        // видео-вложением для встроенного превью и воспроизведения.
        var isVideo = mime.startsWith('video/') || ['mp4','mov','avi','mkv','webm','m4v'].indexOf(ext) !== -1;
        if (asDocument && !isVideo) return 5;
        if (mime.startsWith('image/gif')) return 4;
        if (mime.startsWith('image/')) return 2;
        if (mime.startsWith('video/')) return 3;
        if (mime.startsWith('audio/')) return 7;
        if (ext === 'gif') return 4;
        if (['jpg','jpeg','png','webp','bmp','avif','heic','heif','tiff','tif','svg','ico'].indexOf(ext) !== -1) return 2;
        if (['mp4','mov','avi','mkv','webm','m4v'].indexOf(ext) !== -1) return 3;
        if (['mp3','ogg','wav','aac','flac','m4a'].indexOf(ext) !== -1) return 7;
        return 5;
    }

    // --- Устойчивая загрузка медиа (рефреш протухших ссылок + плейсхолдер) ---

    // Векторная заглушка «не удалось загрузить» (data-URI, нейтральный серый — читается на любой теме).
    var BROKEN_MEDIA_SVG = (function () {
        var svg = '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="#9aa0a6" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">' +
            '<rect x="3" y="3" width="18" height="18" rx="2.5"/>' +
            '<circle cx="8.5" cy="8.5" r="1.5"/>' +
            '<path d="M21 15l-5-5L5 21"/>' +
            '</svg>';
        return 'data:image/svg+xml,' + encodeURIComponent(svg);
    })();

    function pickUrl(fd, preferPreview) {
        if (!fd) return '';
        return preferPreview ? (fd.previewUrl || fd.url || '') : (fd.url || fd.previewUrl || '');
    }

    /**
     * Удалить закешированный (возможно протухший) URL и перезапросить свежий
     * через getTempDownloadUrl. Обновляет кеш (другие элементы по тому же fileId
     * заберут свежую запись), возвращает {fileId,url,previewUrl}|null.
     */
    function refreshFileUrl(fileId) {
        if (!fileId) return Promise.resolve(null);
        urlCache.delete(fileId);
        return BF.api.getTempDownloadUrl([fileId]).then(function (data) {
            var fd = null;
            if (data && data.files) {
                data.files.forEach(cacheFile);
                fd = urlCache.get(fileId) || null;
            }
            return fd;
        }).catch(function () { return null; });
    }

    /**
     * Заменить медиа-элемент на векторную заглушку, чтобы не оставлять сломанный контрол.
     */
    function applyPlaceholder(el) {
        if (!el) return;
        el.setAttribute('data-bf-failed', '1');
        el.classList.add('bf-load-failed');
        var tag = el.tagName;
        if (tag === 'IMG') {
            el.src = BROKEN_MEDIA_SVG;
        } else if (tag === 'VIDEO') {
            try { el.pause(); } catch (e) {}
            el.removeAttribute('src');
            try { el.load(); } catch (e) {}
            el.setAttribute('poster', BROKEN_MEDIA_SVG);
        } else if (tag === 'AUDIO') {
            el.removeAttribute('src');
            try { el.load(); } catch (e) {}
        } else if (tag === 'A') {
            el.removeAttribute('href');
        }
    }

    /**
     * Загрузить background-image через Image, чтобы CSS-фон мог обработать ошибку
     * загрузки так же, как обычные media-элементы. При первой ошибке URL
     * обновляется по fileId; при повторной показывается заглушка.
     */
    function loadResilientBackground(el, fileId, preferPreview) {
        if (!el) return;

        var requestId = String(Number(el.getAttribute('data-bf-background-request') || '0') + 1);
        el.setAttribute('data-bf-background-request', requestId);
        el.setAttribute('data-bf-file-id', fileId || '');
        el.classList.remove('visible', 'bf-load-failed');
        el.style.backgroundImage = '';

        if (!fileId) return;

        function isCurrent() {
            return el.getAttribute('data-bf-background-request') === requestId &&
                el.getAttribute('data-bf-file-id') === fileId;
        }

        function preload(url) {
            if (!url) return Promise.resolve(false);
            return new Promise(function (resolve) {
                var image = new Image();
                image.onload = function () { resolve(true); };
                image.onerror = function () { resolve(false); };
                image.src = url;
            });
        }

        function apply(url) {
            if (!isCurrent()) return false;
            el.style.backgroundImage = 'url("' + url + '")';
            el.classList.add('visible');
            return true;
        }

        function showPlaceholder() {
            if (!isCurrent()) return;
            el.style.backgroundImage = 'url("' + BROKEN_MEDIA_SVG + '")';
            el.classList.add('visible', 'bf-load-failed');
        }

        function loadUrl(fileData, refreshed) {
            var url = pickUrl(fileData, preferPreview);
            return preload(url).then(function (loaded) {
                if (loaded) return apply(url);
                if (refreshed || !isCurrent()) return false;
                return refreshFileUrl(fileId).then(function (fresh) {
                    return loadUrl(fresh, true);
                });
            });
        }

        getFileUrls([fileId]).then(function (urls) {
            return loadUrl(urls[0] || null, false);
        }).then(function (loaded) {
            if (!loaded) showPlaceholder();
        }).catch(function () {
            if (!isCurrent()) return;
            refreshFileUrl(fileId).then(function (fresh) {
                return loadUrl(fresh, true);
            }).then(function (loaded) {
                if (!loaded) showPlaceholder();
            });
        });
    }

    function handleMediaError(el) {
        if (!el || el.getAttribute('data-bf-failed') === '1') return;
        var fileId = el.getAttribute('data-bf-file-id');
        if (!fileId) return; // нет fileId (напр. сброс src при закрытии оверлея) — игнорируем
        var preferPreview = el.getAttribute('data-bf-prefer-preview') === '1';
        if (el.getAttribute('data-bf-refreshed') === '1') {
            // повторная ошибка. Если рефреш ещё в полёте — ждём его результата; иначе плейсхолдер.
            if (el.getAttribute('data-bf-refreshing') === '1') return;
            applyPlaceholder(el);
            return;
        }
        el.setAttribute('data-bf-refreshed', '1');
        el.setAttribute('data-bf-refreshing', '1');
        refreshFileUrl(fileId).then(function (fd) {
            el.removeAttribute('data-bf-refreshing');
            if (el.getAttribute('data-bf-file-id') !== fileId) return; // элемент сброшен/переиспользован
            var fresh = pickUrl(fd, preferPreview);
            if (!fresh) { applyPlaceholder(el); return; }
            el.classList.remove('bf-load-failed');
            el.removeAttribute('data-bf-failed');
            el.src = fresh;
        });
    }

    /**
     * Навесить устойчивую загрузку на медиа-элемент (img/video/audio):
     * сохраняет fileId в data-атрибуте, по ошибке (404/протухание) рефрешит
     * ссылку и перезагружает; при повторной неудаче показывает плейсхолдер.
     * fileId читается из data-bf-file-id на момент ошибки (актуально для
     * переиспользуемых элементов вроде лайтбокса).
     */
    function bindResilientMedia(el, fileId, preferPreview) {
        if (!el) return;
        if (fileId) el.setAttribute('data-bf-file-id', fileId);
        el.setAttribute('data-bf-prefer-preview', preferPreview ? '1' : '0');
        el.addEventListener('error', function () { handleMediaError(el); });
    }

    /**
     * Устойчивая ссылка для документов (<a>): URL не загружается до клика,
     * поэтому рефрешим его лениво — префетч при наведении/фокусе, чтобы к клику
     * ссылка была свежей. Если свежий URL получить не удалось — заглушка.
     */
    function bindResilientLink(el, fileId) {
        if (!el || !fileId) return;
        el.setAttribute('data-bf-file-id', fileId);
        function prefetch() {
            if (el.getAttribute('data-bf-refreshed') === '1' ||
                el.getAttribute('data-bf-refreshing') === '1' ||
                el.getAttribute('data-bf-failed') === '1') return;
            el.setAttribute('data-bf-refreshing', '1');
            refreshFileUrl(fileId).then(function (fd) {
                el.removeAttribute('data-bf-refreshing');
                var fresh = fd && (fd.url || fd.previewUrl);
                if (!fresh) { applyPlaceholder(el); return; }
                el.setAttribute('data-bf-refreshed', '1');
                el.href = fresh;
            });
        }
        el.addEventListener('mouseenter', prefetch);
        el.addEventListener('focus', prefetch);
    }

    window.BF.files = {
        getFileUrls: getFileUrls,
        getCachedFileUrl: getCachedFileUrl,
        refreshFileUrl: refreshFileUrl,
        uploadFile: uploadFile,
        getUploadFileType: getUploadFileType,
        applyPlaceholder: applyPlaceholder,
        bindResilientMedia: bindResilientMedia,
        bindResilientLink: bindResilientLink,
        loadResilientBackground: loadResilientBackground,
        // Chat/Users/Messages встраивают в свои ответы уже готовые ссылки на Files
        // (picture, profilePicture, previewUrl…) — эти поля идут мимо getTempDownloadUrl,
        // поэтому api.js подменяет в них хост тем же способом отдельно.
        mediaUrl: mediaUrl,
        clearCache: function () { urlCache.clear(); }
    };
})();
