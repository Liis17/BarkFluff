/**
 * UI-слой звонков: ринг-оверлей, экран активного звонка, медиа через LiveKit SDK.
 * Потребляет события BF.calls и управляет DOM + window.LivekitClient (Room).
 *
 * Самодостаточен: имя/аватар пользователя берёт через BF.api.getUser, myUserId — из JWT.
 * Requires: BF.calls, BF.api, BF.utils, BF.tokens, window.LivekitClient
 * Exposes: BF.callsUI (тонкая обёртка, в основном работает через события)
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    // --- DOM ---
    var ringOverlay, ringAvatar, ringName, ringSub, ringAccept, ringReject;
    var screenEl, gridEl, titleEl, timerEl, btnMic, btnCam, btnScreen, btnHangup;

    // --- LiveKit ---
    var room = null;
    var tiles = {};            // identity -> { el, avatarEl, labelEl, videoEl, audioEls }
    var micOn = false, camOn = false, screenOn = false;

    // --- Состояние UI ---
    var ringCallId = null;
    var activeCallId = null;
    var timerInterval = null;
    var callStartedAt = 0;

    // --- Рингтон (WebAudio, без ассета) ---
    var audioCtx = null, ringInterval = null;

    var userCache = {};

    function $(id) { return document.getElementById(id); }

    function getMyUserId() {
        try {
            var p = BF.utils.parseJwtPayload(BF.tokens.getAccessToken());
            return p ? Number(p['x-user-id']) : null;
        } catch (e) { return null; }
    }

    function resolveUser(userId) {
        if (userCache[userId]) return Promise.resolve(userCache[userId]);
        return BF.api.getUser(userId).then(function (d) {
            var user = d && d.user ? d.user : null;
            if (user) userCache[userId] = user;
            return user;
        }).catch(function () { return null; });
    }

    function userName(user, fallback) {
        if (!user) return fallback || 'Пользователь';
        var n = [user.firstName, user.lastName].filter(Boolean).join(' ');
        return n || user.username || fallback || 'Пользователь';
    }

    function escapeAttr(s) { return String(s).replace(/"/g, '&quot;'); }

    function fillAvatar(el, user, fallbackText) {
        var src = user && (user.profilePicturePreview || user.profilePicture);
        if (src) el.innerHTML = '<img src="' + escapeAttr(src) + '" alt="">';
        else el.textContent = (fallbackText || '?').charAt(0).toUpperCase();
    }

    // --- Рингтон ---
    function startRingtone() {
        try {
            if (!audioCtx) audioCtx = new (window.AudioContext || window.webkitAudioContext)();
            if (audioCtx.state === 'suspended') audioCtx.resume();
            stopRingtone();
            function beep() {
                var o = audioCtx.createOscillator();
                var g = audioCtx.createGain();
                o.type = 'sine'; o.frequency.value = 480;
                o.connect(g); g.connect(audioCtx.destination);
                var t = audioCtx.currentTime;
                g.gain.setValueAtTime(0.0001, t);
                g.gain.exponentialRampToValueAtTime(0.15, t + 0.05);
                g.gain.exponentialRampToValueAtTime(0.0001, t + 0.9);
                o.start(t); o.stop(t + 1.0);
            }
            beep();
            ringInterval = setInterval(beep, 2500);
        } catch (e) { /* autoplay/policy — ринг визуальный */ }
    }
    function stopRingtone() {
        if (ringInterval) { clearInterval(ringInterval); ringInterval = null; }
    }

    // --- Ринг-оверлей ---
    function showRing(d) {
        ringCallId = d.callId;
        ringName.textContent = d.isGroup ? 'Групповой звонок' : 'Входящий звонок';
        ringSub.textContent = (d.mediaType === BF.calls.MediaType.VIDEO ? 'Видео' : 'Аудио');
        ringAvatar.innerHTML = '';
        ringAvatar.textContent = d.isGroup ? '#' : '?';
        if (!d.isGroup && d.callerUserId) {
            resolveUser(d.callerUserId).then(function (user) {
                if (ringCallId !== d.callId) return;
                ringName.textContent = userName(user, 'Входящий звонок');
                fillAvatar(ringAvatar, user, ringName.textContent);
            });
        }
        ringOverlay.classList.add('visible');
        startRingtone();
    }
    function hideRing() {
        ringOverlay.classList.remove('visible');
        ringCallId = null;
        stopRingtone();
    }

    // --- Таймер ---
    function startTimer() {
        if (timerInterval) return;
        callStartedAt = Date.now();
        timerInterval = setInterval(function () {
            var s = Math.floor((Date.now() - callStartedAt) / 1000);
            var mm = Math.floor(s / 60), ss = s % 60;
            timerEl.textContent = mm + ':' + (ss < 10 ? '0' : '') + ss;
        }, 500);
    }
    function stopTimer() {
        if (timerInterval) { clearInterval(timerInterval); timerInterval = null; }
        timerEl.textContent = '';
    }

    // --- Плитки участников ---
    function ensureTile(participant) {
        var id = participant.identity;
        if (tiles[id]) return tiles[id];

        var el = document.createElement('div'); el.className = 'call-tile';
        var avatar = document.createElement('div'); avatar.className = 'call-tile-avatar';
        var label = document.createElement('div'); label.className = 'call-tile-label';
        label.textContent = participant.isLocal ? 'Вы' : ('#' + id);
        el.appendChild(avatar); el.appendChild(label);
        gridEl.appendChild(el);

        var tile = { el: el, avatarEl: avatar, labelEl: label, videoEl: null, audioEls: [] };
        tiles[id] = tile;

        var uid = Number(id);
        if (uid) {
            resolveUser(uid).then(function (user) {
                if (!tiles[id]) return;
                if (!participant.isLocal) label.textContent = userName(user, '#' + id);
                fillAvatar(avatar, user, participant.isLocal ? 'Вы' : label.textContent);
            });
        }
        return tile;
    }

    function removeTile(participant) {
        var tile = tiles[participant.identity];
        if (!tile) return;
        tile.audioEls.forEach(function (a) { try { a.remove(); } catch (e) {} });
        try { tile.el.remove(); } catch (e) {}
        delete tiles[participant.identity];
    }

    function setTileVideo(tile, videoEl) {
        if (tile.videoEl) { try { tile.videoEl.remove(); } catch (e) {} }
        videoEl.autoplay = true; videoEl.playsInline = true;
        tile.videoEl = videoEl;
        tile.el.insertBefore(videoEl, tile.avatarEl);
        tile.avatarEl.style.display = 'none';
    }
    function clearTileVideo(tile) {
        if (tile.videoEl) { try { tile.videoEl.remove(); } catch (e) {} tile.videoEl = null; }
        tile.avatarEl.style.display = '';
    }

    // --- LiveKit события ---
    function onTrackSubscribed(track, pub, participant) {
        var L = window.LivekitClient;
        var tile = ensureTile(participant);
        if (track.kind === L.Track.Kind.Video) {
            setTileVideo(tile, track.attach());
        } else if (track.kind === L.Track.Kind.Audio) {
            var a = track.attach();
            a.style.display = 'none';
            tile.el.appendChild(a);
            tile.audioEls.push(a);
        }
    }
    function onTrackUnsubscribed(track, pub, participant) {
        try { track.detach().forEach(function (el) { el.remove(); }); } catch (e) {}
        var L = window.LivekitClient;
        var tile = tiles[participant.identity];
        if (tile && track.kind === L.Track.Kind.Video) clearTileVideo(tile);
    }
    function onLocalTrackPublished(pub, participant) {
        var L = window.LivekitClient;
        if (pub.track && pub.track.kind === L.Track.Kind.Video) {
            var el = pub.track.attach(); el.muted = true;
            setTileVideo(ensureTile(participant), el);
        }
        // Локальное аудио НЕ воспроизводим (эхо).
    }
    function onLocalTrackUnpublished(pub, participant) {
        var L = window.LivekitClient;
        var tile = tiles[participant.identity];
        if (tile && pub.track && pub.track.kind === L.Track.Kind.Video) {
            try { pub.track.detach().forEach(function (el) { el.remove(); }); } catch (e) {}
            clearTileVideo(tile);
        }
    }

    function connectLiveKit(d) {
        var L = window.LivekitClient;
        if (!L) { console.error('[calls] LiveKit SDK не загружен'); return; }
        if (room) { try { room.disconnect(); } catch (e) {} room = null; }
        clearGrid();

        room = new L.Room({ adaptiveStream: true, dynacast: true });
        room.on(L.RoomEvent.TrackSubscribed, onTrackSubscribed);
        room.on(L.RoomEvent.TrackUnsubscribed, onTrackUnsubscribed);
        room.on(L.RoomEvent.LocalTrackPublished, onLocalTrackPublished);
        room.on(L.RoomEvent.LocalTrackUnpublished, onLocalTrackUnpublished);
        room.on(L.RoomEvent.ParticipantConnected, function (p) {
            ensureTile(p);
            startTimer(); // первый собеседник вошёл — звонок состоялся
        });
        room.on(L.RoomEvent.ParticipantDisconnected, function (p) { removeTile(p); });

        var wantVideo = d.mediaType === BF.calls.MediaType.VIDEO;

        room.connect(d.livekitUrl, d.accessToken).then(function () {
            ensureTile(room.localParticipant);
            var remotes = room.remoteParticipants || room.participants;
            if (remotes && remotes.forEach) remotes.forEach(function (p) { ensureTile(p); });
            return room.localParticipant.setMicrophoneEnabled(true);
        }).then(function () {
            micOn = true; updateCtl(btnMic, micOn);
            if (wantVideo) return room.localParticipant.setCameraEnabled(true);
        }).then(function () {
            if (wantVideo) { camOn = true; updateCtl(btnCam, camOn); }
        }).catch(function (e) {
            console.error('[calls] LiveKit connect failed:', e);
            BF.calls.end(d.callId);
        });
    }

    function clearGrid() {
        Object.keys(tiles).forEach(function (id) {
            tiles[id].audioEls.forEach(function (a) { try { a.remove(); } catch (e) {} });
            try { tiles[id].el.remove(); } catch (e) {}
        });
        tiles = {};
    }

    function updateCtl(btn, on) { if (btn) btn.classList.toggle('off', !on); }

    // --- Экран активного звонка ---
    function openScreen(d) {
        activeCallId = d.callId;
        titleEl.textContent = d.isGroup ? 'Групповой звонок' : 'Звонок';
        timerEl.textContent = d.role === 'caller' ? 'Вызов…' : '';
        micOn = false; camOn = false; screenOn = false;
        updateCtl(btnMic, false); updateCtl(btnCam, false); updateCtl(btnScreen, false);
        screenEl.classList.add('visible');
        connectLiveKit(d);
        if (d.role !== 'caller') startTimer();
    }

    function teardown() {
        stopTimer();
        stopRingtone();
        if (room) { try { room.disconnect(); } catch (e) {} room = null; }
        clearGrid();
        screenEl.classList.remove('visible');
        activeCallId = null;
        micOn = camOn = screenOn = false;
    }

    // --- Обработчики событий BF.calls ---
    function bindCallEvents() {
        BF.calls.on('incoming', function (d) {
            // Уже в звонке — BF.calls сам отклонит (busy); ринг не показываем.
            if (activeCallId) return;
            showRing(d);
        });
        BF.calls.on('connect', function (d) {
            hideRing();
            openScreen(d);
        });
        BF.calls.on('peer_accepted', function (d) {
            if (activeCallId === d.callId) { titleEl.textContent = titleEl.textContent.replace('Звонок', 'В разговоре'); startTimer(); }
        });
        BF.calls.on('peer_rejected', function (d) {
            if (activeCallId === d.callId) timerEl.textContent = 'Отклонён';
        });
        BF.calls.on('ring_dismiss', function (d) {
            if (ringCallId === d.callId) hideRing();
        });
        BF.calls.on('ended', function (d) {
            if (ringCallId === d.callId) { hideRing(); return; }
            if (activeCallId === d.callId || d.local) teardown();
        });
        BF.calls.on('member', function () { /* плитки ведёт LiveKit; событие справочное */ });
    }

    // --- Кнопки управления ---
    function bindControls() {
        ringAccept.addEventListener('click', function () {
            if (!ringCallId) return;
            var id = ringCallId;
            stopRingtone();
            BF.calls.accept(id).catch(function (e) { console.error('accept failed', e); hideRing(); });
        });
        ringReject.addEventListener('click', function () {
            if (!ringCallId) return;
            BF.calls.reject(ringCallId);
            hideRing();
        });
        btnHangup.addEventListener('click', function () {
            if (activeCallId) BF.calls.end(activeCallId);
            else teardown();
        });
        btnMic.addEventListener('click', function () {
            if (!room) return;
            room.localParticipant.setMicrophoneEnabled(!micOn).then(function () {
                micOn = !micOn; updateCtl(btnMic, micOn);
            });
        });
        btnCam.addEventListener('click', function () {
            if (!room) return;
            room.localParticipant.setCameraEnabled(!camOn).then(function () {
                camOn = !camOn; updateCtl(btnCam, camOn);
            });
        });
        btnScreen.addEventListener('click', function () {
            if (!room) return;
            room.localParticipant.setScreenShareEnabled(!screenOn).then(function () {
                screenOn = !screenOn; updateCtl(btnScreen, screenOn);
            }).catch(function (e) { console.error('screen share failed', e); });
        });
    }

    function init() {
        ringOverlay = $('callRingOverlay'); ringAvatar = $('callRingAvatar');
        ringName = $('callRingName'); ringSub = $('callRingSub');
        ringAccept = $('callRingAccept'); ringReject = $('callRingReject');
        screenEl = $('callScreen'); gridEl = $('callGrid');
        titleEl = $('callScreenTitle'); timerEl = $('callScreenTimer');
        btnMic = $('callToggleMic'); btnCam = $('callToggleCam');
        btnScreen = $('callToggleScreen'); btnHangup = $('callHangup');
        if (!ringOverlay || !screenEl) return; // нет разметки — модуль неактивен
        bindControls();
        bindCallEvents();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.BF.callsUI = { teardown: teardown };
})();
