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
     */
    function getUploadFileType(mimeType) {
        if (mimeType.startsWith('image/gif')) return 4;  // GIF
        if (mimeType.startsWith('image/')) return 2;     // IMAGE
        if (mimeType.startsWith('video/')) return 3;     // VIDEO
        if (mimeType.startsWith('audio/')) return 7;     // AUDIO
        return 5; // DOCUMENT
    }

    window.BF.files = {
        getFileUrls: getFileUrls,
        getCachedFileUrl: getCachedFileUrl,
        uploadFile: uploadFile,
        getUploadFileType: getUploadFileType,
        clearCache: function () { urlCache.clear(); }
    };
})();
