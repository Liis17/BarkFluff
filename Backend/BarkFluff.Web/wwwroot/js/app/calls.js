/**
 * Сигнализация звонков (BarkFluff.Calls) для веб-клиента.
 *
 *  - SubscribeCallEvents: device-scope server-stream входящих/статусных событий звонка
 *    (по образцу realtime.js: backoff, age-timer, watchdog, visibility, refresh при коде 16).
 *  - Call-control: InitiateCall / AcceptCall / RejectCall / JoinCall / EndCall (unary через authCall).
 *  - Простая машина состояний одного активного звонка + событийная модель для UI-слоя.
 *
 * Этот модуль НЕ трогает DOM и НЕ работает с LiveKit — он только сигнализация и состояние.
 * UI-слой (calls-ui.js) подписывается на события и поднимает экран/медиа через LiveKit SDK.
 *
 * События (BF.calls.on):
 *   'incoming'      {callId, callerUserId, chatId, isGroup, mediaType}  — показать ринг
 *   'connect'       {callId, role, phase, livekitUrl, accessToken, mediaType, isGroup, chatId}
 *                                                                       — открыть экран и войти в комнату
 *   'peer_accepted' {callId, userId}                                    — собеседник принял (caller)
 *   'peer_rejected' {callId, userId}                                    — собеседник отклонил (caller)
 *   'ring_dismiss'  {callId}                                            — входящий больше не актуален
 *   'ended'         {callId, reason, durationSeconds, wasRinging}       — звонок завершён
 *   'member'        {callId, userId, action}                           — участник вошёл/вышел (группа)
 *
 * Requires: BF.clients, BF.metadata, window.proto.barkfluff.calls
 * Exposes: BF.calls
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    function callsProto() { return window.proto.barkfluff.calls; }

    // --- Стрим SubscribeCallEvents ---

    var eventsStream = null;
    var backoff = 2000;
    var INITIAL_BACKOFF = 2000;
    var MAX_BACKOFF = 30000;
    var STREAM_MAX_AGE = 180000;            // превентивный реконнект ниже прокси-таймаутов
    var STABLE_STREAM_THRESHOLD = 10000;
    var STREAM_INACTIVITY_THRESHOLD = 90000; // watchdog
    var WATCHDOG_INTERVAL = 30000;

    var ageTimer = null;
    var watchdogTimer = null;
    var openedAt = 0;
    var lastActivity = 0;
    var _started = false;

    // --- Машина состояний одного звонка ---
    // currentCall: { callId, role:'caller'|'callee', status:'incoming'|'active',
    //                chatId, isGroup, mediaType, callerUserId, livekitUrl, accessToken }
    var currentCall = null;

    // --- Событийная модель ---
    var listeners = {};
    function on(event, cb) { (listeners[event] = listeners[event] || []).push(cb); }
    function off(event, cb) {
        if (!listeners[event]) return;
        listeners[event] = listeners[event].filter(function (c) { return c !== cb; });
    }
    function emit(event, data) {
        var cbs = listeners[event];
        if (cbs) cbs.forEach(function (cb) { try { cb(data); } catch (e) { console.error(e); } });
    }

    // gRPC UNAUTHENTICATED == 16 → форс-рефреш токена перед реконнектом.
    function isAuthError(err) {
        if (!err) return false;
        if (err.code === 16) return true;
        var m = String(err.message || err.toString() || '');
        return /UNAUTHENTICATED|status code 16/i.test(m);
    }

    function getStreamToken(forceRefresh) {
        return forceRefresh ? BF.clients.refreshToken() : BF.clients.getValidToken();
    }

    // --- Обработка входящих CallEvent ---

    function handleIncoming(inc) {
        var callId = inc.getCallId();
        var chatId = inc.getChatId() || '';
        var isGroup = chatId.length > 0;
        var mediaType = inc.getMediaType();

        // Заняты другим активным звонком — автоматически отклоняем (busy).
        if (currentCall && currentCall.status === 'active' && currentCall.callId !== callId) {
            reject(callId);
            return;
        }
        if (currentCall && currentCall.callId === callId) return; // дубль

        currentCall = {
            callId: callId,
            role: 'callee',
            status: 'incoming',
            callerUserId: inc.getCallerUserId(),
            chatId: chatId,
            isGroup: isGroup,
            mediaType: mediaType
        };
        emit('incoming', {
            callId: callId,
            callerUserId: inc.getCallerUserId(),
            chatId: chatId,
            isGroup: isGroup,
            mediaType: mediaType
        });
    }

    function handleAccepted(a) {
        var callId = a.getCallId();
        if (!currentCall || currentCall.callId !== callId) return;
        if (currentCall.role === 'caller') {
            emit('peer_accepted', { callId: callId, userId: a.getAcceptedByUserId() });
        } else if (currentCall.status === 'incoming') {
            // Принято на другом моём устройстве — гасим здесь ринг.
            currentCall = null;
            emit('ring_dismiss', { callId: callId });
        }
    }

    function handleRejected(r) {
        var callId = r.getCallId();
        if (!currentCall || currentCall.callId !== callId) return;
        if (currentCall.role === 'caller') {
            emit('peer_rejected', { callId: callId, userId: r.getRejectedByUserId() });
            // Для 1-на-1 следом придёт 'ended' (reason=REJECTED) и снимет экран.
        } else if (currentCall.status === 'incoming') {
            // Отклонено на другом моём устройстве — гасим ринг.
            currentCall = null;
            emit('ring_dismiss', { callId: callId });
        }
    }

    function handleEnded(e) {
        var callId = e.getCallId();
        if (!currentCall || currentCall.callId !== callId) return;
        var wasRinging = currentCall.status === 'incoming';
        currentCall = null;
        emit('ended', {
            callId: callId,
            reason: e.getReason(),
            durationSeconds: e.getDurationSeconds(),
            wasRinging: wasRinging
        });
    }

    function handleMember(m) {
        emit('member', {
            callId: m.getCallId(),
            userId: m.getUserId(),
            action: m.getAction()
        });
    }

    function dispatchEvent(evt) {
        var EventCase = callsProto().CallEvent.EventCase;
        switch (evt.getEventCase()) {
            case EventCase.INCOMING: handleIncoming(evt.getIncoming()); break;
            case EventCase.ACCEPTED: handleAccepted(evt.getAccepted()); break;
            case EventCase.REJECTED: handleRejected(evt.getRejected()); break;
            case EventCase.ENDED:    handleEnded(evt.getEnded());       break;
            case EventCase.MEMBER:   handleMember(evt.getMember());     break;
            default: break;
        }
    }

    // --- Открытие/реконнект стрима (паттерн realtime.js) ---

    function subscribeCallEvents(forceRefresh) {
        getStreamToken(forceRefresh).then(function (token) {
            if (!token) return; // без токена realtime.js уже редиректит на '/'
            var meta = BF.metadata.build(token);
            var req = new (callsProto().SubscribeCallEventsRequest)();

            if (eventsStream) { try { eventsStream.cancel(); } catch (e) {} }
            eventsStream = BF.clients.calls.subscribeCallEvents(req, meta);
            openedAt = Date.now();
            lastActivity = Date.now();
            if (ageTimer) clearTimeout(ageTimer);
            ageTimer = setTimeout(function () {
                if (_started) subscribeCallEvents(false);
            }, STREAM_MAX_AGE);

            eventsStream.on('data', function (evt) {
                backoff = INITIAL_BACKOFF;
                lastActivity = Date.now();
                dispatchEvent(evt);
            });

            eventsStream.on('status', function (status) {
                lastActivity = Date.now();
                if (status && status.code === 0) backoff = INITIAL_BACKOFF;
            });

            eventsStream.on('error', function (err) {
                if (!_started) return;
                if (isAuthError(err)) {
                    setTimeout(function () { subscribeCallEvents(true); }, 0);
                } else {
                    setTimeout(function () { subscribeCallEvents(false); }, backoff);
                    backoff = Math.min(backoff * 2, MAX_BACKOFF);
                }
            });

            eventsStream.on('end', function () {
                if (Date.now() - openedAt > STABLE_STREAM_THRESHOLD) backoff = INITIAL_BACKOFF;
                if (_started) setTimeout(function () { subscribeCallEvents(false); }, backoff);
            });
        }, function () { /* нет токена — молча, сессию чинит realtime */ });
    }

    function checkActivity() {
        if (!_started || !eventsStream) return;
        if (Date.now() - lastActivity > STREAM_INACTIVITY_THRESHOLD) {
            console.warn('[calls] watchdog: call-events stream silent, reconnecting');
            subscribeCallEvents(false);
        }
    }

    function handleVisibilityChange() {
        if (document.visibilityState === 'visible' && _started) {
            BF.clients.getValidToken().then(function (token) {
                if (token && !eventsStream) subscribeCallEvents(false);
            });
        }
    }
    document.addEventListener('visibilitychange', handleVisibilityChange);

    // --- Жизненный цикл ---

    function start() {
        if (_started) return;
        _started = true;
        backoff = INITIAL_BACKOFF;
        subscribeCallEvents(false);
        if (watchdogTimer) clearInterval(watchdogTimer);
        watchdogTimer = setInterval(checkActivity, WATCHDOG_INTERVAL);
    }

    function stop() {
        _started = false;
        if (eventsStream) { try { eventsStream.cancel(); } catch (e) {} eventsStream = null; }
        if (ageTimer) { clearTimeout(ageTimer); ageTimer = null; }
        if (watchdogTimer) { clearInterval(watchdogTimer); watchdogTimer = null; }
        currentCall = null;
    }

    // --- Call-control (unary через authCall: токен + refresh + ретрай) ---

    function initiate(target, mediaType) {
        var req = new (callsProto().InitiateCallRequest)();
        var isGroup = !!(target && target.chatId);
        if (isGroup) req.setChatId(target.chatId);
        else req.setCalleeUserId(target.userId);
        req.setMediaType(mediaType);

        return BF.clients.authCall(
            BF.clients.calls.initiateCall.bind(BF.clients.calls), req
        ).then(function (resp) {
            var callId = resp.getCallId();
            currentCall = {
                callId: callId,
                role: 'caller',
                status: 'active',
                chatId: isGroup ? target.chatId : '',
                isGroup: isGroup,
                mediaType: mediaType,
                livekitUrl: resp.getLivekitUrl(),
                accessToken: resp.getAccessToken()
            };
            emit('connect', {
                callId: callId,
                role: 'caller',
                phase: 'ringing',
                livekitUrl: resp.getLivekitUrl(),
                accessToken: resp.getAccessToken(),
                mediaType: mediaType,
                isGroup: isGroup,
                chatId: currentCall.chatId
            });
            return resp;
        });
    }

    function accept(callId) {
        var req = new (callsProto().AcceptCallRequest)();
        req.setCallId(callId);
        var call = currentCall;
        return BF.clients.authCall(
            BF.clients.calls.acceptCall.bind(BF.clients.calls), req
        ).then(function (resp) {
            if (call && call.callId === callId) {
                call.status = 'active';
                call.livekitUrl = resp.getLivekitUrl();
                call.accessToken = resp.getAccessToken();
            }
            emit('connect', {
                callId: callId,
                role: 'callee',
                phase: 'connected',
                livekitUrl: resp.getLivekitUrl(),
                accessToken: resp.getAccessToken(),
                mediaType: call ? call.mediaType : callsProto().CallMediaType.CALL_MEDIA_VIDEO,
                isGroup: call ? call.isGroup : false,
                chatId: call ? call.chatId : ''
            });
            return resp;
        });
    }

    function reject(callId) {
        var req = new (callsProto().RejectCallRequest)();
        req.setCallId(callId);
        if (currentCall && currentCall.callId === callId) currentCall = null;
        return BF.clients.authCall(
            BF.clients.calls.rejectCall.bind(BF.clients.calls), req
        ).catch(function () { /* best-effort */ });
    }

    function join(callId, chatId, mediaType) {
        var req = new (callsProto().JoinCallRequest)();
        req.setCallId(callId);
        var media = mediaType != null ? mediaType : callsProto().CallMediaType.CALL_MEDIA_VIDEO;
        return BF.clients.authCall(
            BF.clients.calls.joinCall.bind(BF.clients.calls), req
        ).then(function (resp) {
            currentCall = {
                callId: callId,
                role: 'callee',
                status: 'active',
                chatId: chatId || '',
                isGroup: true,
                mediaType: media,
                livekitUrl: resp.getLivekitUrl(),
                accessToken: resp.getAccessToken()
            };
            emit('connect', {
                callId: callId,
                role: 'callee',
                phase: 'connected',
                livekitUrl: resp.getLivekitUrl(),
                accessToken: resp.getAccessToken(),
                mediaType: media,
                isGroup: true,
                chatId: chatId || ''
            });
            return resp;
        });
    }

    function end(callId) {
        var req = new (callsProto().EndCallRequest)();
        req.setCallId(callId);
        var wasRinging = !!(currentCall && currentCall.callId === callId && currentCall.status === 'incoming');
        if (currentCall && currentCall.callId === callId) currentCall = null;
        emit('ended', { callId: callId, reason: null, durationSeconds: 0, wasRinging: wasRinging, local: true });
        return BF.clients.authCall(
            BF.clients.calls.endCall.bind(BF.clients.calls), req
        ).catch(function () { /* best-effort */ });
    }

    function getCurrent() { return currentCall; }

    window.BF.calls = {
        on: on,
        off: off,
        start: start,
        stop: stop,
        initiate: initiate,
        accept: accept,
        reject: reject,
        join: join,
        end: end,
        getCurrent: getCurrent,
        // Тип медиа — резолвится лениво (бандл загружен до app-скриптов)
        get MediaType() {
            var t = callsProto().CallMediaType;
            return { AUDIO: t.CALL_MEDIA_AUDIO, VIDEO: t.CALL_MEDIA_VIDEO };
        }
    };
})();
