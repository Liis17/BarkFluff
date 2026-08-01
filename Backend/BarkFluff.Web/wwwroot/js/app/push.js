/**
 * Web Push + PWA lifecycle. Requires Firebase compat bundle, BF.api and public /pwa-config.js.
 * Exposes: BF.push
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var ENABLED_KEY = 'bf_web_push_enabled';
    var registration = null;
    var deferredInstallPrompt = null;
    var initialized = false;

    function config() {
        return window.BF_PWA_CONFIG || null;
    }

    function isSupported() {
        var c = config();
        return !!(window.isSecureContext && navigator.serviceWorker && window.Notification && window.firebase &&
            c && c.firebase && c.vapidKey);
    }

    function ensureFirebase() {
        var c = config();
        if (!c || !c.firebase || !window.firebase) throw new Error('push_not_configured');
        if (!firebase.apps || firebase.apps.length === 0) firebase.initializeApp(c.firebase);
        return firebase.messaging();
    }

    function registerServiceWorker() {
        if (!isSupported()) return Promise.resolve(null);
        if (registration) return Promise.resolve(registration);
        return navigator.serviceWorker.register('/service-worker.js', { scope: '/' }).then(function (reg) {
            registration = reg;
            watchForUpdate(reg);
            return reg;
        });
    }

    function watchForUpdate(reg) {
        function announceWaiting() {
            if (reg.waiting && navigator.serviceWorker.controller) {
                window.dispatchEvent(new CustomEvent('bf-pwa-update'));
            }
        }
        reg.addEventListener('updatefound', function () {
            var worker = reg.installing;
            if (!worker) return;
            worker.addEventListener('statechange', function () {
                if (worker.state === 'installed') announceWaiting();
            });
        });
        announceWaiting();
    }

    function enable() {
        if (!isSupported()) return Promise.resolve(false);
        return registerServiceWorker().then(function (reg) {
            return Notification.requestPermission().then(function (permission) {
                if (permission !== 'granted') return false;
                var c = config();
                return ensureFirebase().getToken({ vapidKey: c.vapidKey, serviceWorkerRegistration: reg });
            }).then(function (token) {
                if (!token) return false;
                return BF.api.setFirebaseToken(token, 2)
                    .then(function () { return BF.api.setNotificationsEnabled(true); })
                    .then(function () {
                        localStorage.setItem(ENABLED_KEY, '1');
                        return true;
                    });
            });
        }).catch(function (error) {
            console.warn('[push] enable failed', error);
            return false;
        });
    }

    function disable() {
        var serverClear = BF.api.setNotificationsEnabled(false)
            .catch(function () {})
            .then(function () { return BF.api.clearFirebaseToken().catch(function () {}); });
        var deleteToken = Promise.resolve().then(function () {
            if (!isSupported()) return;
            return ensureFirebase().deleteToken();
        }).catch(function () {});

        return Promise.all([serverClear, deleteToken]).then(function () {
            localStorage.removeItem(ENABLED_KEY);
            return true;
        });
    }

    function clearOnLogout() {
        if (localStorage.getItem(ENABLED_KEY) !== '1') return Promise.resolve();
        return disable();
    }

    function syncExistingToken(reg) {
        if (!isSupported() || localStorage.getItem(ENABLED_KEY) !== '1' || Notification.permission !== 'granted') return Promise.resolve();
        var c = config();
        return ensureFirebase().getToken({ vapidKey: c.vapidKey, serviceWorkerRegistration: reg })
            .then(function (token) {
                return token ? BF.api.setFirebaseToken(token, 2) : null;
            })
            .catch(function (error) {
                console.warn('[push] token refresh failed', error);
            });
    }

    function status() {
        if (!isSupported()) return 'unsupported';
        if (Notification.permission === 'denied') return 'denied';
        return localStorage.getItem(ENABLED_KEY) === '1' ? 'enabled' : 'disabled';
    }

    function init() {
        if (initialized) return;
        initialized = true;
        window.addEventListener('beforeinstallprompt', function (event) {
            event.preventDefault();
            deferredInstallPrompt = event;
            window.dispatchEvent(new CustomEvent('bf-pwa-install-available'));
        });
        if (localStorage.getItem(ENABLED_KEY) === '1' && window.Notification && Notification.permission === 'denied') {
            disable();
        } else {
            registerServiceWorker().then(syncExistingToken);
        }
        navigator.serviceWorker && navigator.serviceWorker.addEventListener('controllerchange', function () {
            window.location.reload();
        });
    }

    function install() {
        if (!deferredInstallPrompt) return Promise.resolve(false);
        var prompt = deferredInstallPrompt;
        deferredInstallPrompt = null;
        return prompt.prompt().then(function () { return prompt.userChoice; }).then(function (choice) {
            return choice.outcome === 'accepted';
        });
    }

    function applyUpdate() {
        if (!registration || !registration.waiting) return false;
        registration.waiting.postMessage({ type: 'SKIP_WAITING' });
        return true;
    }

    window.BF.push = {
        init: init,
        enable: enable,
        disable: disable,
        clearOnLogout: clearOnLogout,
        isSupported: isSupported,
        isEnabled: function () { return localStorage.getItem(ENABLED_KEY) === '1'; },
        status: status,
        canInstall: function () { return !!deferredInstallPrompt; },
        install: install,
        applyUpdate: applyUpdate
    };
})();
