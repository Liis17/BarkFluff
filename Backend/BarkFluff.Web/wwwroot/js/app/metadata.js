/**
 * Build gRPC-Web metadata headers matching BarkFluff.Shared.Auth MetadataKeys.
 * All values except x-auth-token are base64-encoded (as expected by server interceptors).
 * Exposes: BF.metadata
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    function toBase64(str) {
        return btoa(unescape(encodeURIComponent(str)));
    }

    /**
     * @param {string} [token] — JWT access token (plain, not base64)
     * @returns {Object} metadata object for gRPC-Web call
     */
    function buildMetadata(token) {
        var dev = window.BF.device;
        var m = {
            'x-device-id': toBase64(dev.getDeviceId()),
            'x-device-name': toBase64(dev.getBrowserName()),
            'x-os-name': toBase64(dev.getOsName()),
            'x-app-name': toBase64(dev.getAppName()),
            'x-app-version': toBase64(dev.getAppVersion())
        };
        if (token) {
            m['x-auth-token'] = token;
        }
        return m;
    }

    window.BF.metadata = {
        build: buildMetadata
    };
})();
