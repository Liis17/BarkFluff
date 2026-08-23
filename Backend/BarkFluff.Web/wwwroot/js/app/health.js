/**
 * Liveness checks for the selected node.
 * Requires: BF.node, fetch, AbortController
 * Exposes: BF.health
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var REQUEST_TIMEOUT_MS = 8000;
    var SERVICES = [
        { id: 'web', name: 'Web', path: '/ping' },
        { id: 'beacon', name: 'Beacon', path: '/ping/beacon' },
        { id: 'identity', name: 'Identity', path: '/ping/identity' },
        { id: 'users', name: 'Users', path: '/ping/users' },
        { id: 'messages', name: 'Messages', path: '/ping/messages' },
        { id: 'files', name: 'Files', path: '/ping/files' },
        { id: 'updates', name: 'Updates', path: '/ping/updates' },
        { id: 'onliner', name: 'Onliner', path: '/ping/onliner' },
        { id: 'fast-auth', name: 'FastAuth', path: '/ping/fast-auth' },
        { id: 'calls', name: 'Calls', path: '/ping/calls' }
    ];

    function now() {
        return window.performance && typeof window.performance.now === 'function'
            ? window.performance.now()
            : Date.now();
    }

    function createHealthResult(service, startedAt, available, status) {
        return {
            id: service.id,
            name: service.name,
            available: available,
            elapsedMs: Math.max(0, Math.round(now() - startedAt)),
            status: status || 0
        };
    }

    function checkService(service) {
        var origin = BF.node.origin();
        var startedAt = now();
        var controller = typeof window.AbortController === 'function'
            ? new window.AbortController()
            : null;
        var timeoutId;
        var timeout = new Promise(function (resolve) {
            timeoutId = setTimeout(function () {
                if (controller) controller.abort();
                resolve(createHealthResult(service, startedAt, false, 0));
            }, REQUEST_TIMEOUT_MS);
        });

        var request;
        try {
            request = window.fetch(origin + service.path, {
                method: 'GET',
                cache: 'no-store',
                credentials: 'omit',
                signal: controller ? controller.signal : undefined
            });
        } catch (error) {
            request = Promise.reject(error);
        }

        var response = Promise.resolve(request).then(function (response) {
            return response.text().then(function (body) {
                return createHealthResult(service, startedAt,
                    response.status === 200 && body.trim() === 'pong', response.status);
            });
        }).catch(function () {
            return createHealthResult(service, startedAt, false, 0);
        });

        return Promise.race([response, timeout]).then(function (value) {
            clearTimeout(timeoutId);
            return value;
        });
    }

    function check() {
        return Promise.all(SERVICES.map(checkService));
    }

    window.BF.health = {
        check: check
    };
})();
