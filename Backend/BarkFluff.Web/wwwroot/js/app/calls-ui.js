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
        // Перечёркивание во всех off-иконках идёт в одну сторону (↘, 1,1→23,23).
        monitorOff: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/><line x1="1" y1="1" x2="23" y2="23"/></svg>',
        // Классическая «трубка завершения» (сплошная, как красная кнопка отбоя).
        phoneEnd: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 9c-1.6 0-3.15.25-4.6.72v3.1c0 .39-.23.74-.56.9-.98.49-1.87 1.12-2.66 1.85-.18.18-.43.28-.7.28-.28 0-.53-.11-.71-.29L.29 13.08A.99.99 0 0 1 0 12.38c0-.28.11-.53.29-.71C3.34 8.78 7.46 7 12 7s8.66 1.78 11.71 4.67c.18.18.29.43.29.71 0 .28-.11.53-.29.71l-2.48 2.48c-.18.18-.43.29-.71.29-.27 0-.52-.11-.7-.28-.79-.74-1.69-1.36-2.67-1.85-.33-.16-.56-.5-.56-.9v-3.1C15.15 9.25 13.6 9 12 9z"/></svg>',
        quality: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="4" y1="21" x2="4" y2="14"/><line x1="4" y1="10" x2="4" y2="3"/><line x1="12" y1="21" x2="12" y2="12"/><line x1="12" y1="8" x2="12" y2="3"/><line x1="20" y1="21" x2="20" y2="16"/><line x1="20" y1="12" x2="20" y2="3"/><line x1="1" y1="14" x2="7" y2="14"/><line x1="9" y1="8" x2="15" y2="8"/><line x1="17" y1="16" x2="23" y2="16"/></svg>',
        chevron: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 14 12 8 18 14"/></svg>'
    };

    // --- Палитра аватаров (детерминированная по identity, как отдельные цвета участников) ---
    var AVATAR_PALETTE = ['#3f6bd8', '#e8412a', '#c2477f', '#d98b1f', '#2e8f68', '#7c6de0', '#1f9bd9', '#b0553d'];
    function avatarColor(identity) {
        var s = String(identity), h = 0;
        for (var i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
        return AVATAR_PALETTE[h % AVATAR_PALETTE.length];
    }

    // --- DOM ---
    var ringOverlay, ringAvatar, ringName, ringSub, ringAccept, ringReject;
    var permissionOverlay, permissionTitle, permissionText, permissionRequest, permissionCancel;
    var screenEl, mainEl, heroEl, vstageEl, stripEl, bgEl, titleEl, timerEl, btnMic, btnCam, btnScreen, btnHangup;
    var waitingAvatar, waitingName, waitingSub;
    var btnQuality, qualityPanel, audioChips, videoChips, videoQualityGroup, audioQualityGroup;
    var micCaret, micMenu, camCaret, camMenu;
    var audioHostEl = null; // постоянный скрытый контейнер для <audio>/<video> чужих аудиотреков

    // --- LiveKit ---
    var room = null;
    var streamTiles = {};      // key (camKey/screenKey) -> плитка с видео (камера или демонстрация экрана)
    var participants = {};     // identity -> { level, active, analyser, hasAudio, audioEls[], inRoom }
    var members = {};          // identity -> плашка участника и его имя/аватар
    var memberOrder = [];      // порядок входа — стабильный порядок плашек и плиток
    var heroIds = [];          // кто крупно в аудио-режиме
    var heroIsDefault = true;  // крупно показан не говорящий, а собеседник по умолчанию
    var heroNodes = {};        // identity -> DOM крупного аватара и его фонового пятна
    var heroHoldTimer = null;
    var fullKey = null;        // ключ плитки, раскрытой на весь экран звонка
    var micOn = false, camOn = false, screenOn = false;

    // --- Голос (WebAudio, только реальные треки — без симуляции) ---
    var SPEAK_LEVEL_THRESHOLD = 0.1;
    var SPEAK_ON_MS = 150, SPEAK_OFF_MS = 800; // гистерезис «говорит»
    var HERO_MAX = 3, HERO_HOLD_MS = 1500;
    // Масштаб аватара и смещения по X (в базовых размерах аватара) для 1–3 говорящих.
    var HERO_LAYOUT = { 1: { s: 1, x: [0] }, 2: { s: 0.78, x: [-0.92, 0.92] }, 3: { s: 0.62, x: [-1.36, 0, 1.36] } };
    var rafId = null, lastFrameTs = 0;
    var ro = null; // ResizeObserver области сцены

    // Выбранные устройства (in-app пикеры) — null = устройство по умолчанию.
    var selectedMicId = null, selectedCamId = null;
    // Индикация «применяю качество» для того, кто меняет.
    var audioPending = false, videoPending = false;

    // --- Качество (0=Авто,1=Низкое,2=Среднее,3=Высокое) ---
    // Голос — общий для звонка (через сервер). Видео — локально у публикующего.
    var currentAudioQuality = 0, currentVideoQuality = 0;
    var AUDIO_BITRATE = { 1: 14000, 2: 24000, 3: 48000 };           // bps, Авто → дефолт SDK
    var VIDEO_PRESET = {
        1: { w: 640,  h: 360, fps: 24, bitrate: 400000 },
        2: { w: 960,  h: 540, fps: 25, bitrate: 1000000 },
        3: { w: 1280, h: 720, fps: 30, bitrate: 1700000 }
    };

    // --- Состояние UI ---
    var ringCallId = null;
    var activeCallId = null;
    var timerInterval = null;
    var callStartedAt = 0;
    var permissionDialog = null;
    var onWinResize = null;

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
        if (!user) return fallback || BF.i18n.t('common.user');
        var n = [user.firstName, user.lastName].filter(Boolean).join(' ');
        return n || user.username || fallback || BF.i18n.t('common.user');
    }

    function escapeAttr(s) { return String(s).replace(/"/g, '&quot;'); }

    function fillAvatar(el, user, fallbackText) {
        var src = user && (user.profilePicture || user.profilePicturePreview);
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
        ringName.textContent = BF.i18n.t(d.isGroup ? 'call.group' : 'call.incoming');
        ringSub.textContent = BF.i18n.t(d.mediaType === BF.calls.MediaType.VIDEO ? 'attachment.video' : 'attachment.audio');
        ringAvatar.innerHTML = '';
        ringAvatar.textContent = d.isGroup ? '#' : '?';
        ringAvatar.classList.add('pulsing');
        if (!d.isGroup && d.callerUserId) {
            resolveUser(d.callerUserId).then(function (user) {
                if (ringCallId !== d.callId) return;
                ringName.textContent = userName(user, BF.i18n.t('call.incoming'));
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

    function dismissIncomingPermissionDialog(callId) {
        if (ringCallId !== callId) return false;
        hideRing();
        if (permissionDialog && permissionDialog.callId === callId) {
            var error = new Error('incoming call is no longer active');
            error.code = 'incoming-call-dismissed';
            closePermissionDialog(error);
        }
        return true;
    }

    // --- Разрешения на медиа ---
    function requiredPermissionNames(mediaType) {
        var names = ['microphone'];
        if (mediaType === BF.calls.MediaType.VIDEO) names.push('camera');
        return names;
    }

    function permissionLabel(name) {
        return BF.i18n.t(name === 'camera' ? 'call.permission.camera' : 'call.permission.mic');
    }

    function permissionList(names) {
        return names.map(permissionLabel).join(BF.i18n.t('common.and'));
    }

    function getPermissionState(name) {
        if (!navigator.permissions || !navigator.permissions.query) return Promise.resolve('prompt');
        return navigator.permissions.query({ name: name }).then(function (status) {
            return status.state;
        }).catch(function () {
            // Safari и отдельные браузеры не поддерживают query для camera/microphone.
            return 'prompt';
        });
    }

    function closePermissionDialog(error) {
        if (!permissionDialog) return;
        var dialog = permissionDialog;
        permissionDialog = null;
        permissionOverlay.classList.remove('visible');
        if (error) dialog.reject(error);
        else dialog.resolve();
    }

    function requestMediaPermissions(mediaType) {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            return Promise.reject(new Error('media devices are not supported by this browser'));
        }
        return navigator.mediaDevices.getUserMedia({
            audio: true,
            video: mediaType === BF.calls.MediaType.VIDEO
        }).then(function (stream) {
            stream.getTracks().forEach(function (track) { track.stop(); });
        });
    }

    function showPermissionDialog(mediaType, states, callId) {
        var names = requiredPermissionNames(mediaType);
        var hasDeniedPermission = states && states.indexOf('denied') !== -1;
        permissionTitle.textContent = BF.i18n.t('call.permission.title', { targets: permissionList(names) });
        permissionText.textContent = hasDeniedPermission
            ? BF.i18n.t('call.permission.denied')
            : BF.i18n.t('call.permission.text', { targets: permissionList(names) });
        permissionRequest.textContent = BF.i18n.t(hasDeniedPermission ? 'call.permission.retry' : 'common.allow');
        permissionRequest.disabled = false;
        permissionOverlay.classList.add('visible');

        if (permissionDialog) return permissionDialog.promise;

        var resolveDialog, rejectDialog;
        var promise = new Promise(function (resolve, reject) {
            resolveDialog = resolve;
            rejectDialog = reject;
        });
        permissionDialog = { promise: promise, resolve: resolveDialog, reject: rejectDialog, mediaType: mediaType, callId: callId || null };
        return promise;
    }

    function ensureMediaPermissions(mediaType, callId) {
        var names = requiredPermissionNames(mediaType);
        return Promise.all(names.map(getPermissionState)).then(function (states) {
            if (states.every(function (state) { return state === 'granted'; })) return;
            return showPermissionDialog(mediaType, states, callId);
        });
    }

    function bindPermissionDialog() {
        permissionRequest.addEventListener('click', function () {
            if (!permissionDialog) return;
            var dialog = permissionDialog;
            permissionRequest.disabled = true;
            requestMediaPermissions(dialog.mediaType).then(function () {
                closePermissionDialog();
            }).catch(function (e) {
                console.warn('[calls] media permission was not granted:', e);
                var denied = e && (e.name === 'NotAllowedError' || e.name === 'SecurityError');
                showPermissionDialog(dialog.mediaType, denied ? ['denied'] : [], dialog.callId);
                permissionRequest.disabled = false;
            });
        });
        permissionCancel.addEventListener('click', function () {
            var error = new Error('permission request dismissed');
            error.code = 'media-permission-dismissed';
            closePermissionDialog(error);
        });
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

    // ============================================================
    // Частотный анализ голоса → уровень и устойчивое «говорит»
    // ============================================================
    function follow(cur, target, dt, attack, release) {
        return cur + (target - cur) * Math.min(1, dt * (target > cur ? attack : release));
    }

    function readFreqBands(analyser) {
        var an = analyser.an, freq = analyser.data;
        an.getByteFrequencyData(freq);
        var binHz = analyser.ctx.sampleRate / an.fftSize;
        function avg(lo, hi) {
            var s = Math.floor(lo / binHz), e = Math.min(freq.length, Math.ceil(hi / binHz));
            var sum = 0; for (var i = s; i < e; i++) sum += freq[i];
            return e > s ? sum / (e - s) / 255 : 0;
        }
        return [avg(60, 320), avg(320, 1600), avg(1600, 6000)].map(function (v) {
            var x = v * 4.5; return Math.min(1, x / (1 + x * 0.5));
        });
    }

    function getOrCreateParticipant(identity) {
        if (!participants[identity]) {
            participants[identity] = {
                identity: identity,
                level: 0, active: false, activeSince: 0, loudAt: 0, quietAt: 0,
                audioState: undefined, analyser: null, hasAudio: false,
                audioEls: [], inRoom: true
            };
        }
        return participants[identity];
    }

    function attachMeter(identity, track) {
        if (!track || !track.mediaStreamTrack) return;
        var p = getOrCreateParticipant(identity);
        try {
            if (!audioCtx) audioCtx = new (window.AudioContext || window.webkitAudioContext)();
            if (audioCtx.state === 'suspended') audioCtx.resume();
            detachMeter(identity);
            var src = audioCtx.createMediaStreamSource(new MediaStream([track.mediaStreamTrack]));
            var an = audioCtx.createAnalyser();
            an.fftSize = 1024; an.smoothingTimeConstant = 0.86;
            src.connect(an); // в destination не подключаем — только анализ, без эха
            p.analyser = { ctx: audioCtx, an: an, data: new Uint8Array(an.frequencyBinCount), src: src };
            p.hasAudio = true;
        } catch (e) { /* WebAudio недоступен — индикации голоса не будет, не критично */ }
    }
    function detachMeter(identity) {
        var p = participants[identity];
        if (!p) return;
        if (p.analyser) { try { p.analyser.src.disconnect(); } catch (e) {} }
        p.analyser = null;
        p.hasAudio = false;
        p.level = 0; p.loudAt = p.quietAt = 0;
        if (p.active) setSpeaking(identity, false);
    }

    // ============================================================
    // Рендер-цикл: уровень голоса, гистерезис «говорит», кольца и эквалайзеры
    // ============================================================
    function stepAll(dt) {
        var now = performance.now();
        Object.keys(participants).forEach(function (id) {
            var p = participants[id];
            var v = p.analyser ? readFreqBands(p.analyser) : [0, 0, 0];
            p.level = follow(p.level, (v[0] + v[1] + v[2]) * 0.42, dt, 8, 3);

            // Гистерезис: короткие всплески и паузы между словами не дёргают раскладку.
            var loud = p.hasAudio && p.level > SPEAK_LEVEL_THRESHOLD;
            if (loud) {
                p.quietAt = 0;
                if (!p.active) {
                    if (!p.loudAt) p.loudAt = now;
                    else if (now - p.loudAt >= SPEAK_ON_MS) setSpeaking(id, true);
                }
            } else {
                p.loudAt = 0;
                if (p.active) {
                    if (!p.quietAt) p.quietAt = now;
                    else if (now - p.quietAt >= SPEAK_OFF_MS) setSpeaking(id, false);
                }
            }

            var audioState = !p.hasAudio ? 'off' : (p.active ? 'speaking' : 'online');
            if (audioState !== p.audioState) {
                p.audioState = audioState;
                updateParticipantAudioUI(id, audioState);
            }
            if (p.active) drawLevel(id, p.level, now);
        });
    }

    // Только transform колец и высота пяти полосок — без перерасчёта раскладки.
    function drawLevel(identity, level, now) {
        var k = Math.min(1, level * 2);
        var hn = heroNodes[identity];
        if (hn && !hn.leaving) {
            hn.ring.style.transform = 'scale(' + (1 + k * 0.05).toFixed(3) + ')';
            hn.ring2.style.transform = 'scale(' + (1 + k * 0.11).toFixed(3) + ')';
        }
        var bars = [];
        var m = members[identity];
        if (m) bars = bars.concat(m.chipBars);
        var st = streamTiles[camKey(identity)];
        if (st) bars = bars.concat(st.bars);
        for (var i = 0; i < bars.length; i++) {
            var h = 4 + 11 * k * Math.abs(Math.sin(now / 120 + (i % 5) * 1.3));
            bars[i].style.height = h.toFixed(1) + 'px';
        }
    }

    function setSpeaking(identity, on) {
        var p = participants[identity];
        if (!p) return;
        p.active = on;
        if (on) p.activeSince = performance.now();
        var m = members[identity];
        if (m) m.chipEl.classList.toggle('speak', on);
        var st = streamTiles[camKey(identity)];
        if (st) st.el.classList.toggle('speak', on);
        if (!on && heroNodes[identity]) {
            heroNodes[identity].ring.style.transform = '';
            heroNodes[identity].ring2.style.transform = '';
        }
        refreshHero();
    }

    function updateParticipantAudioUI(identity, state) {
        var off = state === 'off';
        var m = members[identity];
        if (m) m.chipEl.classList.toggle('mute', off);
        var hn = heroNodes[identity];
        if (hn) {
            hn.el.classList.toggle('speak', state === 'speaking');
            hn.sub.textContent = state === 'speaking' ? BF.i18n.t('call.status.speaking')
                : (off ? BF.i18n.t('call.status.micOff') : '');
        }
        var st = streamTiles[camKey(identity)];
        if (st) st.el.classList.toggle('mute', off);
    }

    function startRenderLoop() {
        if (rafId) return;
        lastFrameTs = performance.now();
        function loop(now) {
            var dt = Math.min((now - lastFrameTs) / 1000, 1 / 30);
            lastFrameTs = now;
            if (!document.hidden) stepAll(dt);
            rafId = requestAnimationFrame(loop);
        }
        rafId = requestAnimationFrame(loop);
    }
    function stopRenderLoop() {
        if (rafId) { cancelAnimationFrame(rafId); rafId = null; }
    }

    function reducedMotion() {
        return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
    }

    function el(tag, cls, html) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (html) e.innerHTML = html;
        return e;
    }

    function eqHtml() { return '<i></i><i></i><i></i><i></i><i></i>'; }

    // ============================================================
    // Участники: плашки нижнего ряда (все, в порядке входа)
    // ============================================================
    function camKey(identity) { return identity; }
    function screenKey(identity) { return identity + '#screen'; }

    function paintAvatar(avatarEl, m) {
        avatarEl.style.background = avatarColor(m.identity);
        fillAvatar(avatarEl, m.user, m.name);
    }

    function ensureMember(identity, isLocal) {
        if (members[identity]) return members[identity];
        var chip = el('div', 'call-member');
        var avatar = el('div', 'call-member-avatar');
        var name = el('span', 'call-member-name');
        var eq = el('span', 'call-eq', eqHtml());
        var muted = el('span', 'call-member-muted', ICONS.micOff);
        chip.appendChild(avatar); chip.appendChild(name); chip.appendChild(eq); chip.appendChild(muted);
        stripEl.appendChild(chip);

        var base = isLocal ? BF.i18n.t('call.you') : ('#' + identity);
        var m = {
            identity: identity, isLocal: !!isLocal, name: base, user: null,
            chipEl: chip, chipAvatar: avatar, chipName: name, chipBars: [].slice.call(eq.children)
        };
        members[identity] = m;
        memberOrder.push(identity);
        name.textContent = base;
        paintAvatar(avatar, m);
        var p = participants[identity];
        if (p) { chip.classList.toggle('mute', !p.hasAudio); chip.classList.toggle('speak', p.active); }
        else chip.classList.add('mute');

        resolveUser(isLocal ? getMyUserId() : Number(identity)).then(function (user) {
            if (members[identity] !== m) return;
            m.user = user;
            m.name = userName(user, base);
            refreshMemberViews(identity);
        });
        refreshScene();
        return m;
    }

    // Имя и аватар пришли из профиля — обновляем все места, где виден участник.
    function refreshMemberViews(identity) {
        var m = members[identity];
        if (!m) return;
        m.chipName.textContent = m.name;
        paintAvatar(m.chipAvatar, m);
        var hn = heroNodes[identity];
        if (hn) { hn.name.textContent = m.name; paintAvatar(hn.avatar, m); paintBlob(hn.blob, m); }
        [camKey(identity), screenKey(identity)].forEach(function (key) {
            var st = streamTiles[key];
            if (st) fillTileLabel(st, m);
        });
    }

    function removeMember(identity) {
        var m = members[identity];
        if (!m) return;
        try { m.chipEl.remove(); } catch (e) {}
        delete members[identity];
        memberOrder = memberOrder.filter(function (id) { return id !== identity; });
        refreshScene();
    }

    // ============================================================
    // Аудио-режим: крупно те, кто говорит (до HERO_MAX одновременно)
    // ============================================================
    function anySpeaking() {
        return memberOrder.some(function (id) { return participants[id] && participants[id].active; });
    }

    function defaultHero() {
        var remote = memberOrder.filter(function (id) { return !members[id].isLocal; });
        return remote.length ? [remote[0]] : memberOrder.slice(0, 1);
    }

    function updateHeroIds() {
        heroIds = heroIds.filter(function (id) { return !!members[id]; });
        var speaking = memberOrder.filter(function (id) { return participants[id] && participants[id].active; });
        if (speaking.length) {
            if (heroHoldTimer) { clearTimeout(heroHoldTimer); heroHoldTimer = null; }
            heroIsDefault = false;
            // Уже показанные остаются на местах, новые говорящие добавляются справа.
            var next = heroIds.filter(function (id) { return speaking.indexOf(id) !== -1; });
            speaking.sort(function (a, b) { return participants[a].activeSince - participants[b].activeSince; })
                .forEach(function (id) { if (next.indexOf(id) === -1) next.push(id); });
            heroIds = next.slice(-HERO_MAX);
            return;
        }
        if (!heroIds.length || heroIsDefault) {
            heroIds = defaultHero();
            heroIsDefault = true;
            return;
        }
        // Все замолчали: держим раскладку паузу, затем остаётся последний говоривший.
        if (heroIds.length > 1 && !heroHoldTimer) {
            heroHoldTimer = setTimeout(function () {
                heroHoldTimer = null;
                if (anySpeaking() || heroIds.length < 2) return;
                heroIds = heroIds.slice(-1);
                placeHero();
            }, HERO_HOLD_MS);
        }
    }

    function paintBlob(blob, m) {
        var src = m.user && (m.user.profilePicture || m.user.profilePicturePreview);
        blob.style.background = src ? '' : avatarColor(m.identity);
        blob.innerHTML = src ? '<img src="' + escapeAttr(src) + '" alt="">' : '';
    }

    function heroNode(identity) {
        if (heroNodes[identity]) return heroNodes[identity];
        var m = members[identity];
        var item = el('div', 'call-hero-item');
        var avw = el('div', 'call-hero-avw');
        var ring2 = el('div', 'call-hero-ring2');
        var ring = el('div', 'call-hero-ring');
        var avatar = el('div', 'call-hero-avatar');
        avw.appendChild(ring2); avw.appendChild(ring); avw.appendChild(avatar);
        var meta = el('div', 'call-hero-meta');
        var name = el('div', 'call-hero-name');
        var sub = el('div', 'call-hero-sub');
        meta.appendChild(name); meta.appendChild(sub);
        item.appendChild(avw); item.appendChild(meta);
        heroEl.appendChild(item);
        var blob = el('div', 'call-blob');
        bgEl.appendChild(blob);

        name.textContent = m.name;
        paintAvatar(avatar, m);
        paintBlob(blob, m);
        var n = { el: item, avw: avw, ring: ring, ring2: ring2, avatar: avatar, name: name, sub: sub, blob: blob, fresh: true, leaving: null };
        heroNodes[identity] = n;
        var p = participants[identity];
        updateParticipantAudioUI(identity, p ? (p.audioState || 'off') : 'off');
        return n;
    }

    function refreshHero() { updateHeroIds(); placeHero(); }

    function placeHero() {
        if (!heroEl || !mainEl) return;
        var n = heroIds.length;
        var W = mainEl.clientWidth, H = mainEl.clientHeight;
        if (n && W && H) {
            // Базовый размер аватара: не больше 184px, влезает по высоте и по ширине для n говорящих.
            var base = Math.max(72, Math.min(184, H * 0.46, W / (n === 1 ? 2.2 : n === 2 ? 3.3 : 4.4)));
            var lay = HERO_LAYOUT[n];
            var instant = reducedMotion();
            heroEl.style.setProperty('--hero-av', base + 'px');
            heroEl.classList.toggle('multi', n > 1);
            heroIds.forEach(function (id, i) {
                var hn = heroNode(id);
                if (hn.leaving) { clearTimeout(hn.leaving); hn.leaving = null; }
                var x = lay.x[i] * base, s = lay.s, dy = -(1 - s) * base / 2;
                var itemTf = 'translate(' + x.toFixed(1) + 'px,' + dy.toFixed(1) + 'px)';
                if (hn.fresh) {
                    hn.fresh = false;
                    hn.el.style.transition = hn.avw.style.transition = 'none';
                    hn.el.style.transform = itemTf;
                    hn.avw.style.transform = 'scale(' + (instant ? s : s * 0.9) + ')';
                    hn.el.getBoundingClientRect();
                    hn.el.style.transition = hn.avw.style.transition = '';
                }
                hn.el.style.transform = itemTf;
                hn.avw.style.transform = 'scale(' + s + ')';
                hn.el.classList.add('in');
                hn.blob.style.transform = 'translateX(' + (x * 1.2).toFixed(1) + 'px) scale(' + (n > 1 ? 0.75 : 1) + ')';
                hn.blob.classList.add('in');
                hn.blob.classList.toggle('multi', n > 1);
            });
        }
        Object.keys(heroNodes).forEach(function (id) {
            if (heroIds.indexOf(id) !== -1) return;
            var hn = heroNodes[id];
            if (hn.leaving) return;
            hn.el.classList.remove('in');
            hn.blob.classList.remove('in');
            hn.leaving = setTimeout(function () {
                try { hn.el.remove(); hn.blob.remove(); } catch (e) {}
                if (heroNodes[id] === hn) delete heroNodes[id];
            }, 460);
        });
    }

    // ============================================================
    // Видео-режим: плитки камер и демонстраций экрана
    // ============================================================
    function fillTileLabel(tile, m) {
        if (tile.screen) {
            tile.nameEl.textContent = BF.i18n.t('call.tile.screen', { name: m.name });
        } else {
            tile.nameEl.textContent = m.name;
            paintAvatar(tile.avatarEl, m);
        }
    }

    function ensureStreamTile(identity, isLocal, kind) {
        var key = kind === 'screen' ? screenKey(identity) : camKey(identity);
        if (streamTiles[key]) return streamTiles[key];
        var m = ensureMember(identity, isLocal);
        var screen = kind === 'screen';

        var outer = el('div', 'call-vt' + (screen ? ' screen' : ''));
        var inner = el('div', 'call-vt-inner');
        var ring = el('div', 'call-vt-ring');
        var pill = el('div', 'call-vt-pill');
        var avatar = null, nameSpan = el('span', 'call-vt-name'), eq = null;
        if (screen) {
            pill.appendChild(el('span', 'call-vt-icon', ICONS.monitor));
            pill.appendChild(nameSpan);
        } else {
            avatar = el('div', 'call-vt-avatar');
            eq = el('span', 'call-eq', eqHtml());
            pill.appendChild(avatar); pill.appendChild(nameSpan); pill.appendChild(eq);
            pill.appendChild(el('span', 'call-vt-muted', ICONS.micOff));
        }
        var expand = el('button', 'call-vt-expand');
        expand.type = 'button';
        inner.appendChild(ring); inner.appendChild(pill); inner.appendChild(expand);
        outer.appendChild(inner);
        // Клик по плитке раскрывает её на весь экран звонка, повторный — сворачивает.
        inner.addEventListener('click', function (e) { e.stopPropagation(); toggleFull(key); });
        vstageEl.appendChild(outer);

        var tile = {
            key: key, el: outer, inner: inner, ringEl: ring, nameEl: nameSpan, avatarEl: avatar, expandEl: expand,
            bars: eq ? [].slice.call(eq.children) : [], videoEl: null, track: null,
            identity: identity, isLocal: !!isLocal, screen: screen, rect: null
        };
        streamTiles[key] = tile;
        fillTileLabel(tile, m);
        var p = participants[identity];
        if (!screen && p) { outer.classList.toggle('speak', p.active); outer.classList.toggle('mute', !p.hasAudio); }
        else if (!screen) outer.classList.add('mute');
        refreshScene();
        return tile;
    }

    function removeStreamTile(key) {
        var tile = streamTiles[key];
        if (!tile) return;
        delete streamTiles[key];
        if (fullKey === key) fullKey = null;
        tile.el.classList.remove('in');
        tile.el.style.pointerEvents = 'none';
        setTimeout(function () { try { tile.el.remove(); } catch (e) {} }, reducedMotion() ? 0 : 320);
        refreshScene();
    }

    function setTileVideo(tile, track, mirror) {
        if (!tile) return;
        var videoEl = track.attach();
        videoEl.autoplay = true; videoEl.playsInline = true;
        if (tile.isLocal) videoEl.muted = true;
        if (mirror) videoEl.style.transform = 'scaleX(-1)';
        if (tile.videoEl) { try { tile.videoEl.remove(); } catch (e) {} }
        tile.videoEl = videoEl; tile.track = track;
        tile.inner.insertBefore(videoEl, tile.inner.firstChild);
    }

    function onVideoActive(identity, kind, isLocal, track) {
        var tile = ensureStreamTile(identity, isLocal, kind);
        setTileVideo(tile, track, isLocal && kind === 'camera');
    }
    function onVideoInactive(identity, kind) {
        removeStreamTile(kind === 'screen' ? screenKey(identity) : camKey(identity));
    }

    function removeParticipant(participant) {
        var identity = participant.identity;
        var p = participants[identity];
        if (p) p.inRoom = false;
        detachMeter(identity);
        removeStreamTile(camKey(identity));
        removeStreamTile(screenKey(identity));
        if (p) p.audioEls.forEach(function (a) { try { a.remove(); } catch (e) {} });
        delete participants[identity];
        removeMember(identity);
    }

    function toggleFull(key) {
        fullKey = fullKey === key || !streamTiles[key] ? null : key;
        refreshScene();
    }

    function isScreenShare(track, pub) {
        var SS = window.LivekitClient.Track.Source.ScreenShare;
        return (pub && pub.source === SS) || (track && track.source === SS);
    }

    // Демонстрации экрана первыми, затем камеры — в порядке входа участников.
    function orderedTileKeys() {
        var keys = [];
        memberOrder.forEach(function (id) { if (streamTiles[screenKey(id)]) keys.push(screenKey(id)); });
        memberOrder.forEach(function (id) { if (streamTiles[camKey(id)]) keys.push(camKey(id)); });
        return keys;
    }

    function fitAspect(w, h) {
        var r = 16 / 9;
        return w / h > r ? { w: h * r, h: h } : { w: w, h: w / r };
    }

    // Раскладка: с демонстрацией экрана — крупно слева и колонка справа; иначе сетка 16:9.
    function computeRects(keys, W, H) {
        var g = 12, n = keys.length, out = [];
        if (n > 1 && streamTiles[keys[0]].screen) {
            var m = n - 1, rw = Math.min(240, W * 0.26), rh = rw * 9 / 16;
            if (m * rh + (m - 1) * g > H) { rh = (H - (m - 1) * g) / m; rw = rh * 16 / 9; }
            var big = fitAspect(W - rw - g, H), x0 = (W - (big.w + g + rw)) / 2, y0 = (H - big.h) / 2;
            out.push({ x: x0, y: y0, w: big.w, h: big.h });
            for (var i = 0; i < m; i++) out.push({ x: x0 + big.w + g, y: y0 + i * (rh + g), w: rw, h: rh, small: true });
            return out;
        }
        var cols = n <= 1 ? 1 : n <= 4 ? 2 : 3, rows = Math.ceil(n / cols);
        var cell = fitAspect((W - g * (cols - 1)) / cols, (H - g * (rows - 1)) / rows);
        var top = (H - (rows * cell.h + (rows - 1) * g)) / 2;
        for (var j = 0; j < n; j++) {
            var r = Math.floor(j / cols), inRow = r < rows - 1 ? cols : n - cols * (rows - 1), c = j % cols;
            var left = (W - (inRow * cell.w + (inRow - 1) * g)) / 2;
            out.push({ x: left + c * (cell.w + g), y: top + r * (cell.h + g), w: cell.w, h: cell.h, small: cell.w < 300 });
        }
        return out;
    }

    // FLIP: размер выставляем сразу, переход играем только через transform.
    function placeTile(tile, r, animate) {
        var prev = tile.rect, s = tile.el.style;
        if (prev && prev.x === r.x && prev.y === r.y && prev.w === r.w && prev.h === r.h) return;
        s.width = r.w + 'px'; s.height = r.h + 'px';
        if (animate && prev) {
            s.transition = 'none';
            s.transform = 'translate(' + prev.x + 'px,' + prev.y + 'px) scale(' + (prev.w / r.w) + ',' + (prev.h / r.h) + ')';
            tile.el.getBoundingClientRect();
            s.transition = '';
        }
        s.transform = 'translate(' + r.x + 'px,' + r.y + 'px)';
        tile.rect = { x: r.x, y: r.y, w: r.w, h: r.h };
    }

    function placeTiles(keys, animate) {
        var W = vstageEl.clientWidth, H = vstageEl.clientHeight;
        if (!W || !H) return;
        var rects = computeRects(keys, W, H);
        // Раскрытая плитка растягивается на весь экран звонка (координаты относительно области сцены).
        var full = { x: -mainEl.offsetLeft, y: -mainEl.offsetTop, w: screenEl.clientWidth, h: screenEl.clientHeight };
        var anim = animate && !reducedMotion();
        keys.forEach(function (key, i) {
            var tile = streamTiles[key], isFull = fullKey === key;
            tile.el.classList.toggle('full', isFull);
            tile.el.classList.toggle('hid', !!fullKey && !isFull);
            tile.el.classList.toggle('small', !isFull && !!rects[i].small);
            tile.expandEl.textContent = BF.i18n.t(isFull ? 'call.collapse' : 'call.expand');
            placeTile(tile, isFull ? full : rects[i], anim);
            if (!tile.el.classList.contains('in')) {
                requestAnimationFrame(function () { if (streamTiles[key] === tile) tile.el.classList.add('in'); });
            }
        });
    }

    // Единая точка пересчёта сцены: режимы экрана, плашки, герои, плитки.
    function refreshScene(animate) {
        if (!screenEl) return;
        var hasRemote = memberOrder.some(function (id) { return !members[id].isLocal; });
        screenEl.classList.toggle('waiting', !hasRemote);
        screenEl.classList.toggle('waiting-empty', !memberOrder.length);
        var keys = orderedTileKeys();
        if (fullKey && keys.indexOf(fullKey) === -1) fullKey = null;
        screenEl.classList.toggle('video', keys.length > 0);
        screenEl.classList.toggle('full', !!fullKey);
        // Участник с камерой уже виден плиткой — его плашку из нижнего ряда убираем.
        memberOrder.forEach(function (id) {
            members[id].chipEl.classList.toggle('gone', !!streamTiles[camKey(id)]);
        });
        refreshHero();
        placeTiles(keys, animate !== false);
    }

    // --- LiveKit события ---
    function onTrackSubscribed(track, pub, participant) {
        var L = window.LivekitClient;
        if (track.kind === L.Track.Kind.Video) {
            onVideoActive(participant.identity, isScreenShare(track, pub) ? 'screen' : 'camera', false, track);
        } else if (track.kind === L.Track.Kind.Audio) {
            var a = track.attach(); a.style.display = 'none';
            audioHostEl.appendChild(a);
            var p = getOrCreateParticipant(participant.identity);
            p.audioEls.push(a);
            attachMeter(participant.identity, track);
            ensureMember(participant.identity, false);
        }
    }
    function onTrackUnsubscribed(track, pub, participant) {
        var L = window.LivekitClient;
        try { track.detach().forEach(function (el) { el.remove(); }); } catch (e) {}
        if (track.kind === L.Track.Kind.Video) {
            onVideoInactive(participant.identity, isScreenShare(track, pub) ? 'screen' : 'camera');
        } else if (track.kind === L.Track.Kind.Audio) {
            detachMeter(participant.identity);
        }
    }
    function onLocalTrackPublished(pub, participant) {
        var L = window.LivekitClient;
        if (!pub.track) return;
        if (pub.track.kind === L.Track.Kind.Audio) { attachMeter(participant.identity, pub.track); return; }
        if (pub.track.kind !== L.Track.Kind.Video) return;
        onVideoActive(participant.identity, isScreenShare(pub.track, pub) ? 'screen' : 'camera', true, pub.track);
    }
    function onLocalTrackUnpublished(pub, participant) {
        var L = window.LivekitClient;
        var isAudio = (pub.kind === L.Track.Kind.Audio) || (pub.track && pub.track.kind === L.Track.Kind.Audio);
        if (isAudio) { detachMeter(participant.identity); return; }
        var isVideo = (pub.kind === L.Track.Kind.Video) || (pub.track && pub.track.kind === L.Track.Kind.Video);
        if (!isVideo) return;
        if (pub.track) { try { pub.track.detach().forEach(function (el) { el.remove(); }); } catch (e) {} }
        onVideoInactive(participant.identity, pub.source === L.Track.Source.ScreenShare ? 'screen' : 'camera');
    }

    function connectLiveKit(d) {
        var L = window.LivekitClient;
        if (!L) { console.error('[calls] LiveKit SDK is not loaded'); return; }
        if (room) { try { room.disconnect(); } catch (e) {} room = null; }
        clearAllParticipants();

        room = new L.Room({ adaptiveStream: true, dynacast: true });
        room.on(L.RoomEvent.TrackSubscribed, onTrackSubscribed);
        room.on(L.RoomEvent.TrackUnsubscribed, onTrackUnsubscribed);
        room.on(L.RoomEvent.LocalTrackPublished, onLocalTrackPublished);
        room.on(L.RoomEvent.LocalTrackUnpublished, onLocalTrackUnpublished);
        room.on(L.RoomEvent.ParticipantConnected, function (p) {
            getOrCreateParticipant(p.identity).inRoom = true;
            ensureMember(p.identity, false);
            startTimer(); // первый собеседник вошёл — звонок состоялся
        });
        room.on(L.RoomEvent.ParticipantDisconnected, function (p) { removeParticipant(p); });

        var wantVideo = d.mediaType === BF.calls.MediaType.VIDEO;

        room.connect(d.livekitUrl, d.accessToken).then(function () {
            getOrCreateParticipant(room.localParticipant.identity).inRoom = true;
            ensureMember(room.localParticipant.identity, true);
            var remotes = room.remoteParticipants || room.participants;
            if (remotes && remotes.forEach) remotes.forEach(function (p) {
                getOrCreateParticipant(p.identity).inRoom = true;
                ensureMember(p.identity, false);
            });
            return room.localParticipant.setMicrophoneEnabled(true, micCaptureOpts(), audioPublishOpts());
        }).then(function () {
            micOn = true; renderControls();
            if (wantVideo) return room.localParticipant.setCameraEnabled(true, videoCaptureOpts(), videoPublishOpts());
        }).then(function () {
            if (wantVideo) { camOn = true; renderControls(); }
        }).catch(function (e) {
            console.error('[calls] LiveKit connect failed:', e);
            BF.calls.end(d.callId);
        });
    }

    function clearAllParticipants() {
        streamTiles = {};
        Object.keys(participants).forEach(function (id) {
            participants[id].audioEls.forEach(function (a) { try { a.remove(); } catch (e) {} });
        });
        participants = {};
        members = {}; memberOrder = [];
        heroIds = []; heroIsDefault = true; heroNodes = {};
        if (heroHoldTimer) { clearTimeout(heroHoldTimer); heroHoldTimer = null; }
        fullKey = null;
        [vstageEl, heroEl, stripEl, bgEl, audioHostEl].forEach(function (c) { if (c) c.innerHTML = ''; });
        if (screenEl) screenEl.classList.remove('video', 'full');
    }

    // --- Контрол-кнопки (иконки + состояние) ---
    function setIcon(btn, svg) { if (btn) btn.innerHTML = svg; }
    function renderControls() {
        setIcon(btnMic, micOn ? ICONS.mic : ICONS.micOff);
        btnMic.classList.toggle('active', micOn);
        btnMic.parentElement.classList.toggle('active', micOn);
        btnMic.title = BF.i18n.t(micOn ? 'call.mic.off' : 'call.mic.on');
        setIcon(btnCam, camOn ? ICONS.videoOff : ICONS.video);
        btnCam.classList.toggle('active', camOn);
        btnCam.parentElement.classList.toggle('active', camOn);
        btnCam.title = BF.i18n.t(camOn ? 'call.camera.off' : 'call.camera.on');
        setIcon(btnScreen, screenOn ? ICONS.monitorOff : ICONS.monitor);
        btnScreen.classList.toggle('active', screenOn);
        btnScreen.title = BF.i18n.t(screenOn ? 'call.screenShare.stop' : 'call.screenShare');
        updateQualityChips();
    }

    // --- Качество публикации + выбор устройства ---
    // Аудио: bps в audioPreset; Авто → дефолт SDK.
    function audioPublishOpts() {
        var br = AUDIO_BITRATE[currentAudioQuality];
        return br ? { audioPreset: { maxBitrate: br } } : undefined;
    }
    function micCaptureOpts() {
        return selectedMicId ? { deviceId: selectedMicId } : undefined;
    }
    function videoCaptureOpts() {
        var p = VIDEO_PRESET[currentVideoQuality];
        var o = {};
        if (p) o.resolution = { width: p.w, height: p.h, frameRate: p.fps };
        if (selectedCamId) o.deviceId = selectedCamId;
        return Object.keys(o).length ? o : undefined;
    }
    function videoPublishOpts() {
        var p = VIDEO_PRESET[currentVideoQuality];
        return p ? { videoEncoding: { maxBitrate: p.bitrate, maxFramerate: p.fps }, simulcast: false } : undefined;
    }

    // Применяем выбранное качество к своей публикации (republish off→on). Возвращаем промис для индикации.
    function applyAudioQuality() {
        if (!room || !micOn) return Promise.resolve(); // выключенный микрофон получит пресет при включении
        return room.localParticipant.setMicrophoneEnabled(false).then(function () {
            return room.localParticipant.setMicrophoneEnabled(true, micCaptureOpts(), audioPublishOpts());
        }).catch(function (e) { console.error('[calls] audio quality apply failed', e); });
    }
    function applyVideoQuality() {
        if (!room || !camOn) return Promise.resolve();
        return room.localParticipant.setCameraEnabled(false).then(function () {
            return room.localParticipant.setCameraEnabled(true, videoCaptureOpts(), videoPublishOpts());
        }).catch(function (e) { console.error('[calls] video quality apply failed', e); });
    }

    // Индикация «применяю качество» для того, кто меняет (смена идёт с задержкой).
    function refreshQualityBusy() {
        if (btnQuality) btnQuality.classList.toggle('busy', audioPending || videoPending);
    }
    function setAudioPending(b) {
        audioPending = b;
        if (audioQualityGroup) audioQualityGroup.classList.toggle('pending', b);
        refreshQualityBusy();
    }
    function setVideoPending(b) {
        videoPending = b;
        if (videoQualityGroup) videoQualityGroup.classList.toggle('pending', b);
        refreshQualityBusy();
    }

    // --- Выбор устройства (in-app пикеры микрофона/камеры) ---
    function listDevices(kind) {
        if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) return Promise.resolve([]);
        return navigator.mediaDevices.enumerateDevices().then(function (devs) {
            return devs.filter(function (d) { return d.kind === kind; });
        }).catch(function () { return []; });
    }
    function buildDeviceMenu(menu, kind, selectedId, fallback, onPick) {
        menu.innerHTML = '';
        listDevices(kind).then(function (devs) {
            menu.innerHTML = '';
            if (!devs.length) {
                var em = document.createElement('div');
                em.className = 'call-ctl-menu-empty'; em.textContent = BF.i18n.t('call.noDevices');
                menu.appendChild(em); return;
            }
            devs.forEach(function (d, i) {
                var item = document.createElement('button');
                item.className = 'call-ctl-menu-item';
                item.textContent = d.label || (fallback + ' ' + (i + 1));
                if (selectedId && d.deviceId === selectedId) item.classList.add('active');
                item.addEventListener('click', function (e) {
                    e.stopPropagation();
                    if (d.deviceId) onPick(d.deviceId);
                    menu.classList.remove('open');
                });
                menu.appendChild(item);
            });
        });
    }
    function closeDeviceMenus() {
        if (micMenu) micMenu.classList.remove('open');
        if (camMenu) camMenu.classList.remove('open');
    }
    function closeAllPopovers() { toggleQualityPanel(false); closeDeviceMenus(); }
    function openDeviceMenu(menu, kind, selectedId, fallback, onPick) {
        var willOpen = !menu.classList.contains('open');
        closeAllPopovers();
        if (willOpen) { buildDeviceMenu(menu, kind, selectedId, fallback, onPick); menu.classList.add('open'); }
    }
    function pickMic(id) {
        selectedMicId = id;
        if (room && micOn) room.switchActiveDevice('audioinput', id).catch(function () {});
    }
    function pickCam(id) {
        selectedCamId = id;
        if (room && camOn) room.switchActiveDevice('videoinput', id).catch(function () {});
    }

    function updateQualityChips() {
        if (!audioChips) return;
        var a = audioChips.querySelectorAll('.call-chip');
        for (var i = 0; i < a.length; i++) a[i].classList.toggle('active', Number(a[i].dataset.q) === currentAudioQuality);
        var v = videoChips.querySelectorAll('.call-chip');
        for (var j = 0; j < v.length; j++) v[j].classList.toggle('active', Number(v[j].dataset.q) === currentVideoQuality);
        // Видео-качество может менять только публикующий → группа видна, когда камера включена.
        videoQualityGroup.style.display = camOn ? '' : 'none';
    }
    function toggleQualityPanel(open) {
        var show = open != null ? open : !qualityPanel.classList.contains('open');
        qualityPanel.classList.toggle('open', show);
        if (show) updateQualityChips();
    }

    // --- Экран ожидания собеседника ---
    function setupWaiting(d) {
        waitingAvatar.innerHTML = '';
        if (d.isGroup) {
            waitingAvatar.textContent = '#';
            waitingName.textContent = BF.i18n.t('call.group');
            waitingSub.textContent = BF.i18n.t('call.waitingMembers');
        } else {
            waitingAvatar.textContent = '?';
            waitingName.textContent = BF.i18n.t('call.connecting');
            waitingSub.textContent = BF.i18n.t(d.role === 'caller' ? 'call.calling' : 'call.joining');
            if (d.peerUserId) {
                resolveUser(d.peerUserId).then(function (user) {
                    if (activeCallId !== d.callId) return;
                    waitingName.textContent = userName(user, BF.i18n.t('call.connecting'));
                    fillAvatar(waitingAvatar, user, waitingName.textContent);
                });
            }
        }
    }

    // --- Экран активного звонка ---
    function openScreen(d) {
        activeCallId = d.callId;
        titleEl.textContent = BF.i18n.t(d.isGroup ? 'call.group' : 'call.title');
        timerEl.textContent = d.role === 'caller' ? BF.i18n.t('call.calling') : '';
        micOn = false; camOn = false; screenOn = false;
        selectedMicId = null; selectedCamId = null;
        currentAudioQuality = d.audioQuality != null ? d.audioQuality : 0;
        currentVideoQuality = 0;
        setAudioPending(false); setVideoPending(false);
        closeAllPopovers();
        renderControls();
        setupWaiting(d);
        screenEl.classList.add('waiting');   // до подключения собеседника
        screenEl.classList.add('visible');
        connectLiveKit(d);
        if (!ro && window.ResizeObserver) ro = new ResizeObserver(function () { refreshScene(false); });
        if (ro) ro.observe(mainEl);
        onWinResize = function () { refreshScene(false); };
        addEventListener('resize', onWinResize);
        startRenderLoop();
        setTimeout(onWinResize, 60);
        if (d.role !== 'caller') startTimer();
    }

    function teardown() {
        stopTimer();
        stopRingtone();
        stopRenderLoop();
        if (onWinResize) { removeEventListener('resize', onWinResize); onWinResize = null; }
        if (ro) ro.disconnect();
        if (room) { try { room.disconnect(); } catch (e) {} room = null; }
        clearAllParticipants();
        screenEl.classList.remove('visible', 'waiting', 'waiting-empty', 'video', 'full');
        closeAllPopovers();
        setAudioPending(false); setVideoPending(false);
        activeCallId = null;
        micOn = camOn = screenOn = false;
        selectedMicId = null; selectedCamId = null;
        currentAudioQuality = 0; currentVideoQuality = 0;
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
            if (activeCallId === d.callId) {
                // Групповой заголовок не меняем — как и раньше, «В разговоре» только для 1-на-1
                if (titleEl.textContent === BF.i18n.t('call.title')) titleEl.textContent = BF.i18n.t('call.inProgress');
                startTimer();
            }
        });
        BF.calls.on('peer_rejected', function (d) {
            if (activeCallId === d.callId) teardown();
        });
        BF.calls.on('ring_dismiss', function (d) {
            dismissIncomingPermissionDialog(d.callId);
        });
        BF.calls.on('ended', function (d) {
            if (dismissIncomingPermissionDialog(d.callId)) return;
            if (activeCallId === d.callId || d.local) teardown();
        });
        BF.calls.on('member', function () { /* плитки ведёт LiveKit; событие справочное */ });
        BF.calls.on('audio_quality_changed', function (d) {
            if (activeCallId !== d.callId) return;
            currentAudioQuality = d.quality;     // единый источник истины — сервер
            updateQualityChips();
            applyAudioQuality().then(function () { // переопубликовать свой микрофон
                if (audioPending) setAudioPending(false);
            }).catch(function () { setAudioPending(false); });
        });
    }

    // --- Кнопки управления ---
    function bindControls() {
        ringAccept.addEventListener('click', function () {
            if (!ringCallId) return;
            var id = ringCallId;
            var call = BF.calls.getCurrent();
            ringAccept.disabled = true;
            ensureMediaPermissions(call ? call.mediaType : BF.calls.MediaType.AUDIO, id).then(function () {
                if (ringCallId !== id) return;
                stopRingtone();
                return BF.calls.accept(id);
            }).catch(function (e) {
                if (!e || (e.code !== 'media-permission-dismissed' && e.code !== 'incoming-call-dismissed')) {
                    console.error('accept failed', e);
                    if (ringCallId === id) hideRing();
                }
            }).finally(function () { ringAccept.disabled = false; });
        });
        ringReject.addEventListener('click', function () {
            if (!ringCallId) return;
            var id = ringCallId;
            BF.calls.reject(id);
            dismissIncomingPermissionDialog(id);
        });
        btnHangup.addEventListener('click', function () {
            if (activeCallId) BF.calls.end(activeCallId);
            else teardown();
        });
        btnMic.addEventListener('click', function () {
            if (!room) return;
            room.localParticipant.setMicrophoneEnabled(!micOn, micCaptureOpts(), audioPublishOpts()).then(function () {
                micOn = !micOn; renderControls();
            });
        });
        btnCam.addEventListener('click', function () {
            if (!room) return;
            room.localParticipant.setCameraEnabled(!camOn, videoCaptureOpts(), videoPublishOpts()).then(function () {
                camOn = !camOn; renderControls();
            });
        });
        btnScreen.addEventListener('click', function () {
            if (!room) return;
            room.localParticipant.setScreenShareEnabled(!screenOn).then(function () {
                screenOn = !screenOn; renderControls();
            }).catch(function (e) { console.error('screen share failed', e); });
        });
        // Esc сворачивает раскрытую на весь экран плитку.
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape' || !fullKey || !screenEl.classList.contains('visible')) return;
            e.preventDefault();
            fullKey = null;
            refreshScene();
        });

        // Выбор устройства (карет на кнопке микрофона/камеры).
        if (micCaret) micCaret.addEventListener('click', function (e) {
            e.stopPropagation();
            openDeviceMenu(micMenu, 'audioinput', selectedMicId, BF.i18n.t('call.mic'), pickMic);
        });
        if (camCaret) camCaret.addEventListener('click', function (e) {
            e.stopPropagation();
            openDeviceMenu(camMenu, 'videoinput', selectedCamId, BF.i18n.t('call.camera'), pickCam);
        });
        if (micMenu) micMenu.addEventListener('click', function (e) { e.stopPropagation(); });
        if (camMenu) camMenu.addEventListener('click', function (e) { e.stopPropagation(); });

        // Качество: кнопка-поповер; голос — через сервер, видео — локально.
        btnQuality.addEventListener('click', function (e) { e.stopPropagation(); closeDeviceMenus(); toggleQualityPanel(); });
        qualityPanel.addEventListener('click', function (e) { e.stopPropagation(); });
        screenEl.addEventListener('click', function () { closeAllPopovers(); });
        audioChips.addEventListener('click', function (e) {
            var btn = e.target.closest('.call-chip');
            if (!btn || !activeCallId) return;
            setAudioPending(true); // показать «применяю» инициатору (смена идёт с задержкой)
            BF.calls.setAudioQuality(activeCallId, Number(btn.dataset.q))
                .catch(function (err) { console.error('[calls] setAudioQuality failed', err); setAudioPending(false); });
            // UI и микрофон обновятся по broadcast-событию audio_quality_changed.
        });
        videoChips.addEventListener('click', function (e) {
            var btn = e.target.closest('.call-chip');
            if (!btn) return;
            currentVideoQuality = Number(btn.dataset.q);
            updateQualityChips();
            setVideoPending(true);
            applyVideoQuality().then(function () { setVideoPending(false); })
                .catch(function () { setVideoPending(false); });
        });
    }

    function init() {
        ringOverlay = $('callRingOverlay'); ringAvatar = $('callRingAvatar');
        ringName = $('callRingName'); ringSub = $('callRingSub');
        ringAccept = $('callRingAccept'); ringReject = $('callRingReject');
        permissionOverlay = $('callPermissionOverlay'); permissionTitle = $('callPermissionTitle');
        permissionText = $('callPermissionText'); permissionRequest = $('callPermissionRequest'); permissionCancel = $('callPermissionCancel');
        screenEl = $('callScreen'); mainEl = $('callMain'); heroEl = $('callHero'); vstageEl = $('callVStage');
        stripEl = $('callStrip'); bgEl = $('callBg');
        titleEl = $('callScreenTitle'); timerEl = $('callScreenTimer');
        btnMic = $('callToggleMic'); btnCam = $('callToggleCam');
        btnScreen = $('callToggleScreen'); btnHangup = $('callHangup');
        waitingAvatar = $('callWaitingAvatar'); waitingName = $('callWaitingName'); waitingSub = $('callWaitingSub');
        btnQuality = $('callToggleQuality'); qualityPanel = $('callQualityPanel');
        audioChips = $('callAudioChips'); videoChips = $('callVideoChips');
        videoQualityGroup = $('callVideoQualityGroup'); audioQualityGroup = $('callAudioQualityGroup');
        micCaret = $('callMicCaret'); micMenu = $('callMicMenu');
        camCaret = $('callCamCaret'); camMenu = $('callCamMenu');
        if (!ringOverlay || !screenEl || !permissionOverlay) return; // нет разметки — модуль неактивен

        audioHostEl = document.createElement('div');
        audioHostEl.style.cssText = 'position:fixed;width:0;height:0;overflow:hidden;';
        document.body.appendChild(audioHostEl);

        setIcon(btnHangup, ICONS.phoneEnd);
        setIcon(btnQuality, ICONS.quality);
        setIcon(micCaret, ICONS.chevron);
        setIcon(camCaret, ICONS.chevron);
        renderControls();
        bindPermissionDialog();
        bindControls();
        bindCallEvents();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.BF.callsUI = { teardown: teardown, ensureMediaPermissions: ensureMediaPermissions };
})();
