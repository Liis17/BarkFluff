/**
 * Token storage — sessionStorage for temp login, localStorage for persistent.
 * Exposes: BF.tokens
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var KEY = 'barkfluff_auth';
    var MODE_KEY = 'barkfluff_temp';

    function store() {
        return localStorage.getItem(MODE_KEY) === '1' ? sessionStorage : localStorage;
    }

    window.BF.tokens = {
        setTempMode: function (isTemp) {
            if (isTemp) localStorage.setItem(MODE_KEY, '1');
            else localStorage.removeItem(MODE_KEY);
        },

        save: function (data) {
            store().setItem(KEY, JSON.stringify(data));
        },

        get: function () {
            var raw = store().getItem(KEY);
            if (raw) return JSON.parse(raw);
            var fb = sessionStorage.getItem(KEY) || localStorage.getItem(KEY);
            return fb ? JSON.parse(fb) : null;
        },

        clear: function () {
            sessionStorage.removeItem(KEY);
            localStorage.removeItem(KEY);
            localStorage.removeItem(MODE_KEY);
        },

        getAccessToken: function () {
            var d = this.get();
            return d ? d.accessToken || null : null;
        },

        getRefreshToken: function () {
            var d = this.get();
            return d ? d.refreshToken || null : null;
        },

        isAccessExpired: function () {
            var d = this.get();
            if (!d || !d.accessTokenExpiration) return true;
            return Date.now() >= d.accessTokenExpiration - 30000;
        }
    };
})();
