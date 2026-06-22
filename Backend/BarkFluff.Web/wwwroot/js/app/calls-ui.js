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

    // --- SVG-иконки (stroke=currentColor, 24×24) ---
    var ICONS = {
        mic: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><line x1="12" y1="19" x2="12" y2="23"/><line x1="8" y1="23" x2="16" y2="23"/></svg>',
        micOff: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="1" y1="1" x2="23" y2="23"/><path d="M9 9v3a3 3 0 0 0 5.12 2.12M15 9.34V4a3 3 0 0 0-5.94-.6"/><path d="M17 16.95A7 7 0 0 1 5 12v-2m14 0v2a7 7 0 0 1-.11 1.23"/><line x1="12" y1="19" x2="12" y2="23"/><line x1="8" y1="23" x2="16" y2="23"/></svg>',
        video: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M23 7l-7 5 7 5V7z"/><rect x="1" y="5" width="15" height="14" rx="2" ry="2"/></svg>',
        videoOff: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 16v1a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h2m5.66 0H14a2 2 0 0 1 2 2v3.34l1 1L23 7v10"/><line x1="1" y1="1" x2="23" y2="23"/></svg>',
        monitor: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>',
        phoneOff: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.68 13.31a16 16 0 0 0 3.41 2.6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7 2 2 0 0 1 1.72 2v3a2 2 0 0 1-2.18 2A19.79 19.79 0 0 1 8.63 19.24M5 5a2 2 0 0 0-1 .54 19.5 19.5 0 0 0-3 8.63 2 2 0 0 0 .54 1.84"/><line x1="23" y1="1" x2="1" y2="23"/></svg>'
    };

    // --- DOM ---
    var ringOverlay, ringAvatar, ringName, ringSub, ringAccept, ringReject;
    var screenEl, gridEl, titleEl, timerEl, btnMic, btnCam, btnScreen, btnHangup;
    var stageVideoWrap, stageLabel, waitingAvatar, waitingName, waitingSub;

    // --- LiveKit ---
    var room = null;
    var tiles = {};            // identity -> { el, avatarEl, labelEl, videoEl, audioEls }
    var stage = null;          // демонстрация экрана: { identity, track }
    var localIdentity = null;
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
        ringAvatar.classList.add('pulsing');
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
        updateLayout();
        return tile;
    }

    function removeTile(participant) {
        var tile = tiles[participant.identity];
        if (!tile) return;
        tile.audioEls.forEach(function (a) { try { a.remove(); } catch (e) {} });
        try { tile.el.remove(); } catch (e) {}
        delete tiles[participant.identity];
        updateLayout();
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

    // --- Демонстрация экрана (stage) ---
    function isScreenShare(track, pub) {
        var SS = window.LivekitClient.Track.Source.ScreenShare;
        return (pub && pub.source === SS) || (track && track.source === SS);
    }

    function setStage(participant, track) {
        if (stage && stage.track) { try { stage.track.detach().forEach(function (e) { e.remove(); }); } catch (e) {} }
        var v = track.attach();
        v.autoplay = true; v.playsInline = true;
        if (participant.isLocal) v.muted = true;
        stageVideoWrap.innerHTML = '';
        stageVideoWrap.appendChild(v);
        stage = { identity: participant.identity, track: track };

        var base = participant.isLocal ? 'Вы' : ('#' + participant.identity);
        stageLabel.textContent = base + ' · демонстрация экрана';
        var uid = Number(participant.identity);
        if (uid && !participant.isLocal) {
            resolveUser(uid).then(function (user) {
                if (stage && stage.identity === participant.identity)
                    stageLabel.textContent = userName(user, base) + ' · демонстрация экрана';
            });
        }
        updateLayout();
    }
    function clearStage(identity) {
        if (!stage) return;
        if (identity && stage.identity !== identity) return;
        try { stage.track.detach().forEach(function (e) { e.remove(); }); } catch (e) {}
        stageVideoWrap.innerHTML = '';
        stage = null;
        updateLayout();
    }

    // Раскладка экрана: ожидание (нет удалённых) / демонстрация / обычная сетка.
    function updateLayout() {
        if (!screenEl) return;
        var hasStage = !!stage;
        var hasRemote = Object.keys(tiles).some(function (id) { return id !== localIdentity; });
        screenEl.classList.toggle('has-stage', hasStage);
        screenEl.classList.toggle('waiting', !hasStage && !hasRemote);
    }

    // --- LiveKit события ---
    function onTrackSubscribed(track, pub, participant) {
        var L = window.LivekitClient;
        if (track.kind === L.Track.Kind.Video) {
            if (isScreenShare(track, pub)) { setStage(participant, track); return; }
            setTileVideo(ensureTile(participant), track.attach());
        } else if (track.kind === L.Track.Kind.Audio) {
            var tile = ensureTile(participant);
            var a = track.attach();
            a.style.display = 'none';
            tile.el.appendChild(a);
            tile.audioEls.push(a);
        }
    }
    function onTrackUnsubscribed(track, pub, participant) {
        var L = window.LivekitClient;
        if (track.kind === L.Track.Kind.Video && isScreenShare(track, pub)) {
            try { track.detach().forEach(function (el) { el.remove(); }); } catch (e) {}
            clearStage(participant.identity);
            return;
        }
        try { track.detach().forEach(function (el) { el.remove(); }); } catch (e) {}
        var tile = tiles[participant.identity];
        if (tile && track.kind === L.Track.Kind.Video) clearTileVideo(tile);
    }
    function onLocalTrackPublished(pub, participant) {
        var L = window.LivekitClient;
        if (!pub.track || pub.track.kind !== L.Track.Kind.Video) return; // аудио не отрисовываем (эхо)
        if (isScreenShare(pub.track, pub)) { setStage(participant, pub.track); return; }
        var el = pub.track.attach(); el.muted = true;
        setTileVideo(ensureTile(participant), el);
    }
    function onLocalTrackUnpublished(pub, participant) {
        var L = window.LivekitClient;
        if (!pub.track || pub.track.kind !== L.Track.Kind.Video) return;
        if (isScreenShare(pub.track, pub)) {
            try { pub.track.detach().forEach(function (el) { el.remove(); }); } catch (e) {}
            clearStage(participant.identity);
            return;
        }
        var tile = tiles[participant.identity];
        if (tile) {
            try { pub.track.detach().forEach(function (el) { el.remove(); }); } catch (e) {}
            clearTileVideo(tile);
        }
    }

    function onActiveSpeakers(speakers) {
        var speaking = {};
        speakers.forEach(function (p) { speaking[p.identity] = true; });
        Object.keys(tiles).forEach(function (id) {
            tiles[id].el.classList.toggle('speaking', !!speaking[id]);
        });
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
        room.on(L.RoomEvent.ActiveSpeakersChanged, onActiveSpeakers);
        room.on(L.RoomEvent.ParticipantConnected, function (p) {
            ensureTile(p);
            startTimer(); // первый собеседник вошёл — звонок состоялся
        });
        room.on(L.RoomEvent.ParticipantDisconnected, function (p) { removeTile(p); });

        var wantVideo = d.mediaType === BF.calls.MediaType.VIDEO;

        room.connect(d.livekitUrl, d.accessToken).then(function () {
            localIdentity = room.localParticipant.identity;
            ensureTile(room.localParticipant);
            var remotes = room.remoteParticipants || room.participants;
            if (remotes && remotes.forEach) remotes.forEach(function (p) { ensureTile(p); });
            updateLayout();
            return room.localParticipant.setMicrophoneEnabled(true);
        }).then(function () {
            micOn = true; renderControls();
            if (wantVideo) return room.localParticipant.setCameraEnabled(true);
        }).then(function () {
            if (wantVideo) { camOn = true; renderControls(); }
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

    // --- Контрол-кнопки (иконки + состояние) ---
    function setIcon(btn, svg) { if (btn) btn.innerHTML = svg; }
    function renderControls() {
        setIcon(btnMic, micOn ? ICONS.mic : ICONS.micOff);
        btnMic.classList.toggle('off', !micOn);
        btnMic.title = micOn ? 'Выключить микрофон' : 'Включить микрофон';
        setIcon(btnCam, camOn ? ICONS.video : ICONS.videoOff);
        btnCam.classList.toggle('off', !camOn);
        btnCam.title = camOn ? 'Выключить камеру' : 'Включить камеру';
        setIcon(btnScreen, ICONS.monitor);
        btnScreen.classList.toggle('active', screenOn);
    }

    // --- Экран ожидания собеседника ---
    function setupWaiting(d) {
        waitingAvatar.innerHTML = '';
        if (d.isGroup) {
            waitingAvatar.textContent = '#';
            waitingName.textContent = 'Групповой звонок';
            waitingSub.textContent = 'Ожидание участников…';
        } else {
            waitingAvatar.textContent = '?';
            waitingName.textContent = 'Соединение…';
            waitingSub.textContent = d.role === 'caller' ? 'Вызов…' : 'Подключение…';
            if (d.peerUserId) {
                resolveUser(d.peerUserId).then(function (user) {
                    if (activeCallId !== d.callId) return;
                    waitingName.textContent = userName(user, 'Соединение…');
                    fillAvatar(waitingAvatar, user, waitingName.textContent);
                });
            }
        }
    }

    // --- Экран активного звонка ---
    function openScreen(d) {
        activeCallId = d.callId;
        titleEl.textContent = d.isGroup ? 'Групповой звонок' : 'Звонок';
        timerEl.textContent = d.role === 'caller' ? 'Вызов…' : '';
        micOn = false; camOn = false; screenOn = false;
        renderControls();
        setupWaiting(d);
        screenEl.classList.remove('has-stage');
        screenEl.classList.add('waiting');   // до подключения собеседника
        screenEl.classList.add('visible');
        connectLiveKit(d);
        if (d.role !== 'caller') startTimer();
    }

    function teardown() {
        stopTimer();
        stopRingtone();
        if (room) { try { room.disconnect(); } catch (e) {} room = null; }
        clearGrid();
        stage = null;
        stageVideoWrap.innerHTML = '';
        screenEl.classList.remove('visible', 'waiting', 'has-stage');
        activeCallId = null; localIdentity = null;
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
                micOn = !micOn; renderControls();
            });
        });
        btnCam.addEventListener('click', function () {
            if (!room) return;
            room.localParticipant.setCameraEnabled(!camOn).then(function () {
                camOn = !camOn; renderControls();
            });
        });
        btnScreen.addEventListener('click', function () {
            if (!room) return;
            room.localParticipant.setScreenShareEnabled(!screenOn).then(function () {
                screenOn = !screenOn; renderControls();
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
        stageVideoWrap = $('callStageVideo'); stageLabel = $('callStageLabel');
        waitingAvatar = $('callWaitingAvatar'); waitingName = $('callWaitingName'); waitingSub = $('callWaitingSub');
        if (!ringOverlay || !screenEl) return; // нет разметки — модуль неактивен
        setIcon(btnHangup, ICONS.phoneOff);
        renderControls();
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
