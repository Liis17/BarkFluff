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
    var editedStream = null;
    var deletedStream = null;
    var onlineStream = null;
    var keepAliveTimer = null;

    var updatesBackoff = 2000;
    var readBackoff = 2000;
    var editedBackoff = 2000;
    var deletedBackoff = 2000;
    var onlineBackoff = 2000;

    var INITIAL_BACKOFF = 2000;
    var MAX_BACKOFF = 30000;
    var STREAM_MAX_AGE = 180000; // ms — превентивный реконнект ниже любых прокси/nginx-таймаутов
    // Если поток прожил больше этого порога, считаем закрытие «штатным» и не растим backoff.
    var STABLE_STREAM_THRESHOLD = 10000;
    // Watchdog: если стрим не подал признаков жизни (data/status) дольше этого порога —
    // считаем его «чёрной дырой» (TCP-сокет ещё жив, но прокси молча дропнул) и форсим реконнект.
    var STREAM_INACTIVITY_THRESHOLD = 90000;
    var WATCHDOG_INTERVAL = 30000;

    var updatesAgeTimer = null;
    var readAgeTimer    = null;
    var editedAgeTimer  = null;
    var deletedAgeTimer = null;
    var onlineAgeTimer  = null;

    var updatesOpenedAt = 0;
    var readOpenedAt    = 0;
    var editedOpenedAt  = 0;
    var deletedOpenedAt = 0;
    var onlineOpenedAt  = 0;

    // Время последней активности (data/status) — для watchdog'а.
    var updatesLastActivity = 0;
    var readLastActivity    = 0;
    var editedLastActivity  = 0;
    var deletedLastActivity = 0;
    var onlineLastActivity  = 0;
    var watchdogTimer = null;

    // Currently subscribed online user IDs (for reconnection)
    var currentOnlineUserIds = [];

    // Connection status: true when at least one core stream is alive
    var updatesConnected = false;
    var readConnected = false;
    var editedConnected = false;
    var deletedConnected = false;
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
        var connected = updatesConnected || readConnected || editedConnected || deletedConnected;
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
            updatesOpenedAt = Date.now();
            if (updatesAgeTimer) clearTimeout(updatesAgeTimer);
            updatesAgeTimer = setTimeout(function () {
                if (_started) subscribeNewMessages();
            }, STREAM_MAX_AGE);

            updatesLastActivity = Date.now();

            updatesStream.on('data', function (evt) {
                updatesBackoff = INITIAL_BACKOFF;
                updatesLastActivity = Date.now();
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
                updatesLastActivity = Date.now();
                if (status && status.code === 0) {
                    updatesBackoff = INITIAL_BACKOFF;
                    if (!updatesConnected) { updatesConnected = true; emitConnectionStatus(); }
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
                // Штатное закрытие после длительной сессии — backoff не растим.
                if (Date.now() - updatesOpenedAt > STABLE_STREAM_THRESHOLD) {
                    updatesBackoff = INITIAL_BACKOFF;
                }
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
            readOpenedAt = Date.now();
            if (readAgeTimer) clearTimeout(readAgeTimer);
            readAgeTimer = setTimeout(function () {
                if (_started) subscribeMessagesRead();
            }, STREAM_MAX_AGE);

            readLastActivity = Date.now();

            readStream.on('data', function (evt) {
                readBackoff = INITIAL_BACKOFF;
                readLastActivity = Date.now();
                if (!readConnected) { readConnected = true; emitConnectionStatus(); }
                emit('message_read', {
                    chatId: evt.getChatId(),
                    messageId: evt.getMessageId(),
                    readBy: evt.getNewReadByList()
                });
            });

            readStream.on('status', function (status) {
                readLastActivity = Date.now();
                if (status && status.code === 0) {
                    readBackoff = INITIAL_BACKOFF;
                    if (!readConnected) { readConnected = true; emitConnectionStatus(); }
                }
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
                if (Date.now() - readOpenedAt > STABLE_STREAM_THRESHOLD) {
                    readBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(subscribeMessagesRead, readBackoff);
            });

            readConnected = true;
            emitConnectionStatus();
        });
    }

    // --- Updates: message edited ---

    function subscribeMessagesEdited() {
        BF.clients.getValidToken().then(function (token) {
            if (!token) return;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeMessagesEditedRequest();

            if (editedStream) { try { editedStream.cancel(); } catch (e) {} }
            editedStream = BF.clients.updates.subscribeMessagesEdited(req, meta);
            editedOpenedAt = Date.now();
            if (editedAgeTimer) clearTimeout(editedAgeTimer);
            editedAgeTimer = setTimeout(function () {
                if (_started) subscribeMessagesEdited();
            }, STREAM_MAX_AGE);

            editedLastActivity = Date.now();

            editedStream.on('data', function (evt) {
                editedBackoff = INITIAL_BACKOFF;
                editedLastActivity = Date.now();
                if (!editedConnected) { editedConnected = true; emitConnectionStatus(); }
                var msg = evt.getMessage();
                if (msg) {
                    emit('message_edited', {
                        chatId: evt.getChatId(),
                        message: BF.api._mapMessage(msg)
                    });
                }
            });

            editedStream.on('status', function (status) {
                editedLastActivity = Date.now();
                if (status && status.code === 0) {
                    editedBackoff = INITIAL_BACKOFF;
                    if (!editedConnected) { editedConnected = true; emitConnectionStatus(); }
                }
            });

            editedStream.on('error', function () {
                editedConnected = false;
                emitConnectionStatus();
                if (_started) setTimeout(subscribeMessagesEdited, editedBackoff);
                editedBackoff = Math.min(editedBackoff * 2, MAX_BACKOFF);
            });

            editedStream.on('end', function () {
                editedConnected = false;
                emitConnectionStatus();
                if (Date.now() - editedOpenedAt > STABLE_STREAM_THRESHOLD) {
                    editedBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(subscribeMessagesEdited, editedBackoff);
            });

            editedConnected = true;
            emitConnectionStatus();
        });
    }

    // --- Updates: message deleted ---

    function subscribeMessagesDeleted() {
        BF.clients.getValidToken().then(function (token) {
            if (!token) return;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeMessagesDeletedRequest();

            if (deletedStream) { try { deletedStream.cancel(); } catch (e) {} }
            deletedStream = BF.clients.updates.subscribeMessagesDeleted(req, meta);
            deletedOpenedAt = Date.now();
            if (deletedAgeTimer) clearTimeout(deletedAgeTimer);
            deletedAgeTimer = setTimeout(function () {
                if (_started) subscribeMessagesDeleted();
            }, STREAM_MAX_AGE);

            deletedLastActivity = Date.now();

            deletedStream.on('data', function (evt) {
                deletedBackoff = INITIAL_BACKOFF;
                deletedLastActivity = Date.now();
                if (!deletedConnected) { deletedConnected = true; emitConnectionStatus(); }
                var chatId = evt.getChatId();
                var messageId = evt.getMessageId();
                console.log('[realtime] message_deleted received', { chatId: chatId, messageId: messageId });
                emit('message_deleted', { chatId: chatId, messageId: messageId });
            });

            deletedStream.on('status', function (status) {
                deletedLastActivity = Date.now();
                if (status && status.code === 0) {
                    deletedBackoff = INITIAL_BACKOFF;
                    if (!deletedConnected) { deletedConnected = true; emitConnectionStatus(); }
                }
            });

            deletedStream.on('error', function () {
                deletedConnected = false;
                emitConnectionStatus();
                if (_started) setTimeout(subscribeMessagesDeleted, deletedBackoff);
                deletedBackoff = Math.min(deletedBackoff * 2, MAX_BACKOFF);
            });

            deletedStream.on('end', function () {
                deletedConnected = false;
                emitConnectionStatus();
                if (Date.now() - deletedOpenedAt > STABLE_STREAM_THRESHOLD) {
                    deletedBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(subscribeMessagesDeleted, deletedBackoff);
            });

            deletedConnected = true;
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
            onlineOpenedAt = Date.now();
            if (onlineAgeTimer) clearTimeout(onlineAgeTimer);
            onlineAgeTimer = setTimeout(function () {
                if (_started && currentOnlineUserIds.length > 0) subscribeOnline(currentOnlineUserIds);
            }, STREAM_MAX_AGE);

            onlineLastActivity = Date.now();

            onlineStream.on('data', function (evt) {
                onlineBackoff = INITIAL_BACKOFF;
                onlineLastActivity = Date.now();
                emit('online_status', {
                    userId: evt.getUserId(),
                    status: evt.getStatus(),
                    lastSeen: evt.getLastSeen() ? evt.getLastSeen().toDate().getTime() : null
                });
            });

            onlineStream.on('status', function (status) {
                onlineLastActivity = Date.now();
                if (status && status.code === 0) onlineBackoff = INITIAL_BACKOFF;
            });

            onlineStream.on('error', function () {
                if (_started && currentOnlineUserIds.length > 0) {
                    setTimeout(function () { subscribeOnline(currentOnlineUserIds); }, onlineBackoff);
                }
                onlineBackoff = Math.min(onlineBackoff * 2, MAX_BACKOFF);
            });

            onlineStream.on('end', function () {
                if (Date.now() - onlineOpenedAt > STABLE_STREAM_THRESHOLD) {
                    onlineBackoff = INITIAL_BACKOFF;
                }
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

    // --- Watchdog: реконнектим стримы, которые молчат дольше STREAM_INACTIVITY_THRESHOLD ---

    function checkStreamActivity() {
        if (!_started) return;
        var now = Date.now();
        if (updatesStream && (now - updatesLastActivity) > STREAM_INACTIVITY_THRESHOLD) {
            console.warn('[realtime] watchdog: new-messages stream silent for ' +
                Math.round((now - updatesLastActivity) / 1000) + 's, reconnecting');
            subscribeNewMessages();
        }
        if (readStream && (now - readLastActivity) > STREAM_INACTIVITY_THRESHOLD) {
            console.warn('[realtime] watchdog: messages-read stream silent, reconnecting');
            subscribeMessagesRead();
        }
        if (editedStream && (now - editedLastActivity) > STREAM_INACTIVITY_THRESHOLD) {
            console.warn('[realtime] watchdog: messages-edited stream silent, reconnecting');
            subscribeMessagesEdited();
        }
        if (deletedStream && (now - deletedLastActivity) > STREAM_INACTIVITY_THRESHOLD) {
            console.warn('[realtime] watchdog: messages-deleted stream silent, reconnecting');
            subscribeMessagesDeleted();
        }
        if (onlineStream && (now - onlineLastActivity) > STREAM_INACTIVITY_THRESHOLD) {
            if (currentOnlineUserIds.length > 0) {
                console.warn('[realtime] watchdog: online stream silent, reconnecting');
                subscribeOnline(currentOnlineUserIds);
            }
        }
    }

    function startWatchdog() {
        if (watchdogTimer) clearInterval(watchdogTimer);
        watchdogTimer = setInterval(checkStreamActivity, WATCHDOG_INTERVAL);
    }

    function stopWatchdog() {
        if (watchdogTimer) { clearInterval(watchdogTimer); watchdogTimer = null; }
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
                if (!editedConnected) subscribeMessagesEdited();
                if (!deletedConnected) subscribeMessagesDeleted();
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
        editedBackoff = INITIAL_BACKOFF;
        deletedBackoff = INITIAL_BACKOFF;
        subscribeNewMessages();
        subscribeMessagesRead();
        subscribeMessagesEdited();
        subscribeMessagesDeleted();
        startKeepAlive();
        startWatchdog();
    }

    function stopAll() {
        _started = false;
        if (updatesStream) { try { updatesStream.cancel(); } catch (e) {} updatesStream = null; }
        if (readStream) { try { readStream.cancel(); } catch (e) {} readStream = null; }
        if (editedStream) { try { editedStream.cancel(); } catch (e) {} editedStream = null; }
        if (deletedStream) { try { deletedStream.cancel(); } catch (e) {} deletedStream = null; }
        if (onlineStream) { try { onlineStream.cancel(); } catch (e) {} onlineStream = null; }
        if (updatesAgeTimer) { clearTimeout(updatesAgeTimer); updatesAgeTimer = null; }
        if (readAgeTimer)    { clearTimeout(readAgeTimer);    readAgeTimer    = null; }
        if (editedAgeTimer)  { clearTimeout(editedAgeTimer);  editedAgeTimer  = null; }
        if (deletedAgeTimer) { clearTimeout(deletedAgeTimer); deletedAgeTimer = null; }
        if (onlineAgeTimer)  { clearTimeout(onlineAgeTimer);  onlineAgeTimer  = null; }
        updatesConnected = false;
        readConnected = false;
        editedConnected = false;
        deletedConnected = false;
        _lastEmittedStatus = null;
        currentOnlineUserIds = [];
        stopKeepAlive();
        stopWatchdog();
        emitConnectionStatus();
    }

    // Reconnect all streams (e.g., after token refresh)
    function reconnect() {
        updatesBackoff = INITIAL_BACKOFF;
        readBackoff = INITIAL_BACKOFF;
        editedBackoff = INITIAL_BACKOFF;
        deletedBackoff = INITIAL_BACKOFF;
        onlineBackoff = INITIAL_BACKOFF;
        subscribeNewMessages();
        subscribeMessagesRead();
        subscribeMessagesEdited();
        subscribeMessagesDeleted();
        if (currentOnlineUserIds.length > 0) subscribeOnline(currentOnlineUserIds);
    }

    function isConnected() {
        return updatesConnected || readConnected || editedConnected || deletedConnected;
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
