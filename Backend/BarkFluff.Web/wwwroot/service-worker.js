/* BarkFluff PWA shell and Firebase background notifications. */
importScripts('/pwa-config.js');
importScripts('/js/vendor/firebase-messaging-compat.bundle.js');

const CACHE_NAME = 'barkfluff-shell-v1';
const APP_SHELL = [
    '/', '/index.html', '/messenger', '/messenger.html', '/offline.html', '/manifest.webmanifest', '/favicon.ico',
    '/js/proto/barkfluff.bundle.js', '/js/vendor/livekit-client.bundle.js', '/js/vendor/hash-wasm.umd.min.js',
    '/js/vendor/firebase-messaging-compat.bundle.js',
    '/js/app/device.js', '/js/app/tokens.js', '/js/app/metadata.js', '/js/app/clients.js', '/js/app/utils.js',
    '/js/app/sound.js', '/js/app/api.js', '/js/app/drafts.js', '/js/app/privatechat.js', '/js/app/newchat.js',
    '/js/app/files.js', '/js/app/messages.js', '/js/app/realtime.js', '/js/app/calls.js', '/js/app/calls-ui.js',
    '/js/app/personalization.js', '/js/app/settings.js', '/js/app/attach.js', '/js/app/imageeditor.js',
    '/js/app/folders.js', '/js/app/pinned.js', '/js/app/push.js', '/js/app/main.js',
    '/icons/pwa-icon-192.png', '/icons/pwa-icon-512.png'
];

self.addEventListener('install', function (event) {
    event.waitUntil(caches.open(CACHE_NAME).then(function (cache) { return cache.addAll(APP_SHELL); }));
});

self.addEventListener('activate', function (event) {
    event.waitUntil(caches.keys().then(function (keys) {
        return Promise.all(keys.filter(function (key) {
            return key.startsWith('barkfluff-shell-') && key !== CACHE_NAME;
        }).map(function (key) { return caches.delete(key); }));
    }));
});

self.addEventListener('message', function (event) {
    if (event.data && event.data.type === 'SKIP_WAITING') self.skipWaiting();
});

self.addEventListener('fetch', function (event) {
    const request = event.request;
    const url = new URL(request.url);
    if (url.origin !== self.location.origin || request.method !== 'GET') return;
    if (url.pathname.startsWith('/barkfluff.') || url.pathname.startsWith('/api/') ||
        url.pathname === '/pwa-config.js' || url.pathname.startsWith('/legal/')) return;

    if (request.mode === 'navigate') {
        event.respondWith(fetch(request).catch(function () { return caches.match('/offline.html'); }));
        return;
    }

    if (url.pathname.startsWith('/js/') || url.pathname.startsWith('/icons/') ||
        url.pathname === '/favicon.ico' || url.pathname === '/manifest.webmanifest') {
        event.respondWith(caches.match(request, { ignoreSearch: true }).then(function (cached) {
            return cached || fetch(request);
        }));
    }
});

function notificationDetails(data) {
    const type = data.type || '';
    const name = data.sender_name || data.inviter_name || data.caller_name || 'BarkFluff';
    if (type === 'new_message') return { title: 'BarkFluff', body: name + ': новое сообщение', tag: 'bf-chat-' + data.chat_id };
    if (type === 'private_chat_invite') return { title: 'BarkFluff', body: name + ' приглашает в приватный чат', tag: 'bf-chat-' + data.chat_id };
    if (type === 'incoming_call') return { title: 'BarkFluff', body: 'Входящий звонок от ' + name, tag: 'bf-call-' + data.call_id };
    if (type === 'admin_broadcast') return { title: 'BarkFluff', body: 'Новое системное уведомление', tag: 'bf-admin-broadcast' };
    return null;
}

function isDismiss(data) {
    if (data.type === 'dismiss_chat_notifications') return 'bf-chat-' + data.chat_id;
    if (data.type === 'dismiss_call') return 'bf-call-' + data.call_id;
    return null;
}

function isVisibleChatOpen(client, chatId) {
    if (client.visibilityState !== 'visible') return false;
    const url = new URL(client.url);
    return (url.pathname === '/messenger' || url.pathname === '/messenger.html') &&
        url.searchParams.get('chat') === String(chatId);
}

function handlePushData(data) {
    data = data || {};
    const dismissTag = isDismiss(data);
    if (dismissTag) {
        return self.registration.getNotifications({ tag: dismissTag }).then(function (items) {
            items.forEach(function (item) { item.close(); });
        });
    }

    return self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (clients) {
        if (data.type === 'new_message' && clients.some(function (client) {
            return isVisibleChatOpen(client, data.chat_id);
        })) return;
        const details = notificationDetails(data);
        if (!details) return;
        if (data.avatar_url && /^https:\/\//i.test(data.avatar_url)) details.icon = data.avatar_url;
        details.data = { chatId: data.chat_id || '', callId: data.call_id || '' };
        return self.registration.showNotification(details.title, details);
    });
}

self.addEventListener('notificationclick', function (event) {
    event.notification.close();
    const data = event.notification.data || {};
    const target = new URL('/messenger', self.location.origin);
    if (data.chatId) target.searchParams.set('chat', data.chatId);
    if (data.callId) target.searchParams.set('call', data.callId);

    event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (clients) {
        const client = clients.find(function (item) {
            return new URL(item.url).pathname === '/messenger';
        });
        if (client) {
            client.postMessage({ type: 'bf-push-open', chatId: data.chatId, callId: data.callId });
            return client.focus();
        }
        return self.clients.openWindow(target.href);
    }));
});

if (self.BF_PWA_CONFIG && self.BF_PWA_CONFIG.firebase && self.firebase) {
    firebase.initializeApp(self.BF_PWA_CONFIG.firebase);
    firebase.messaging().onBackgroundMessage(function (payload) {
        return handlePushData(payload && payload.data);
    });
}
