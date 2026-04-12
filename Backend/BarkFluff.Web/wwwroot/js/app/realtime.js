/**
 * Server-streaming gRPC-Web subscriptions for real-time updates.
 * Uses callback-style ClientReadableStream (gRPC-Web server-streaming over grpcwebtext).
 * Requires: BF.clients, BF.metadata, BF.api, window.proto
 * Exposes: BF.realtime
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var updatesStream = null;
    var readStream = null;
    var onlineStream = null;
    var keepAliveTimer = null;

    var updatesBackoff = 2000;
    var readBackoff = 2000;
    var onlineBackoff = 2000;

    // Event listeners: { event_name: [callback, ...] }
    var listeners = {};

    function emit(event, data) {
        var cbs = listeners[event];
        if (cbs) cbs.forEach(function (cb) { try { cb(data); } catch (e) { console.error(e); } });
    }

    function on(event, cb) {
        if (!listeners[event]) listeners[event] = [];
        listeners[event].push(cb);
    }

    function off(event, cb) {
        if (!listeners[event]) return;
        listeners[event] = listeners[event].filter(function (c) { return c !== cb; });
    }

    // --- Updates: new messages ---

    function subscribeNewMessages() {
        BF.clients.getValidToken().then(function (token) {
            if (!token) return;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeNewMessagesRequest();

            if (updatesStream) { try { updatesStream.cancel(); } catch (e) {} }
            updatesStream = BF.clients.updates.subscribeNewMessages(req, meta);

            updatesStream.on('data', function (evt) {
                updatesBackoff = 2000;
                var msg = evt.getMessage();
                if (msg) {
                    emit('new_message', {
                        chatId: evt.getChatId(),
                        message: BF.api._mapMessage(msg)
                    });
                }
            });

            updatesStream.on('error', function () {
                setTimeout(subscribeNewMessages, updatesBackoff);
                updatesBackoff = Math.min(updatesBackoff * 2, 30000);
            });

            updatesStream.on('end', function () {
                setTimeout(subscribeNewMessages, updatesBackoff);
            });
        });
    }

    // --- Updates: message read ---

    function subscribeMessagesRead() {
        BF.clients.getValidToken().then(function (token) {
            if (!token) return;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeMessagesReadRequest();

            if (readStream) { try { readStream.cancel(); } catch (e) {} }
            readStream = BF.clients.updates.subscribeMessagesRead(req, meta);

            readStream.on('data', function (evt) {
                readBackoff = 2000;
                emit('message_read', {
                    chatId: evt.getChatId(),
                    messageId: evt.getMessageId(),
                    readBy: evt.getNewReadByList()
                });
            });

            readStream.on('error', function () {
                setTimeout(subscribeMessagesRead, readBackoff);
                readBackoff = Math.min(readBackoff * 2, 30000);
            });

            readStream.on('end', function () {
                setTimeout(subscribeMessagesRead, readBackoff);
            });
        });
    }

    // --- Online status ---

    function subscribeOnline(userIds) {
        if (!userIds || userIds.length === 0) return;

        BF.clients.getValidToken().then(function (token) {
            if (!token) return;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.onliner;
            var req = new proto.SubscribeToOnlineStatusRequest();
            req.setUserIdsList(userIds);

            if (onlineStream) { try { onlineStream.cancel(); } catch (e) {} }
            onlineStream = BF.clients.onliner.subscribeToOnlineStatus(req, meta);

            onlineStream.on('data', function (evt) {
                onlineBackoff = 2000;
                emit('online_status', {
                    userId: evt.getUserId(),
                    status: evt.getStatus(),
                    lastSeen: evt.getLastSeen() ? evt.getLastSeen().toDate().getTime() : null
                });
            });

            onlineStream.on('error', function () {
                setTimeout(function () { subscribeOnline(userIds); }, onlineBackoff);
                onlineBackoff = Math.min(onlineBackoff * 2, 30000);
            });

            onlineStream.on('end', function () {
                setTimeout(function () { subscribeOnline(userIds); }, onlineBackoff);
            });
        });
    }

    // --- Keep-alive ping ---

    function startKeepAlive() {
        if (keepAliveTimer) clearInterval(keepAliveTimer);
        BF.api.setOnlineStatus().catch(function () {});
        keepAliveTimer = setInterval(function () {
            BF.api.setOnlineStatus().catch(function () {});
        }, 30000);
    }

    function stopKeepAlive() {
        if (keepAliveTimer) { clearInterval(keepAliveTimer); keepAliveTimer = null; }
    }

    // --- Start/stop all subscriptions ---

    function startAll() {
        subscribeNewMessages();
        subscribeMessagesRead();
        startKeepAlive();
    }

    function stopAll() {
        if (updatesStream) { try { updatesStream.cancel(); } catch (e) {} updatesStream = null; }
        if (readStream) { try { readStream.cancel(); } catch (e) {} readStream = null; }
        if (onlineStream) { try { onlineStream.cancel(); } catch (e) {} onlineStream = null; }
        stopKeepAlive();
    }

    // Reconnect all streams (e.g., after token refresh)
    function reconnect() {
        subscribeNewMessages();
        subscribeMessagesRead();
        // Online will be reconnected when subscribeOnline is called with new user IDs
    }

    window.BF.realtime = {
        on: on,
        off: off,
        startAll: startAll,
        stopAll: stopAll,
        reconnect: reconnect,
        subscribeOnline: subscribeOnline
    };
})();
