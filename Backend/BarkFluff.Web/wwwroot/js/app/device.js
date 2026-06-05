/**
 * Device identification and browser/OS detection.
 * Exposes: BF.device
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    function getDeviceId() {
        let id = localStorage.getItem('barkfluff_device_id');
        if (!id) {
            id = crypto.randomUUID();
            localStorage.setItem('barkfluff_device_id', id);
        }
        return id;
    }

    function getBrowserName() {
        const ua = navigator.userAgent;
        if (ua.includes('Firefox')) return 'Firefox';
        if (ua.includes('Edg/')) return 'Microsoft Edge';
        if (ua.includes('OPR/') || ua.includes('Opera')) return 'Opera';
        if (ua.includes('YaBrowser')) return 'Yandex Browser';
        if (ua.includes('Chrome')) return 'Chrome';
        if (ua.includes('Safari')) return 'Safari';
        return 'Unknown';
    }

    function getOsName() {
        const ua = navigator.userAgent;
        if (ua.includes('Windows NT 10')) return 'Windows 10/11';
        if (ua.includes('Windows')) return 'Windows';
        if (ua.includes('Mac OS X')) return 'macOS';
        if (ua.includes('Android')) return 'Android';
        if (ua.includes('iPhone') || ua.includes('iPad')) return 'iOS';
        if (ua.includes('Linux')) return 'Linux';
        return 'Unknown';
    }

    window.BF.device = {
        getDeviceId: getDeviceId,
        getBrowserName: getBrowserName,
        getOsName: getOsName,
        getAppName: function () { return 'BarkFluff Web'; },
        getAppVersion: function () { return '1.0.0'; }
    };
})();
