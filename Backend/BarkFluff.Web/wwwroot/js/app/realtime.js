/**
 * Server-streaming gRPC-Web subscriptions for real-time updates.
 * Uses callback-style ClientReadableStream (gRPC-Web server-streaming over grpcwebtext).
 *
 * Features:
 *  - SubscribeNewMessages / SubscribeMessagesRead (Updates service)
 *  - SubscribeToOnlineStatus / ChangeUsersInSubscription (Onliner service)
 *  - Exponential backoff reconnection per stream
 *  - Page-visibility–aware reconnection (streams restore when tab becomes visible)
 *  - Connection-status tracking with 'connection_status' event
 *  - Keep-alive ping (SetOnlineStatus every 30 s)
 *
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

    var INITIAL_BACKOFF = 2000;
    var MAX_BACKOFF = 30000;
    var STREAM_MAX_AGE = 250000; // ms — превентивный реконнект до nginx 300s-таймаута

    var updatesAgeTimer = null;
    var readAgeTimer    = null;
    var onlineAgeTimer  = null;

    // Currently subscribed online user IDs (for reconnection)
    var currentOnlineUserIds = [];

    // Connection status: true when at least one core stream is alive
    var updatesConnected = false;
    var readConnected = false;
    var _lastEmittedStatus = null;

    // Whether startAll() was called (used for visibility-based reconnection)
    var _started = false;

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

    // --- Connection status helper ---

    function emitConnectionStatus() {
        var connected = updatesConnected || readConnected;
        if (connected !== _lastEmittedStatus) {
            _lastEmittedStatus = connected;
            emit('connection_status', { connected: connected });
        }
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
            if (updatesAgeTimer) clearTimeout(updatesAgeTimer);
            updatesAgeTimer = setTimeout(function () {
                if (_started) subscribeNewMessages();
            }, STREAM_MAX_AGE);

            updatesStream.on('data', function (evt) {
                updatesBackoff = INITIAL_BACKOFF;
                if (!updatesConnected) { updatesConnected = true; emitConnectionStatus(); }
                var msg = evt.getMessage();
                if (msg) {
                    emit('new_message', {
                        chatId: evt.getChatId(),
                        message: BF.api._mapMessage(msg)
                    });
                }
            });

            updatesStream.on('status', function (status) {
                if (status && status.code === 0 && !updatesConnected) {
                    updatesConnected = true;
                    emitConnectionStatus();
                }
            });

            updatesStream.on('error', function () {
                updatesConnected = false;
                emitConnectionStatus();
                if (_started) setTimeout(subscribeNewMessages, updatesBackoff);
                updatesBackoff = Math.min(updatesBackoff * 2, MAX_BACKOFF);
            });

            updatesStream.on('end', function () {
                updatesConnected = false;
                emitConnectionStatus();
                if (_started) setTimeout(subscribeNewMessages, updatesBackoff);
            });

            // Mark as connected optimistically after opening
            updatesConnected = true;
            emitConnectionStatus();
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
            if (readAgeTimer) clearTimeout(readAgeTimer);
            readAgeTimer = setTimeout(function () {
                if (_started) subscribeMessagesRead();
            }, STREAM_MAX_AGE);

            readStream.on('data', function (evt) {
                readBackoff = INITIAL_BACKOFF;
                if (!readConnected) { readConnected = true; emitConnectionStatus(); }
                emit('message_read', {
                    chatId: evt.getChatId(),
                    messageId: evt.getMessageId(),
                    readBy: evt.getNewReadByList()
                });
            });

            readStream.on('error', function () {
                readConnected = false;
                emitConnectionStatus();
                if (_started) setTimeout(subscribeMessagesRead, readBackoff);
                readBackoff = Math.min(readBackoff * 2, MAX_BACKOFF);
            });

            readStream.on('end', function () {
                readConnected = false;
                emitConnectionStatus();
                if (_started) setTimeout(subscribeMessagesRead, readBackoff);
            });

            readConnected = true;
            emitConnectionStatus();
        });
    }

    // --- Online status ---

    function subscribeOnline(userIds) {
        if (!userIds || userIds.length === 0) return;
        currentOnlineUserIds = userIds.slice();

        BF.clients.getValidToken().then(function (token) {
            if (!token) return;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.onliner;
            var req = new proto.SubscribeToOnlineStatusRequest();
            req.setUserIdsList(userIds);

            if (onlineStream) { try { onlineStream.cancel(); } catch (e) {} }
            onlineStream = BF.clients.onliner.subscribeToOnlineStatus(req, meta);
            if (onlineAgeTimer) clearTimeout(onlineAgeTimer);
            onlineAgeTimer = setTimeout(function () {
                if (_started && currentOnlineUserIds.length > 0) subscribeOnline(currentOnlineUserIds);
            }, STREAM_MAX_AGE);

            onlineStream.on('data', function (evt) {
                onlineBackoff = INITIAL_BACKOFF;
                emit('online_status', {
                    userId: evt.getUserId(),
                    status: evt.getStatus(),
                    lastSeen: evt.getLastSeen() ? evt.getLastSeen().toDate().getTime() : null
                });
            });

            onlineStream.on('error', function () {
                if (_started && currentOnlineUserIds.length > 0) {
                    setTimeout(function () { subscribeOnline(currentOnlineUserIds); }, onlineBackoff);
                }
                onlineBackoff = Math.min(onlineBackoff * 2, MAX_BACKOFF);
            });

            onlineStream.on('end', function () {
                if (_started && currentOnlineUserIds.length > 0) {
                    setTimeout(function () { subscribeOnline(currentOnlineUserIds); }, onlineBackoff);
                }
            });
        });
    }

    /**
     * Change the list of user IDs we're subscribed to for online status
     * without reopening the stream (uses ChangeUsersInSubscription RPC).
     * Falls back to full re-subscribe if the unary call fails.
     */
    function changeOnlineSubscription(userIds) {
        if (!userIds || userIds.length === 0) return;
        currentOnlineUserIds = userIds.slice();

        // If no active stream yet, open a new one
        if (!onlineStream) { subscribeOnline(userIds); return; }

        BF.clients.getValidToken().then(function (token) {
            if (!token) return;
            var proto = window.proto.barkfluff.onliner;
            var req = new proto.ChangeUsersInSubscriptionRequest();
            req.setUserIdsList(userIds);
            BF.clients.authCall(
                BF.clients.onliner.changeUsersInSubscription.bind(BF.clients.onliner),
                req
            ).catch(function () {
                // Fallback: reopen the stream with updated IDs
                subscribeOnline(userIds);
            });
        });
    }

    // --- Keep-alive ping ---

    function startKeepAlive() {
        if (keepAliveTimer) clearInterval(keepAliveTimer);
        BF.api.setOnlineStatus().catch(function () {});
        keepAliveTimer = setInterval(function () {
            BF.api.setOnlineStatus().catch(function () {});
        }, 3000);
    }

    function stopKeepAlive() {
        if (keepAliveTimer) { clearInterval(keepAliveTimer); keepAliveTimer = null; }
    }

    // --- Page visibility handling ---
    // When the user switches tabs the browser may throttle/kill streams.
    // On return we reconnect if streams dropped and refresh the token.

    function handleVisibilityChange() {
        if (document.visibilityState === 'visible' && _started) {
            // Refresh token in case it expired while tab was hidden
            BF.clients.getValidToken().then(function (token) {
                if (!token) return;
                if (!updatesConnected) subscribeNewMessages();
                if (!readConnected) subscribeMessagesRead();
                if (currentOnlineUserIds.length > 0 && !onlineStream) subscribeOnline(currentOnlineUserIds);
            });
            // Send keep-alive immediately
            BF.api.setOnlineStatus().catch(function () {});
            emit('tab_visible', {});
        }
    }

    document.addEventListener('visibilitychange', handleVisibilityChange);

    // --- Start/stop all subscriptions ---

    function startAll() {
        _started = true;
        updatesBackoff = INITIAL_BACKOFF;
        readBackoff = INITIAL_BACKOFF;
        subscribeNewMessages();
        subscribeMessagesRead();
        startKeepAlive();
    }

    function stopAll() {
        _started = false;
        if (updatesStream) { try { updatesStream.cancel(); } catch (e) {} updatesStream = null; }
        if (readStream) { try { readStream.cancel(); } catch (e) {} readStream = null; }
        if (onlineStream) { try { onlineStream.cancel(); } catch (e) {} onlineStream = null; }
        if (updatesAgeTimer) { clearTimeout(updatesAgeTimer); updatesAgeTimer = null; }
        if (readAgeTimer)    { clearTimeout(readAgeTimer);    readAgeTimer    = null; }
        if (onlineAgeTimer)  { clearTimeout(onlineAgeTimer);  onlineAgeTimer  = null; }
        updatesConnected = false;
        readConnected = false;
        _lastEmittedStatus = null;
        currentOnlineUserIds = [];
        stopKeepAlive();
        emitConnectionStatus();
    }

    // Reconnect all streams (e.g., after token refresh)
    function reconnect() {
        updatesBackoff = INITIAL_BACKOFF;
        readBackoff = INITIAL_BACKOFF;
        onlineBackoff = INITIAL_BACKOFF;
        subscribeNewMessages();
        subscribeMessagesRead();
        if (currentOnlineUserIds.length > 0) subscribeOnline(currentOnlineUserIds);
    }

    function isConnected() {
        return updatesConnected || readConnected;
    }

    window.BF.realtime = {
        on: on,
        off: off,
        startAll: startAll,
        stopAll: stopAll,
        reconnect: reconnect,
        subscribeOnline: subscribeOnline,
        changeOnlineSubscription: changeOnlineSubscription,
        isConnected: isConnected
    };
})();
