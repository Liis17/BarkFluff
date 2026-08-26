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
     * Срок жизни кэша temp-ссылок. Сервер выдаёт их на 60 минут (TempFiles:ExpiresAt),
     * поэтому кэшируем меньше — иначе фоны/аватарки в долго открытой вкладке
     * грузятся по протухшему URL.
     */
    var URL_CACHE_TTL_MS = 30 * 60 * 1000;

    /**
     * Отдельный публичный адрес файлового HTTP ноды (Beacon `files_media_endpoint`):
     * загрузка и скачивание в обход CDN с его лимитом на размер файла.
     * Пусто — работаем по адресам, которые выдал сервер, как раньше.
     * На прокси-зеркале всегда пусто: отдельный файловый адрес ноды недоступен
     * напрямую, и загрузка, и медиа идут через сам прокси-хост.
     */
    function mediaOrigin() {
        if (BF.node.proxied && BF.node.proxied()) return '';
        var meta = BF.node.meta();
        return (meta && meta.filesMediaEndpoint) || '';
    }

    /**
     * Подменяет хост в ссылке Files на отдельный файловый адрес ноды; путь сохраняется.
     * На прокси-зеркале ссылка оборачивается в /media/{host}/... этого же origin:
     * файловые хосты ноды недоступны из РФ напрямую, их релеит прокси.
     */
    function mediaUrl(url) {
        if (!url) return url;
        if (BF.node.proxied && BF.node.proxied()) {
            try {
                var relay = new URL(url);
                return BF.node.origin() + '/media/' + relay.host + relay.pathname + relay.search;
            } catch (e) {
                return url;
            }
        }
        var origin = mediaOrigin();
        if (!origin) return url;
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
            previewUrl: mediaUrl(f.previewUrl),
            cachedAt: Date.now()
        });
    }

    function isCacheEntryFresh(entry) {
        return Boolean(entry) && (Date.now() - entry.cachedAt) < URL_CACHE_TTL_MS;
    }

    /**
     * Get download URLs for file IDs, using cache.
     * @param {string[]} fileIds
     * @returns {Promise<Object[]>} — array of {fileId, url, previewUrl}
     */
    function getFileUrls(fileIds) {
        var missing = fileIds.filter(function (id) {
            if (isCacheEntryFresh(urlCache.get(id))) return false;
            urlCache.delete(id);
            return true;
        });
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

    function statusEndpoint(fileId) {
        var origin = mediaOrigin();
        return origin
            ? origin + '/web/upload/' + fileId + '/status'
            : BF.node.origin() + '/api/files/upload/' + fileId + '/status';
    }

    function uploadError(kind, message, details) {
        var error = new Error(message || kind);
        error.kind = kind;
        error.retryable = kind === 'timeout' || kind === 'transport' || kind === 'http';
        error.outcomeUnknown = kind !== 'cancelled';
        if (details) Object.keys(details).forEach(function (key) { error[key] = details[key]; });
        return error;
    }

    function xhrRequest(method, url, body, options) {
        options = options || {};
        var wallTimeoutMs = options.wallTimeoutMs || (method === 'POST' ? 30 * 60 * 1000 : 12000);
        var stallTimeoutMs = options.stallTimeoutMs || 60000;

        return new Promise(function (resolve, reject) {
            var xhr = new XMLHttpRequest();
            var settled = false;
            var wallTimer = null;
            var stallTimer = null;
            var uploadComplete = method !== 'POST';
            var signal = options.signal;

            function cleanup() {
                if (wallTimer) clearTimeout(wallTimer);
                if (stallTimer) clearTimeout(stallTimer);
                if (signal) signal.removeEventListener('abort', onAbort);
            }

            function fail(error, abort) {
                if (settled) return;
                settled = true;
                cleanup();
                reject(error);
                if (abort) xhr.abort();
            }

            function succeed(value) {
                if (settled) return;
                settled = true;
                cleanup();
                resolve(value);
            }

            function armStallTimer() {
                if (method !== 'POST' || uploadComplete || settled) return;
                if (stallTimer) clearTimeout(stallTimer);
                stallTimer = setTimeout(function () {
                    fail(uploadError('timeout', 'upload_stalled', {
                        timeout: 'stall',
                        fileId: options.fileId || ''
                    }), true);
                }, stallTimeoutMs);
            }

            function onAbort() {
                fail(uploadError('cancelled', 'upload_cancelled', {
                    fileId: options.fileId || '',
                    outcomeUnknown: false
                }), true);
            }

            xhr.open(method, url);
            if (method === 'POST') {
                xhr.upload.addEventListener('progress', function (event) {
                    if (!event.lengthComputable) return;
                    var percent = Math.round(event.loaded / event.total * 100);
                    uploadComplete = event.total > 0 && event.loaded >= event.total;
                    if (uploadComplete && stallTimer) {
                        clearTimeout(stallTimer);
                        stallTimer = null;
                    } else {
                        armStallTimer();
                    }
                    if (typeof options.onProgress === 'function') options.onProgress(percent);
                });
            }
            xhr.addEventListener('load', function () {
                var response;
                try { response = JSON.parse(xhr.responseText || '{}'); } catch (_) { response = null; }
                if (xhr.status < 200 || xhr.status >= 300) {
                    fail(uploadError('http', 'upload_failed_' + xhr.status, {
                        status: xhr.status,
                        response: response,
                        state: response && response.state,
                        retryAfterSeconds: response && response.retryAfterSeconds,
                        fileId: options.fileId || ''
                    }));
                    return;
                }
                if (!response) {
                    fail(uploadError('transport', 'upload_invalid_response', { fileId: options.fileId || '' }));
                    return;
                }
                if (method === 'POST' && typeof options.onProgress === 'function') options.onProgress(100);
                succeed(response);
            });
            xhr.addEventListener('error', function () {
                fail(uploadError('transport', 'upload_failed_network', { fileId: options.fileId || '' }));
            });
            xhr.addEventListener('abort', function () {
                fail(uploadError('cancelled', 'upload_cancelled', {
                    fileId: options.fileId || '',
                    outcomeUnknown: false
                }));
            });

            wallTimer = setTimeout(function () {
                fail(uploadError('timeout', 'upload_timed_out', {
                    timeout: 'wall',
                    fileId: options.fileId || ''
                }), true);
            }, wallTimeoutMs);
            if (signal) {
                if (signal.aborted) { onAbort(); return; }
                signal.addEventListener('abort', onAbort, { once: true });
            }
            armStallTimer();
            xhr.send(body || null);
        });
    }

    function postFile(file, fileId, onProgress, options) {
        var formData = new FormData();
        formData.append('file', file, file.name);
        return xhrRequest('POST', uploadEndpoint(fileId), formData, Object.assign({}, options || {}, {
            fileId: fileId,
            onProgress: onProgress
        })).then(function (body) {
            if (!body.fileId) throw uploadError('transport', 'upload_invalid_response', { fileId: fileId });
            return body.fileId;
        });
    }

    function getUploadStatus(fileId, options) {
        return xhrRequest('GET', statusEndpoint(fileId), null, Object.assign({}, options || {}, {
            fileId: fileId
        }));
    }

    /**
     * Upload a file: reserve upload slot → upload via REST.
     * Returns the file ID assigned by the server.
     * @param {File} file — browser File object
     * @param {number} uploadFileType — UploadFileType enum value
     * @param {Function} [onProgress] — function(percent) for bytes sent to the server
     * @returns {Promise<string>} — file ID
     */
    function uploadFile(file, uploadFileType, onProgress, options) {
        options = options || {};
        return BF.api.getUploadUrl(uploadFileType, options.operationId, options.signal).then(function (data) {
            if (!data || !data.fileId) return Promise.reject(new Error('no_upload_url'));
            if (typeof options.onReserved === 'function') options.onReserved(data.fileId);
            return postFile(file, data.fileId, onProgress, options);
        });
    }

    function retryUpload(file, uploadFileType, descriptor, onProgress, options) {
        descriptor = descriptor || {};
        options = Object.assign({}, options || {}, { operationId: descriptor.operationId || '' });
        if (!descriptor.reservedFileId) return uploadFile(file, uploadFileType, onProgress, options);

        return getUploadStatus(descriptor.reservedFileId, options).then(function (status) {
            if (status.state === 'completed') return status.fileId;
            if (status.state === 'pending') {
                return postFile(file, descriptor.reservedFileId, onProgress, options);
            }
            throw uploadError('http', 'upload_processing', {
                status: 409,
                state: status.state || 'processing',
                retryAfterSeconds: status.retryAfterSeconds || 1,
                fileId: descriptor.reservedFileId,
                outcomeUnknown: true
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
    function loadResilientBackground(el, fileId, preferPreview, onResolved) {
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

        function notifyResolved(url) {
            if (typeof onResolved !== 'function') return;
            try { onResolved(url || ''); } catch (e) {}
        }

        function apply(url) {
            if (!isCurrent()) return false;
            el.style.backgroundImage = 'url("' + url + '")';
            el.classList.add('visible');
            notifyResolved(url);
            return true;
        }

        function showPlaceholder() {
            if (!isCurrent()) return;
            el.style.backgroundImage = 'url("' + BROKEN_MEDIA_SVG + '")';
            el.classList.add('visible', 'bf-load-failed');
            notifyResolved('');
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
        retryUpload: retryUpload,
        getUploadStatus: getUploadStatus,
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
