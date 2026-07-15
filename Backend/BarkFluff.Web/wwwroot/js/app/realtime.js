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
 *  - 'resync' event on any stream RE-open (backoff/watchdog/age-timer): сигнал UI
 *    дозагрузить пропущенное за время разрыва (server-streaming не реплеит)
 *  - Keep-alive ping (SetOnlineStatus every 3 s)
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
    var pinnedStream = null;
    var unpinnedStream = null;
    var allUnpinnedStream = null;
    var privateMsgStream = null;
    var onlineStream = null;
    var keepAliveTimer = null;

    var updatesBackoff = 2000;
    var readBackoff = 2000;
    var editedBackoff = 2000;
    var deletedBackoff = 2000;
    var pinnedBackoff = 2000;
    var unpinnedBackoff = 2000;
    var allUnpinnedBackoff = 2000;
    var privateMsgBackoff = 2000;
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
    var pinnedAgeTimer  = null;
    var unpinnedAgeTimer = null;
    var allUnpinnedAgeTimer = null;
    var privateMsgAgeTimer = null;
    var onlineAgeTimer  = null;

    var updatesOpenedAt = 0;
    var readOpenedAt    = 0;
    var editedOpenedAt  = 0;
    var deletedOpenedAt = 0;
    var pinnedOpenedAt  = 0;
    var unpinnedOpenedAt = 0;
    var allUnpinnedOpenedAt = 0;
    var privateMsgOpenedAt = 0;
    var onlineOpenedAt  = 0;

    // Время последней активности (data/status) — для watchdog'а.
    var updatesLastActivity = 0;
    var readLastActivity    = 0;
    var editedLastActivity  = 0;
    var deletedLastActivity = 0;
    var pinnedLastActivity  = 0;
    var unpinnedLastActivity = 0;
    var allUnpinnedLastActivity = 0;
    var privateMsgLastActivity = 0;
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

    // Был ли поток уже открыт хотя бы раз. Нужно, чтобы отличить первое открытие
    // (startAll) от ПЕРЕоткрытия (backoff/watchdog/age-timer/visibility). Любой реконнект
    // означает потенциальный разрыв, за время которого мог потеряться live-event
    // (server-streaming не реплеит пропущенное) — поэтому шлём 'resync', чтобы UI
    // дозагрузил актуальное состояние. Без этого пропавшие сообщения видны только
    // после ручного переоткрытия чата.
    var updatesEverOpened = false;
    var readEverOpened = false;
    var editedEverOpened = false;
    var deletedEverOpened = false;
    var privateMsgEverOpened = false;

    // Whether startAll() was called (used for visibility-based reconnection)
    var _started = false;

    // Event listeners: { event_name: [callback, ...] }
    var listeners = {};

    function emit(event, data) {
        var cbs = listeners[event];
        if (cbs) cbs.forEach(function (cb) { try { cb(data); } catch (e) { console.error(e); } });
    }

    // gRPC UNAUTHENTICATED == 16. Используется для форс-рефреша токена перед реконнектом стрима.
    function isAuthError(err) {
        if (!err) return false;
        if (err.code === 16) return true;
        // grpc-web иногда кладёт код в строковый message
        var m = String(err.message || err.toString() || '');
        return /UNAUTHENTICATED|status code 16/i.test(m);
    }

    var _redirecting = false;
    function handleNoToken() {
        if (_redirecting) return;
        _redirecting = true;
        try { stopAll(); } catch (e) {}
        try { BF.tokens && BF.tokens.clear && BF.tokens.clear(); } catch (e) {}
        window.location.href = '/';
    }

    // Если forceRefresh=true — не доверяем кешу, дёргаем refresh напрямую (на случай UNAUTHENTICATED
    // от уже открытого стрима, когда локально токен ещё «не помечен» как expired).
    function getStreamToken(forceRefresh) {
        return forceRefresh ? BF.clients.refreshToken() : BF.clients.getValidToken();
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

    function subscribeNewMessages(forceRefresh) {
        getStreamToken(forceRefresh).then(function (token) {
            if (!token) { handleNoToken(); return; }
            if (updatesEverOpened && _started) emit('resync', { source: 'new_messages' });
            updatesEverOpened = true;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeNewMessagesRequest();

            if (updatesStream) { try { updatesStream.cancel(); } catch (e) {} }
            updatesStream = BF.clients.updates.subscribeNewMessages(req, meta);
            updatesOpenedAt = Date.now();
            if (updatesAgeTimer) clearTimeout(updatesAgeTimer);
            updatesAgeTimer = setTimeout(function () {
                if (_started) subscribeNewMessages(false);
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

            updatesStream.on('error', function (err) {
                updatesConnected = false;
                emitConnectionStatus();
                if (!_started) return;
                if (isAuthError(err)) {
                    setTimeout(function () { subscribeNewMessages(true); }, 0);
                } else {
                    setTimeout(function () { subscribeNewMessages(false); }, updatesBackoff);
                    updatesBackoff = Math.min(updatesBackoff * 2, MAX_BACKOFF);
                }
            });

            updatesStream.on('end', function () {
                updatesConnected = false;
                emitConnectionStatus();
                // Штатное закрытие после длительной сессии — backoff не растим.
                if (Date.now() - updatesOpenedAt > STABLE_STREAM_THRESHOLD) {
                    updatesBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(function () { subscribeNewMessages(false); }, updatesBackoff);
            });

            // Mark as connected optimistically after opening
            updatesConnected = true;
            emitConnectionStatus();
        }, function () { handleNoToken(); });
    }

    // --- Updates: message read ---

    function subscribeMessagesRead(forceRefresh) {
        getStreamToken(forceRefresh).then(function (token) {
            if (!token) { handleNoToken(); return; }
            if (readEverOpened && _started) emit('resync', { source: 'messages_read' });
            readEverOpened = true;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeMessagesReadRequest();

            if (readStream) { try { readStream.cancel(); } catch (e) {} }
            readStream = BF.clients.updates.subscribeMessagesRead(req, meta);
            readOpenedAt = Date.now();
            if (readAgeTimer) clearTimeout(readAgeTimer);
            readAgeTimer = setTimeout(function () {
                if (_started) subscribeMessagesRead(false);
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

            readStream.on('error', function (err) {
                readConnected = false;
                emitConnectionStatus();
                if (!_started) return;
                if (isAuthError(err)) {
                    setTimeout(function () { subscribeMessagesRead(true); }, 0);
                } else {
                    setTimeout(function () { subscribeMessagesRead(false); }, readBackoff);
                    readBackoff = Math.min(readBackoff * 2, MAX_BACKOFF);
                }
            });

            readStream.on('end', function () {
                readConnected = false;
                emitConnectionStatus();
                if (Date.now() - readOpenedAt > STABLE_STREAM_THRESHOLD) {
                    readBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(function () { subscribeMessagesRead(false); }, readBackoff);
            });

            readConnected = true;
            emitConnectionStatus();
        }, function () { handleNoToken(); });
    }

    // --- Updates: message edited ---

    function subscribeMessagesEdited(forceRefresh) {
        getStreamToken(forceRefresh).then(function (token) {
            if (!token) { handleNoToken(); return; }
            if (editedEverOpened && _started) emit('resync', { source: 'messages_edited' });
            editedEverOpened = true;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeMessagesEditedRequest();

            if (editedStream) { try { editedStream.cancel(); } catch (e) {} }
            editedStream = BF.clients.updates.subscribeMessagesEdited(req, meta);
            editedOpenedAt = Date.now();
            if (editedAgeTimer) clearTimeout(editedAgeTimer);
            editedAgeTimer = setTimeout(function () {
                if (_started) subscribeMessagesEdited(false);
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

            editedStream.on('error', function (err) {
                editedConnected = false;
                emitConnectionStatus();
                if (!_started) return;
                if (isAuthError(err)) {
                    setTimeout(function () { subscribeMessagesEdited(true); }, 0);
                } else {
                    setTimeout(function () { subscribeMessagesEdited(false); }, editedBackoff);
                    editedBackoff = Math.min(editedBackoff * 2, MAX_BACKOFF);
                }
            });

            editedStream.on('end', function () {
                editedConnected = false;
                emitConnectionStatus();
                if (Date.now() - editedOpenedAt > STABLE_STREAM_THRESHOLD) {
                    editedBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(function () { subscribeMessagesEdited(false); }, editedBackoff);
            });

            editedConnected = true;
            emitConnectionStatus();
        }, function () { handleNoToken(); });
    }

    // --- Updates: message deleted ---

    function subscribeMessagesDeleted(forceRefresh) {
        getStreamToken(forceRefresh).then(function (token) {
            if (!token) { handleNoToken(); return; }
            if (deletedEverOpened && _started) emit('resync', { source: 'messages_deleted' });
            deletedEverOpened = true;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeMessagesDeletedRequest();

            if (deletedStream) { try { deletedStream.cancel(); } catch (e) {} }
            deletedStream = BF.clients.updates.subscribeMessagesDeleted(req, meta);
            deletedOpenedAt = Date.now();
            if (deletedAgeTimer) clearTimeout(deletedAgeTimer);
            deletedAgeTimer = setTimeout(function () {
                if (_started) subscribeMessagesDeleted(false);
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

            deletedStream.on('error', function (err) {
                deletedConnected = false;
                emitConnectionStatus();
                if (!_started) return;
                if (isAuthError(err)) {
                    setTimeout(function () { subscribeMessagesDeleted(true); }, 0);
                } else {
                    setTimeout(function () { subscribeMessagesDeleted(false); }, deletedBackoff);
                    deletedBackoff = Math.min(deletedBackoff * 2, MAX_BACKOFF);
                }
            });

            deletedStream.on('end', function () {
                deletedConnected = false;
                emitConnectionStatus();
                if (Date.now() - deletedOpenedAt > STABLE_STREAM_THRESHOLD) {
                    deletedBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(function () { subscribeMessagesDeleted(false); }, deletedBackoff);
            });

            deletedConnected = true;
            emitConnectionStatus();
        }, function () { handleNoToken(); });
    }

    // --- Updates: message pinned ---

    function subscribeMessagesPinned(forceRefresh) {
        getStreamToken(forceRefresh).then(function (token) {
            if (!token) { handleNoToken(); return; }
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeMessagesPinnedRequest();

            if (pinnedStream) { try { pinnedStream.cancel(); } catch (e) {} }
            pinnedStream = BF.clients.updates.subscribeMessagesPinned(req, meta);
            pinnedOpenedAt = Date.now();
            if (pinnedAgeTimer) clearTimeout(pinnedAgeTimer);
            pinnedAgeTimer = setTimeout(function () {
                if (_started) subscribeMessagesPinned(false);
            }, STREAM_MAX_AGE);

            pinnedLastActivity = Date.now();

            pinnedStream.on('data', function (evt) {
                pinnedBackoff = INITIAL_BACKOFF;
                pinnedLastActivity = Date.now();
                var pa = evt.getPinnedAt && evt.getPinnedAt();
                emit('message_pinned', {
                    chatId: evt.getChatId(),
                    messageId: evt.getMessageId(),
                    pinnerUserId: evt.getPinnerUserId(),
                    pinnedAt: pa ? pa.toDate().getTime() : null
                });
            });

            pinnedStream.on('status', function (status) {
                pinnedLastActivity = Date.now();
                if (status && status.code === 0) pinnedBackoff = INITIAL_BACKOFF;
            });

            pinnedStream.on('error', function (err) {
                if (!_started) return;
                if (isAuthError(err)) {
                    setTimeout(function () { subscribeMessagesPinned(true); }, 0);
                } else {
                    setTimeout(function () { subscribeMessagesPinned(false); }, pinnedBackoff);
                    pinnedBackoff = Math.min(pinnedBackoff * 2, MAX_BACKOFF);
                }
            });

            pinnedStream.on('end', function () {
                if (Date.now() - pinnedOpenedAt > STABLE_STREAM_THRESHOLD) {
                    pinnedBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(function () { subscribeMessagesPinned(false); }, pinnedBackoff);
            });
        }, function () { handleNoToken(); });
    }

    // --- Updates: message unpinned ---

    function subscribeMessagesUnpinned(forceRefresh) {
        getStreamToken(forceRefresh).then(function (token) {
            if (!token) { handleNoToken(); return; }
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeMessagesUnpinnedRequest();

            if (unpinnedStream) { try { unpinnedStream.cancel(); } catch (e) {} }
            unpinnedStream = BF.clients.updates.subscribeMessagesUnpinned(req, meta);
            unpinnedOpenedAt = Date.now();
            if (unpinnedAgeTimer) clearTimeout(unpinnedAgeTimer);
            unpinnedAgeTimer = setTimeout(function () {
                if (_started) subscribeMessagesUnpinned(false);
            }, STREAM_MAX_AGE);

            unpinnedLastActivity = Date.now();

            unpinnedStream.on('data', function (evt) {
                unpinnedBackoff = INITIAL_BACKOFF;
                unpinnedLastActivity = Date.now();
                emit('message_unpinned', {
                    chatId: evt.getChatId(),
                    messageId: evt.getMessageId()
                });
            });

            unpinnedStream.on('status', function (status) {
                unpinnedLastActivity = Date.now();
                if (status && status.code === 0) unpinnedBackoff = INITIAL_BACKOFF;
            });

            unpinnedStream.on('error', function (err) {
                if (!_started) return;
                if (isAuthError(err)) {
                    setTimeout(function () { subscribeMessagesUnpinned(true); }, 0);
                } else {
                    setTimeout(function () { subscribeMessagesUnpinned(false); }, unpinnedBackoff);
                    unpinnedBackoff = Math.min(unpinnedBackoff * 2, MAX_BACKOFF);
                }
            });

            unpinnedStream.on('end', function () {
                if (Date.now() - unpinnedOpenedAt > STABLE_STREAM_THRESHOLD) {
                    unpinnedBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(function () { subscribeMessagesUnpinned(false); }, unpinnedBackoff);
            });
        }, function () { handleNoToken(); });
    }

    // --- Updates: all messages unpinned ---

    function subscribeAllMessagesUnpinned(forceRefresh) {
        getStreamToken(forceRefresh).then(function (token) {
            if (!token) { handleNoToken(); return; }
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribeAllMessagesUnpinnedRequest();

            if (allUnpinnedStream) { try { allUnpinnedStream.cancel(); } catch (e) {} }
            allUnpinnedStream = BF.clients.updates.subscribeAllMessagesUnpinned(req, meta);
            allUnpinnedOpenedAt = Date.now();
            if (allUnpinnedAgeTimer) clearTimeout(allUnpinnedAgeTimer);
            allUnpinnedAgeTimer = setTimeout(function () {
                if (_started) subscribeAllMessagesUnpinned(false);
            }, STREAM_MAX_AGE);

            allUnpinnedLastActivity = Date.now();

            allUnpinnedStream.on('data', function (evt) {
                allUnpinnedBackoff = INITIAL_BACKOFF;
                allUnpinnedLastActivity = Date.now();
                emit('all_messages_unpinned', { chatId: evt.getChatId() });
            });

            allUnpinnedStream.on('status', function (status) {
                allUnpinnedLastActivity = Date.now();
                if (status && status.code === 0) allUnpinnedBackoff = INITIAL_BACKOFF;
            });

            allUnpinnedStream.on('error', function (err) {
                if (!_started) return;
                if (isAuthError(err)) {
                    setTimeout(function () { subscribeAllMessagesUnpinned(true); }, 0);
                } else {
                    setTimeout(function () { subscribeAllMessagesUnpinned(false); }, allUnpinnedBackoff);
                    allUnpinnedBackoff = Math.min(allUnpinnedBackoff * 2, MAX_BACKOFF);
                }
            });

            allUnpinnedStream.on('end', function () {
                if (Date.now() - allUnpinnedOpenedAt > STABLE_STREAM_THRESHOLD) {
                    allUnpinnedBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(function () { subscribeAllMessagesUnpinned(false); }, allUnpinnedBackoff);
            });
        }, function () { handleNoToken(); });
    }

    // --- Updates: новые сообщения приватных чатов (шифротекст; расшифровка в UI) ---

    function subscribePrivateMessages(forceRefresh) {
        getStreamToken(forceRefresh).then(function (token) {
            if (!token) { handleNoToken(); return; }
            if (privateMsgEverOpened && _started) emit('resync', { source: 'private_messages' });
            privateMsgEverOpened = true;
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.updates;
            var req = new proto.SubscribePrivateMessagesRequest();

            if (privateMsgStream) { try { privateMsgStream.cancel(); } catch (e) {} }
            privateMsgStream = BF.clients.updates.subscribePrivateMessages(req, meta);
            privateMsgOpenedAt = Date.now();
            if (privateMsgAgeTimer) clearTimeout(privateMsgAgeTimer);
            privateMsgAgeTimer = setTimeout(function () {
                if (_started) subscribePrivateMessages(false);
            }, STREAM_MAX_AGE);

            privateMsgLastActivity = Date.now();

            privateMsgStream.on('data', function (evt) {
                privateMsgBackoff = INITIAL_BACKOFF;
                privateMsgLastActivity = Date.now();
                var msg = evt.getMessage();
                if (msg) {
                    emit('private_message', {
                        chatId: evt.getChatId(),
                        message: BF.api._mapEncryptedMessage(msg)
                    });
                }
            });

            privateMsgStream.on('status', function (status) {
                privateMsgLastActivity = Date.now();
                if (status && status.code === 0) privateMsgBackoff = INITIAL_BACKOFF;
            });

            privateMsgStream.on('error', function (err) {
                if (!_started) return;
                if (isAuthError(err)) {
                    setTimeout(function () { subscribePrivateMessages(true); }, 0);
                } else {
                    setTimeout(function () { subscribePrivateMessages(false); }, privateMsgBackoff);
                    privateMsgBackoff = Math.min(privateMsgBackoff * 2, MAX_BACKOFF);
                }
            });

            privateMsgStream.on('end', function () {
                if (Date.now() - privateMsgOpenedAt > STABLE_STREAM_THRESHOLD) {
                    privateMsgBackoff = INITIAL_BACKOFF;
                }
                if (_started) setTimeout(function () { subscribePrivateMessages(false); }, privateMsgBackoff);
            });
        }, function () { handleNoToken(); });
    }

    // --- Online status ---

    function subscribeOnline(userIds, forceRefresh) {
        if (!userIds || userIds.length === 0) return;
        currentOnlineUserIds = userIds.slice();

        getStreamToken(forceRefresh).then(function (token) {
            if (!token) { handleNoToken(); return; }
            var meta = BF.metadata.build(token);
            var proto = window.proto.barkfluff.onliner;
            var req = new proto.SubscribeToOnlineStatusRequest();
            req.setUserIdsList(userIds);

            if (onlineStream) { try { onlineStream.cancel(); } catch (e) {} }
            onlineStream = BF.clients.onliner.subscribeToOnlineStatus(req, meta);
            onlineOpenedAt = Date.now();
            if (onlineAgeTimer) clearTimeout(onlineAgeTimer);
            onlineAgeTimer = setTimeout(function () {
                if (_started && currentOnlineUserIds.length > 0) subscribeOnline(currentOnlineUserIds, false);
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

            onlineStream.on('error', function (err) {
                if (!_started || currentOnlineUserIds.length === 0) return;
                if (isAuthError(err)) {
                    setTimeout(function () { subscribeOnline(currentOnlineUserIds, true); }, 0);
                } else {
                    setTimeout(function () { subscribeOnline(currentOnlineUserIds, false); }, onlineBackoff);
                    onlineBackoff = Math.min(onlineBackoff * 2, MAX_BACKOFF);
                }
            });

            onlineStream.on('end', function () {
                if (Date.now() - onlineOpenedAt > STABLE_STREAM_THRESHOLD) {
                    onlineBackoff = INITIAL_BACKOFF;
                }
                if (_started && currentOnlineUserIds.length > 0) {
                    setTimeout(function () { subscribeOnline(currentOnlineUserIds, false); }, onlineBackoff);
                }
            });
        }, function () { handleNoToken(); });
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
        if (pinnedStream && (now - pinnedLastActivity) > STREAM_INACTIVITY_THRESHOLD) {
            console.warn('[realtime] watchdog: messages-pinned stream silent, reconnecting');
            subscribeMessagesPinned();
        }
        if (unpinnedStream && (now - unpinnedLastActivity) > STREAM_INACTIVITY_THRESHOLD) {
            console.warn('[realtime] watchdog: messages-unpinned stream silent, reconnecting');
            subscribeMessagesUnpinned();
        }
        if (allUnpinnedStream && (now - allUnpinnedLastActivity) > STREAM_INACTIVITY_THRESHOLD) {
            console.warn('[realtime] watchdog: all-messages-unpinned stream silent, reconnecting');
            subscribeAllMessagesUnpinned();
        }
        if (privateMsgStream && (now - privateMsgLastActivity) > STREAM_INACTIVITY_THRESHOLD) {
            console.warn('[realtime] watchdog: private-messages stream silent, reconnecting');
            subscribePrivateMessages();
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

    // Переоткрывает только упавшие стримы (живые не трогаем). Токен обновляется
    // при необходимости внутри getValidToken.
    function reconnectDeadStreams() {
        BF.clients.getValidToken().then(function (token) {
            if (!token) return;
            if (!updatesConnected) subscribeNewMessages();
            if (!readConnected) subscribeMessagesRead();
            if (!editedConnected) subscribeMessagesEdited();
            if (!deletedConnected) subscribeMessagesDeleted();
            if (!pinnedStream) subscribeMessagesPinned();
            if (!unpinnedStream) subscribeMessagesUnpinned();
            if (!allUnpinnedStream) subscribeAllMessagesUnpinned();
            if (!privateMsgStream) subscribePrivateMessages();
            if (currentOnlineUserIds.length > 0 && !onlineStream) subscribeOnline(currentOnlineUserIds);
        });
    }

    function handleVisibilityChange() {
        if (document.visibilityState === 'visible' && _started) {
            reconnectDeadStreams();
            // Send keep-alive immediately
            BF.api.setOnlineStatus().catch(function () {});
            emit('tab_visible', {});
        }
    }

    document.addEventListener('visibilitychange', handleVisibilityChange);

    // Сеть вернулась (ОС сообщила) — не ждём до 30с backoff'а, реконнектим сразу.
    window.addEventListener('online', function () {
        if (!_started) return;
        reconnectDeadStreams();
        BF.api.setOnlineStatus().catch(function () {});
    });

    // --- Start/stop all subscriptions ---

    function startAll() {
        _started = true;
        updatesBackoff = INITIAL_BACKOFF;
        readBackoff = INITIAL_BACKOFF;
        editedBackoff = INITIAL_BACKOFF;
        deletedBackoff = INITIAL_BACKOFF;
        pinnedBackoff = INITIAL_BACKOFF;
        unpinnedBackoff = INITIAL_BACKOFF;
        allUnpinnedBackoff = INITIAL_BACKOFF;
        privateMsgBackoff = INITIAL_BACKOFF;
        subscribeNewMessages();
        subscribeMessagesRead();
        subscribeMessagesEdited();
        subscribeMessagesDeleted();
        subscribeMessagesPinned();
        subscribeMessagesUnpinned();
        subscribeAllMessagesUnpinned();
        subscribePrivateMessages();
        startKeepAlive();
        startWatchdog();
    }

    function stopAll() {
        _started = false;
        if (updatesStream) { try { updatesStream.cancel(); } catch (e) {} updatesStream = null; }
        if (readStream) { try { readStream.cancel(); } catch (e) {} readStream = null; }
        if (editedStream) { try { editedStream.cancel(); } catch (e) {} editedStream = null; }
        if (deletedStream) { try { deletedStream.cancel(); } catch (e) {} deletedStream = null; }
        if (pinnedStream) { try { pinnedStream.cancel(); } catch (e) {} pinnedStream = null; }
        if (unpinnedStream) { try { unpinnedStream.cancel(); } catch (e) {} unpinnedStream = null; }
        if (allUnpinnedStream) { try { allUnpinnedStream.cancel(); } catch (e) {} allUnpinnedStream = null; }
        if (privateMsgStream) { try { privateMsgStream.cancel(); } catch (e) {} privateMsgStream = null; }
        if (onlineStream) { try { onlineStream.cancel(); } catch (e) {} onlineStream = null; }
        if (updatesAgeTimer) { clearTimeout(updatesAgeTimer); updatesAgeTimer = null; }
        if (readAgeTimer)    { clearTimeout(readAgeTimer);    readAgeTimer    = null; }
        if (editedAgeTimer)  { clearTimeout(editedAgeTimer);  editedAgeTimer  = null; }
        if (deletedAgeTimer) { clearTimeout(deletedAgeTimer); deletedAgeTimer = null; }
        if (pinnedAgeTimer)  { clearTimeout(pinnedAgeTimer);  pinnedAgeTimer  = null; }
        if (unpinnedAgeTimer) { clearTimeout(unpinnedAgeTimer); unpinnedAgeTimer = null; }
        if (allUnpinnedAgeTimer) { clearTimeout(allUnpinnedAgeTimer); allUnpinnedAgeTimer = null; }
        if (privateMsgAgeTimer) { clearTimeout(privateMsgAgeTimer); privateMsgAgeTimer = null; }
        if (onlineAgeTimer)  { clearTimeout(onlineAgeTimer);  onlineAgeTimer  = null; }
        updatesConnected = false;
        readConnected = false;
        editedConnected = false;
        deletedConnected = false;
        updatesEverOpened = false;
        readEverOpened = false;
        editedEverOpened = false;
        deletedEverOpened = false;
        privateMsgEverOpened = false;
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
        pinnedBackoff = INITIAL_BACKOFF;
        unpinnedBackoff = INITIAL_BACKOFF;
        allUnpinnedBackoff = INITIAL_BACKOFF;
        privateMsgBackoff = INITIAL_BACKOFF;
        onlineBackoff = INITIAL_BACKOFF;
        subscribeNewMessages();
        subscribeMessagesRead();
        subscribeMessagesEdited();
        subscribeMessagesDeleted();
        subscribeMessagesPinned();
        subscribeMessagesUnpinned();
        subscribeAllMessagesUnpinned();
        subscribePrivateMessages();
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
