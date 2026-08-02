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
        chevron: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 14 12 8 18 14"/></svg>',
        expand: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 3 21 3 21 9"/><polyline points="9 21 3 21 3 15"/><line x1="21" y1="3" x2="14" y2="10"/><line x1="3" y1="21" x2="10" y2="14"/></svg>'
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
    var screenEl, stageEl, gridEl, voicesEl, titleEl, timerEl, btnMic, btnCam, btnScreen, btnHangup;
    var spotlightEl, spotlightVideoHost, spotlightCanvasEl, spotlightRingEl, spotlightNameEl, spotlightExitBtn;
    var waitingAvatar, waitingName, waitingSub;
    var btnQuality, qualityPanel, audioChips, videoChips, videoQualityGroup, audioQualityGroup;
    var micCaret, micMenu, camCaret, camMenu;
    var audioHostEl = null; // постоянный скрытый контейнер для <audio>/<video> чужих аудиотреков

    // --- LiveKit ---
    var room = null;
    var streamTiles = {};      // key (camKey/screenKey) -> tile с видео (сетка/рельс)
    var voiceTiles = {};       // identity -> компактная плитка без видео
    var participants = {};     // identity -> { bands, wake, speaking, analyser, hasAudio, slots[], audioEls[], inRoom }
    var spotlightKey = null;   // ключ стрим-тайла, сейчас в spotlight
    var spotlightSlot = null;  // персистентный WebGL-слот canvas'а spotlight'а
    var micOn = false, camOn = false, screenOn = false;

    // --- Частотный анализ голоса (WebAudio, только реальные треки — без симуляции) ---
    var SPEAK_LEVEL_THRESHOLD = 0.1;
    var rafId = null, lastFrameTs = 0, timeAccum = 0;
    var ro = null; // общий ResizeObserver на канвасы + контейнеры сетки

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
    // WebGL2 — визуализация спектра голоса (перенос 1:1 из Call.dc.html)
    // ============================================================
    function shaderSources() {
        var vertex = '#version 300 es\nvoid main(){vec2 v=vec2((gl_VertexID<<1)&2,gl_VertexID&2);gl_Position=vec4(v*2.0-1.0,0.0,1.0);}';
        var fragment = [
            '#version 300 es', 'precision highp float;',
            'uniform vec2 uRes; uniform float uTime,uLow,uMid,uHigh,uLevel,uWake;',
            'out vec4 outColor; const float PI=3.14159265359;',
            'vec3 spectral(float n){return n<.5?vec3(.26,.18,1.):n<1.5?vec3(.74,.17,.96):n<2.5?vec3(1.,.22,.52):vec3(1.,.66,.22);}',
            'float wave(float x,float amp,float env,float drift,float shift,float harm){return amp*env*(1.+.14*sin(x*.42-drift*.6))*(sin(x*1.1+drift+shift)+harm*sin(x*2.53+drift*1.6+shift*1.5+1.7));}',
            'vec3 ribbon(vec2 p,float aspect,float amp,float spread,float drift,float harm){',
            ' float xN=p.x/max(aspect,1.); float env=cos(PI*.5*min(abs(.92*xN),1.)); env*=env;',
            ' float thick=(.020+.016*(1.-.55*clamp(abs(xN)*.75,0.,1.)))*(1.+.35*uMid);',
            ' float soft=.020+.012*uMid, intensity=.019*(1.+.7*uLevel);',
            ' float main=wave(p.x,amp,env,drift,0.,harm); vec3 color=vec3(0.); vec3 hues=vec3(0.);',
            ' for(int i=0;i<4;i++){ float fi=float(i); vec3 hue=spectral(fi); hues+=hue;',
            '  float y=wave(p.x,amp+.03*uMid,env,drift,mix(-spread,spread,fi/3.),harm); float d=abs(p.y-y);',
            '  float line=intensity/(sqrt(d*d+soft*soft)+thick)*exp(-d*d);',
            '  float lo=min(main,y),hi=max(main,y),outside=max(0.,max(p.y-hi,lo-p.y));',
            '  float band=4.9*intensity*exp(-outside/.08); color+=hue*(line+band);}',
            ' color/=max((hues.r+hues.g+hues.b)/3.,.0001);',
            ' float dm=abs(p.y-main); color+=.42*intensity/(sqrt(dm*dm+soft*soft)+thick); return color;}',
            'float hash(vec2 p){return fract(sin(dot(p,vec2(12.9898,78.233)))*43758.5453);}',
            'void main(){',
            ' float aspect=uRes.x/uRes.y; vec2 p=(gl_FragCoord.xy+.5)*2./uRes-1.; float ndcY=p.y; p.x*=aspect/.62; p.y/=.62;',
            ' float amp=mix(.12,.80+.45*uLow,uWake); float spread=mix(.6,3.3+1.6*uHigh+.6*uMid,uWake); float harm=mix(.1,.34+.22*uHigh,uWake);',
            ' float xN=p.x/max(aspect,1.); vec3 col=ribbon(p,aspect,amp,spread,uTime*mix(.9,2.1,uWake),harm);',
            ' col=pow(max(col,0.),vec3(1.12)); col*=1.35; col*=exp(-pow(xN*1.25,2.)); col*=1.-smoothstep(.62,.99,abs(ndcY));',
            ' col+=(hash(gl_FragCoord.xy)-.5)/255.; outColor=vec4(col,1.);}'
        ].join('\n');
        return { vertex: vertex, fragment: fragment };
    }

    function createGlProgram(gl) {
        var src = shaderSources();
        function mk(type, code) {
            var s = gl.createShader(type);
            gl.shaderSource(s, code); gl.compileShader(s);
            if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) console.error(gl.getShaderInfoLog(s));
            return s;
        }
        var prog = gl.createProgram();
        gl.attachShader(prog, mk(gl.VERTEX_SHADER, src.vertex));
        gl.attachShader(prog, mk(gl.FRAGMENT_SHADER, src.fragment));
        gl.linkProgram(prog);
        if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) { console.error(gl.getProgramInfoLog(prog)); return null; }
        gl.useProgram(prog);
        var u = {};
        ['uRes', 'uTime', 'uLow', 'uMid', 'uHigh', 'uLevel', 'uWake'].forEach(function (n) { u[n] = gl.getUniformLocation(prog, n); });
        return { prog: prog, u: u };
    }

    function sizeCanvas(el, gl) {
        if (!el || !gl) return;
        var dpr = Math.min(window.devicePixelRatio || 1, 2);
        var r = el.getBoundingClientRect();
        if (!r.width || !r.height) return;
        var w = Math.round(r.width * dpr), h = Math.round(r.height * dpr);
        if (el.width !== w || el.height !== h) { el.width = w; el.height = h; }
        gl.viewport(0, 0, el.width, el.height);
    }

    function registerCanvasSlot(canvasEl) {
        var gl = canvasEl.getContext('webgl2', { antialias: false, alpha: false, powerPreference: 'low-power' });
        if (!gl) return { canvas: canvasEl, gl: null, prog: null, u: null, ring: null };
        var pu = createGlProgram(gl);
        if (!pu) return { canvas: canvasEl, gl: null, prog: null, u: null, ring: null };
        sizeCanvas(canvasEl, gl);
        return { canvas: canvasEl, gl: gl, prog: pu.prog, u: pu.u, ring: null };
    }

    function drawShaderSlot(slot, bands, wake, time) {
        var gl = slot.gl;
        if (!gl) return;
        gl.useProgram(slot.prog);
        gl.uniform2f(slot.u.uRes, slot.canvas.width, slot.canvas.height);
        gl.uniform1f(slot.u.uTime, time);
        gl.uniform1f(slot.u.uLow, bands.low);
        gl.uniform1f(slot.u.uMid, bands.mid);
        gl.uniform1f(slot.u.uHigh, bands.high);
        gl.uniform1f(slot.u.uLevel, bands.level);
        gl.uniform1f(slot.u.uWake, wake);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    function observeEl(el) {
        if (!el) return;
        if (!ro) ro = new ResizeObserver(function () { resizeAllCanvases(); updateLayout(); });
        ro.observe(el);
    }

    function resizeAllCanvases() {
        Object.keys(participants).forEach(function (id) {
            participants[id].slots.forEach(function (s) { sizeCanvas(s.canvas, s.gl); });
        });
    }

    // ============================================================
    // Частотный анализ (перенос micBands/follow из Call.dc.html)
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

    function readBands(p) {
        return p.analyser ? readFreqBands(p.analyser) : [0, 0, 0];
    }

    function getOrCreateParticipant(identity) {
        if (!participants[identity]) {
            participants[identity] = {
                identity: identity,
                bands: { low: 0, mid: 0, high: 0, level: 0 },
                wake: 0, speaking: false, audioState: undefined,
                analyser: null, hasAudio: false,
                audioEls: [], slots: [],
                seed: Math.random() * 7, inRoom: true
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
        } catch (e) { /* WebAudio недоступен — визуализации не будет, не критично */ }
    }
    function detachMeter(identity) {
        var p = participants[identity];
        if (!p || !p.analyser) { if (p) p.hasAudio = false; return; }
        try { p.analyser.src.disconnect(); } catch (e) {}
        p.analyser = null;
        p.hasAudio = false;
        p.bands.low = p.bands.mid = p.bands.high = p.bands.level = 0;
        p.wake = 0; p.speaking = false;
    }

    // ============================================================
    // Рендер-цикл (перенос componentDidMount/step из Call.dc.html)
    // ============================================================
    function stepAll(dt) {
        timeAccum += dt;
        Object.keys(participants).forEach(function (id) {
            var p = participants[id];
            var v = readBands(p);
            var b = p.bands;
            b.low = follow(b.low, v[0], dt, 9, 3.5);
            b.mid = follow(b.mid, v[1], dt, 10, 4);
            b.high = follow(b.high, v[2], dt, 11, 4.5);
            b.level = follow(b.level, (v[0] + v[1] + v[2]) * 0.42, dt, 8, 3);
            p.wake = follow(p.wake, b.level > 0.05 ? Math.min(1, 0.55 + b.level * 1.4) : 0, dt, 4.5, 1.4);

            var speaking = b.level > SPEAK_LEVEL_THRESHOLD;
            p.speaking = speaking;

            var audioState = !p.hasAudio ? 'off' : (speaking ? 'speaking' : 'online');
            if (audioState !== p.audioState) {
                p.audioState = audioState;
                updateParticipantAudioUI(id, audioState);
            }

            var ringOpacity = speaking ? String(0.5 + 0.5 * Math.min(1, b.level * 2)) : '0';
            var visTarget = p.hasAudio ? '0.96' : '0';
            p.slots.forEach(function (slot) {
                if (slot.ring && slot.ring.style.opacity !== ringOpacity) slot.ring.style.opacity = ringOpacity;
                if (slot.canvas.dataset.vis !== visTarget) { slot.canvas.dataset.vis = visTarget; slot.canvas.style.opacity = visTarget; }
                if (!p.hasAudio || !slot.gl) return;
                drawShaderSlot(slot, b, p.wake, timeAccum + p.seed * 5);
            });
        });
    }

    function updateParticipantAudioUI(identity, state) {
        var vt = voiceTiles[identity];
        if (vt) {
            vt.statusEl.textContent = state === 'off' ? BF.i18n.t('call.status.micOff')
                : (state === 'speaking' ? BF.i18n.t('call.status.speaking') : BF.i18n.t('status.online'));
            vt.mutedEl.style.display = state === 'off' ? 'flex' : 'none';
        }
        [camKey(identity), screenKey(identity)].forEach(function (key) {
            var st = streamTiles[key];
            if (st && st.mutedIconEl) st.mutedIconEl.style.display = state === 'off' ? 'flex' : 'none';
        });
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

    // ============================================================
    // Плитки: голосовые (без видео) и стрим (камера/экран — равноправные)
    // ============================================================
    function camKey(identity) { return identity; }
    function screenKey(identity) { return identity + '#screen'; }

    function hasAnyStreamTile(identity) {
        return Object.keys(streamTiles).some(function (k) { return streamTiles[k].identity === identity; });
    }

    function ensureVoiceTile(identity, isLocal) {
        if (voiceTiles[identity] || hasAnyStreamTile(identity)) return voiceTiles[identity] || null;
        var el = document.createElement('div'); el.className = 'call-voice-tile';
        var ring = document.createElement('div'); ring.className = 'call-voice-ring';
        var body = document.createElement('div'); body.className = 'call-voice-body';
        var avatar = document.createElement('div'); avatar.className = 'call-voice-avatar';
        var name = document.createElement('div'); name.className = 'call-voice-name';
        var status = document.createElement('div'); status.className = 'call-voice-status';
        body.appendChild(avatar); body.appendChild(name); body.appendChild(status);
        var canvas = document.createElement('canvas'); canvas.className = 'call-voice-canvas';
        var muted = document.createElement('div'); muted.className = 'call-voice-muted'; muted.innerHTML = ICONS.micOff;
        el.appendChild(ring); el.appendChild(body); el.appendChild(canvas); el.appendChild(muted);
        voicesEl.appendChild(el);

        var base = isLocal ? BF.i18n.t('call.you') : ('#' + identity);
        name.textContent = base;
        avatar.style.background = avatarColor(identity);
        avatar.textContent = base.charAt(0).toUpperCase();
        status.textContent = BF.i18n.t('call.status.micOff');

        var tile = { el: el, ringEl: ring, canvasEl: canvas, nameEl: name, statusEl: status, avatarEl: avatar, mutedEl: muted, identity: identity, isLocal: !!isLocal };
        voiceTiles[identity] = tile;

        resolveUser(isLocal ? getMyUserId() : Number(identity)).then(function (user) {
            if (!voiceTiles[identity]) return;
            var nm = userName(user, base);
            name.textContent = nm;
            fillAvatar(avatar, user, nm);
        });

        var p = getOrCreateParticipant(identity);
        var slot = registerCanvasSlot(canvas);
        slot.ring = ring;
        p.slots.push(slot);
        observeEl(canvas);
        updateLayout();
        return tile;
    }
    function removeVoiceTile(identity) {
        var tile = voiceTiles[identity];
        if (!tile) return;
        var p = participants[identity];
        if (p) p.slots = p.slots.filter(function (s) { return s.canvas !== tile.canvasEl; });
        try { tile.el.remove(); } catch (e) {}
        delete voiceTiles[identity];
        updateLayout();
    }

    function ensureStreamTile(identity, isLocal, kind) {
        var key = kind === 'screen' ? screenKey(identity) : camKey(identity);
        if (streamTiles[key]) return streamTiles[key];
        removeVoiceTile(identity);

        var el = document.createElement('div');
        el.className = 'call-tile' + (kind === 'screen' ? ' screen' : '');
        var ring = document.createElement('div'); ring.className = 'call-tile-ring';
        var canvas = document.createElement('canvas'); canvas.className = 'call-tile-canvas';
        var pill = document.createElement('div'); pill.className = 'call-tile-pill';
        var nameSpan = document.createElement('span'); nameSpan.className = 'call-tile-name';
        var mutedIcon = document.createElement('span'); mutedIcon.className = 'call-tile-muted'; mutedIcon.innerHTML = ICONS.micOff; mutedIcon.style.display = 'none';
        pill.appendChild(nameSpan); pill.appendChild(mutedIcon);
        var hint = document.createElement('div'); hint.className = 'call-tile-hint'; hint.innerHTML = ICONS.expand;
        el.appendChild(ring); el.appendChild(pill); el.appendChild(hint); el.appendChild(canvas);
        el.addEventListener('click', function (e) { e.stopPropagation(); enterSpotlight(key); });
        gridEl.appendChild(el);

        var base = isLocal ? BF.i18n.t('call.you') : ('#' + identity);
        nameSpan.textContent = kind === 'screen' ? BF.i18n.t('call.tile.screen', { name: base }) : base;

        var tile = {
            el: el, ringEl: ring, canvasEl: canvas, nameEl: nameSpan, mutedIconEl: mutedIcon,
            videoEl: null, track: null, identity: identity, isLocal: !!isLocal, screen: kind === 'screen'
        };
        streamTiles[key] = tile;

        resolveUser(isLocal ? getMyUserId() : Number(identity)).then(function (user) {
            if (!streamTiles[key]) return;
            var nm = userName(user, base);
            tile.nameEl.textContent = kind === 'screen' ? BF.i18n.t('call.tile.screen', { name: nm }) : nm;
            if (spotlightKey === key) spotlightNameEl.textContent = tile.nameEl.textContent;
        });

        var p = getOrCreateParticipant(identity);
        var slot = registerCanvasSlot(canvas);
        slot.ring = ring;
        p.slots.push(slot);
        observeEl(canvas);
        updateLayout();
        return tile;
    }
    function removeStreamTile(key) {
        var tile = streamTiles[key];
        if (!tile) return;
        if (spotlightKey === key) exitSpotlight(true);
        var p = participants[tile.identity];
        if (p) p.slots = p.slots.filter(function (s) { return s.canvas !== tile.canvasEl; });
        try { tile.el.remove(); } catch (e) {}
        delete streamTiles[key];
        updateLayout();
        var identity = tile.identity, pp = participants[identity];
        if (!hasAnyStreamTile(identity) && pp && pp.inRoom) ensureVoiceTile(identity, tile.isLocal);
    }

    function setTileVideo(tile, track, mirror) {
        if (!tile) return;
        var videoEl = track.attach();
        videoEl.autoplay = true; videoEl.playsInline = true;
        if (tile.isLocal) videoEl.muted = true;
        if (mirror) videoEl.style.transform = 'scaleX(-1)';
        if (tile.videoEl) { try { tile.videoEl.remove(); } catch (e) {} }
        tile.videoEl = videoEl; tile.track = track;
        tile.el.insertBefore(videoEl, tile.ringEl.nextSibling);
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
        removeVoiceTile(identity);
        if (p) p.audioEls.forEach(function (a) { try { a.remove(); } catch (e) {} });
        delete participants[identity];
    }

    // --- Spotlight + рельс (клик по стрим-тайлу переносит его в крупный блок) ---
    function enterSpotlight(key) {
        var tile = streamTiles[key];
        if (!tile || key === spotlightKey) return;
        if (spotlightKey) exitSpotlight(false);
        spotlightKey = key;
        spotlightVideoHost.appendChild(tile.videoEl);
        spotlightNameEl.textContent = tile.nameEl.textContent;
        tile.el.style.display = 'none';
        var p = getOrCreateParticipant(tile.identity);
        spotlightSlot.ring = spotlightRingEl;
        p.slots.push(spotlightSlot);
        screenEl.classList.add('has-spotlight');
        spotlightEl.classList.add('visible');
        updateLayout();
        setTimeout(function () { sizeCanvas(spotlightCanvasEl, spotlightSlot.gl); }, 60);
    }
    function exitSpotlight(forRemoval) {
        if (!spotlightKey) return;
        var key = spotlightKey, tile = streamTiles[key];
        spotlightKey = null;
        if (tile) {
            if (!forRemoval && tile.videoEl) tile.el.insertBefore(tile.videoEl, tile.ringEl.nextSibling);
            tile.el.style.display = '';
        }
        Object.keys(participants).forEach(function (id) {
            participants[id].slots = participants[id].slots.filter(function (s) { return s !== spotlightSlot; });
        });
        screenEl.classList.remove('has-spotlight');
        spotlightEl.classList.remove('visible');
        updateLayout();
    }

    function isScreenShare(track, pub) {
        var SS = window.LivekitClient.Track.Source.ScreenShare;
        return (pub && pub.source === SS) || (track && track.source === SS);
    }

    // --- Адаптивная сетка стримов + плотность голосового ряда (layout/applyDensity) ---
    function applyDensity() {
        var tight = !!spotlightKey || window.innerHeight < 640;
        screenEl.classList.toggle('tight', tight);
    }
    function updateLayout() {
        applyDensity();
        if (!screenEl) return;
        var hasAny = Object.keys(streamTiles).length > 0 || Object.keys(voiceTiles).length > 0;
        var hasRemote = Object.keys(streamTiles).some(function (k) { return !streamTiles[k].isLocal; })
            || Object.keys(voiceTiles).some(function (k) { return !voiceTiles[k].isLocal; });
        screenEl.classList.toggle('waiting', !hasRemote);
        screenEl.classList.toggle('waiting-empty', !hasAny);
        if (!gridEl || !stageEl) return;
        var keys = Object.keys(streamTiles).filter(function (k) { return k !== spotlightKey; });
        var n = keys.length, gap = 12, aspect = 16 / 9;
        if (spotlightKey) {
            stageEl.style.flex = '0 0 auto';
            if (!n) { gridEl.style.gridTemplateColumns = ''; return; }
            var railH = Math.max(76, Math.min(124, stageEl.parentElement.clientHeight * 0.22));
            gridEl.style.gridTemplateColumns = 'repeat(' + n + ', minmax(0, ' + Math.round(railH * aspect) + 'px))';
            return;
        }
        stageEl.style.flex = '1';
        var W = stageEl.clientWidth, H = stageEl.clientHeight;
        if (!W || !H || !n) return;
        var best = 0, cols = 1;
        for (var c = 1; c <= n; c++) {
            var r = Math.ceil(n / c);
            var byW = (W - gap * (c - 1)) / c;
            var byH = ((H - gap * (r - 1)) / r) * aspect;
            var tileSize = Math.min(byW, byH);
            if (tileSize > best) { best = tileSize; cols = c; }
        }
        var size = Math.max(180, Math.min(best, 920));
        gridEl.style.gridTemplateColumns = 'repeat(' + cols + ', ' + Math.floor(size) + 'px)';
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
            if (!hasAnyStreamTile(participant.identity)) ensureVoiceTile(participant.identity, false);
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
            ensureVoiceTile(p.identity, false);
            startTimer(); // первый собеседник вошёл — звонок состоялся
        });
        room.on(L.RoomEvent.ParticipantDisconnected, function (p) { removeParticipant(p); });

        var wantVideo = d.mediaType === BF.calls.MediaType.VIDEO;

        room.connect(d.livekitUrl, d.accessToken).then(function () {
            getOrCreateParticipant(room.localParticipant.identity).inRoom = true;
            ensureVoiceTile(room.localParticipant.identity, true);
            var remotes = room.remoteParticipants || room.participants;
            if (remotes && remotes.forEach) remotes.forEach(function (p) {
                getOrCreateParticipant(p.identity).inRoom = true;
                ensureVoiceTile(p.identity, false);
            });
            updateLayout();
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
        Object.keys(streamTiles).forEach(function (k) { try { streamTiles[k].el.remove(); } catch (e) {} });
        streamTiles = {};
        Object.keys(voiceTiles).forEach(function (k) { try { voiceTiles[k].el.remove(); } catch (e) {} });
        voiceTiles = {};
        Object.keys(participants).forEach(function (id) {
            participants[id].audioEls.forEach(function (a) { try { a.remove(); } catch (e) {} });
        });
        participants = {};
        spotlightKey = null;
        if (spotlightVideoHost) spotlightVideoHost.innerHTML = '';
        if (spotlightEl) spotlightEl.classList.remove('visible');
        if (screenEl) screenEl.classList.remove('has-spotlight');
        if (audioHostEl) audioHostEl.innerHTML = '';
    }

    // --- Контрол-кнопки (иконки + состояние) ---
    function setIcon(btn, svg) { if (btn) btn.innerHTML = svg; }
    function renderControls() {
        setIcon(btnMic, micOn ? ICONS.mic : ICONS.micOff);
        btnMic.classList.toggle('active', micOn);
        btnMic.title = BF.i18n.t(micOn ? 'call.mic.off' : 'call.mic.on');
        setIcon(btnCam, camOn ? ICONS.videoOff : ICONS.video);
        btnCam.classList.toggle('active', camOn);
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
        observeEl(stageEl); observeEl(gridEl);
        onWinResize = function () { resizeAllCanvases(); updateLayout(); };
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
        if (room) { try { room.disconnect(); } catch (e) {} room = null; }
        clearAllParticipants();
        screenEl.classList.remove('visible', 'waiting', 'has-spotlight', 'tight', 'waiting-empty');
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
        if (spotlightExitBtn) spotlightExitBtn.addEventListener('click', function (e) { e.stopPropagation(); exitSpotlight(false); });

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
        screenEl = $('callScreen'); stageEl = $('callStage'); gridEl = $('callGrid'); voicesEl = $('callVoices');
        titleEl = $('callScreenTitle'); timerEl = $('callScreenTimer');
        spotlightEl = $('callSpotlight'); spotlightVideoHost = $('callSpotlightVideoHost');
        spotlightCanvasEl = $('callSpotlightCanvas'); spotlightRingEl = $('callSpotlightRing');
        spotlightNameEl = $('callSpotlightName'); spotlightExitBtn = $('callSpotlightExit');
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

        spotlightSlot = registerCanvasSlot(spotlightCanvasEl);

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
