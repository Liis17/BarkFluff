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
     * Get download URLs for file IDs, using cache.
     * @param {string[]} fileIds
     * @returns {Promise<Object[]>} — array of {fileId, url, previewUrl}
     */
    function getFileUrls(fileIds) {
        var missing = fileIds.filter(function (id) { return !urlCache.has(id); });
        var p = missing.length > 0
            ? BF.api.getTempDownloadUrl(missing).then(function (data) {
                if (data && data.files) {
                    data.files.forEach(function (f) { urlCache.set(f.fileId, f); });
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
     * Принудительно перезапросить свежую ссылку по fileId (в обход кэша) и обновить кэш.
     * Presigned-ссылки протухают, если чат долго открыт.
     * @param {string} fileId
     * @returns {Promise<Object|null>} — {fileId, url, previewUrl} или null
     */
    function refreshFileUrl(fileId) {
        return BF.api.getTempDownloadUrl([fileId]).then(function (data) {
            var f = data && data.files && data.files[0];
            if (f) { urlCache.set(f.fileId, f); return f; }
            return null;
        });
    }

    /**
     * Upload a file: reserve upload slot → upload via REST.
     * Returns the file ID assigned by the server.
     * @param {File} file — browser File object
     * @param {number} uploadFileType — UploadFileType enum value
     * @returns {Promise<string>} — file ID
     */
    function uploadFile(file, uploadFileType) {
        return BF.api.getUploadUrl(uploadFileType).then(function (data) {
            if (!data || !data.fileId) return Promise.reject(new Error('no_upload_url'));

            var formData = new FormData();
            formData.append('file', file, file.name);

            return fetch('/api/files/upload/' + data.fileId, {
                method: 'POST',
                body: formData
            }).then(function (resp) {
                if (!resp.ok) return Promise.reject(new Error('upload_failed_' + resp.status));
                return resp.json().then(function (body) { return body.fileId; });
            });
        });
    }

    /**
     * Map MIME type → UploadFileType enum value.
     * Если MIME пустой или нераспознан (напр. application/octet-stream при drag-drop на Windows),
     * используем расширение файла как fallback.
     * @param {string} mimeType
     * @param {boolean} [asDocument] — force DOCUMENT type
     * @param {string} [fileName] — для fallback по расширению
     */
    function getUploadFileType(mimeType, asDocument, fileName) {
        if (asDocument) return 5;
        var mime = mimeType || '';
        if (mime.startsWith('image/gif')) return 4;
        if (mime.startsWith('image/')) return 2;
        if (mime.startsWith('video/')) return 3;
        if (mime.startsWith('audio/')) return 7;
        var ext = (fileName || '').split('.').pop().toLowerCase();
        if (ext === 'gif') return 4;
        if (['jpg','jpeg','png','webp','bmp','avif','heic','heif','tiff','tif','svg','ico'].indexOf(ext) !== -1) return 2;
        if (['mp4','mov','avi','mkv','webm','m4v'].indexOf(ext) !== -1) return 3;
        if (['mp3','ogg','wav','aac','flac','m4a'].indexOf(ext) !== -1) return 7;
        return 5;
    }

    window.BF.files = {
        getFileUrls: getFileUrls,
        getCachedFileUrl: getCachedFileUrl,
        refreshFileUrl: refreshFileUrl,
        uploadFile: uploadFile,
        getUploadFileType: getUploadFileType,
        clearCache: function () { urlCache.clear(); }
    };
})();
