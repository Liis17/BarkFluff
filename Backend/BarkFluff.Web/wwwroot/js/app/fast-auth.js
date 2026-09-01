/**
 * Fast-Auth (QR-логин) для страницы входа.
 *
 * Поток (тот же, что в WPF Login.xaml.cs и macOS FastAuthViewModel):
 *   1. GenerateFastAuthToken(QR) — анонимный запрос, метаданные устройства уходят в gRPC headers.
 *      В ответе PNG (base64) и fast_auth_id.
 *   2. SubscribeFastAuthResult(fast_auth_id) — server-streaming, ждём финальный статус.
 *      ACCEPTED → сохранить access/refresh токены, навигация на /messenger.
 *      REJECTED → toast в статусной строке + auto-restart.
 *      EXPIRED  → молча auto-restart.
 *
 * Вызывается из login-page.js: BF.fastAuth.start() при инициализации,
 * BF.fastAuth.cancel() при уходе со страницы / переходе на OTP.
 *
 * Requires: barkfluff.bundle.js, BF.metadata, BF.tokens, BF.network, window.proto
 * Exposes: BF.fastAuth
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var INITIAL_BACKOFF = 2000;
    var MAX_BACKOFF = 30000;

    // Анонимный клиент для fast-auth (без auth-interceptor) — создаём локально,
    // как auth.js создаёт IdentityApiClient. Не зависит от clients.js.
    // Клиент строится лениво: на шелле ноду выбирают на этой же странице.
    var bf = window.barkfluff;
    var cache = { origin: null, client: null };
    function fastAuthClient() {
        var origin = BF.node.origin();
        if (!bf || !bf.FastAuthApiClient || !origin) return null;
        if (cache.origin !== origin) cache = { origin: origin, client: new bf.FastAuthApiClient(origin) };
        return cache.client;
    }

    var stream = null;
    var countdownTimer = null;
    var restartTimer = null;
    var backoff = INITIAL_BACKOFF;

    var currentFastAuthId = null;
    var started = false;
    var generating = false;

    var els = null;

    function bindEls() {
        if (els) return els;
        els = {
            card: document.getElementById('fastAuthCard'),
            img: document.getElementById('fastAuthImg'),
            spinner: document.getElementById('fastAuthSpinner'),
            status: document.getElementById('fastAuthStatus')
        };
        return els;
    }

    function setStatus(text, kind) {
        var e = bindEls();
        if (!e.status) return;
        e.status.textContent = text || '';
        e.status.classList.remove('qr-status--ok', 'qr-status--err');
        if (kind === 'ok') e.status.classList.add('qr-status--ok');
        else if (kind === 'err') e.status.classList.add('qr-status--err');
    }

    function showLoading() {
        var e = bindEls();
        if (e.img) {
            e.img.onload = null;
            e.img.classList.remove('visible');
            e.img.setAttribute('aria-hidden', 'true');
        }
        if (e.spinner) e.spinner.classList.remove('is-hidden');
    }

    function showQr(pngBase64) {
        var e = bindEls();
        if (!e.img) return;
        var qrSrc = 'data:image/png;base64,' + pngBase64;
        e.img.onload = function () {
            if (!started || e.img.getAttribute('src') !== qrSrc) return;
            e.img.onload = null;
            e.img.classList.add('visible');
            e.img.setAttribute('aria-hidden', 'false');
            if (e.spinner) e.spinner.classList.add('is-hidden');
        };
        e.img.src = qrSrc;
    }

    function clearTimers() {
        if (countdownTimer) { clearInterval(countdownTimer); countdownTimer = null; }
        if (restartTimer) { clearTimeout(restartTimer); restartTimer = null; }
    }

    function cancelStream() {
        if (stream) { try { stream.cancel(); } catch (e) {} stream = null; }
    }

    function scheduleRestart(delay) {
        if (!started) return;
        if (restartTimer) clearTimeout(restartTimer);
        restartTimer = setTimeout(function () {
            restartTimer = null;
            if (started) startSession();
        }, delay);
    }

    function startCountdown(expiresAtMs) {
        if (countdownTimer) clearInterval(countdownTimer);
        function tick() {
            var remaining = Math.max(0, Math.floor((expiresAtMs - Date.now()) / 1000));
            // Обновляем только если статус ещё в режиме ожидания скана
            var e = bindEls();
            if (e.status && e.status.dataset.phase === 'pending') {
                e.status.textContent = remaining > 0
                    ? BF.i18n.t('qr.validFor', { seconds: remaining })
                    : BF.i18n.t('qr.expired');
            }
            if (remaining <= 0) {
                clearInterval(countdownTimer);
                countdownTimer = null;
            }
        }
        tick();
        countdownTimer = setInterval(tick, 1000);
    }

    function setPhase(phase) {
        var e = bindEls();
        if (e.status) e.status.dataset.phase = phase;
    }

    /**
     * Шаг 1: запросить новый QR-токен.
     */
    function generateToken() {
        var pkg = window.proto && window.proto.barkfluff
            && window.proto.barkfluff.fast && window.proto.barkfluff.fast.auth;
        var client = fastAuthClient();
        if (!pkg || !client) return Promise.resolve({ ok: false, err: 'fastauth_unavailable' });
        var req = new pkg.GenerateFastAuthTokenRequest();
        req.setFormat(pkg.TokenFormat.TOKEN_FORMAT_QR);
        var meta = BF.metadata.build(); // без auth-токена
        return BF.network.unary(
            client.generateFastAuthToken.bind(client),
            req,
            meta,
            BF.network.POLICIES.MUTATION
        ).then(function (resp) {
                if (!resp) return { ok: false, err: 'empty_response' };
                var token = resp.getToken();
                var expiresAt = resp.getExpiresAt();
                return {
                    ok: true,
                    pngBase64: token ? token.getValue() : '',
                    fastAuthId: resp.getFastAuthId(),
                    expiresAtMs: expiresAt ? expiresAt.toDate().getTime() : (Date.now() + 5 * 60 * 1000)
                };
            }).catch(function (err) { return { ok: false, err: err }; });
    }

    /**
     * Шаг 2: подписаться на событие подтверждения/отказа.
     */
    function subscribeResult(fastAuthId) {
        var pkg = window.proto.barkfluff.fast.auth;
        var meta = BF.metadata.build();
        var req = new pkg.SubscribeFastAuthResultRequest();
        req.setFastAuthId(fastAuthId);

        cancelStream();
        var client = fastAuthClient();
        if (!client) return;
        stream = client.subscribeFastAuthResult(req, meta);

        stream.on('data', function (evt) {
            backoff = INITIAL_BACKOFF;
            var status = evt.getStatus();
            handleStatus(status, evt);
        });

        stream.on('error', function () {
            // Сетевая ошибка / разрыв до финального статуса — переподключаемся с backoff
            scheduleRestart(backoff);
            backoff = Math.min(backoff * 2, MAX_BACKOFF);
        });

        stream.on('end', function () {
            // Если стрим закрылся без финального статуса — пробуем заново.
            // (Финальные статусы сами вызывают startSession() через handleStatus.)
            if (started && currentFastAuthId === fastAuthId) {
                scheduleRestart(INITIAL_BACKOFF);
            }
        });
    }

    function handleStatus(status, evt) {
        var pkg = window.proto.barkfluff.fast.auth;
        var FS = pkg.FastAuthStatus;
        switch (status) {
            case FS.FAST_AUTH_STATUS_PENDING:
                // ничего не меняем — countdown сам обновляет
                break;

            case FS.FAST_AUTH_STATUS_SCANNED:
                setPhase('scanned');
                if (countdownTimer) { clearInterval(countdownTimer); countdownTimer = null; }
                setStatus(BF.i18n.t('qr.scanned'), 'ok');
                break;

            case FS.FAST_AUTH_STATUS_ACCEPTED:
                setPhase('accepted');
                clearTimers();
                cancelStream();
                started = false;
                setStatus(BF.i18n.t('qr.confirmed'), 'ok');
                var data = {
                    accessToken: evt.getAccessToken(),
                    accessTokenExpiration: evt.getAccessTokenExpiresAt()
                        ? evt.getAccessTokenExpiresAt().toDate().getTime() : 0,
                    refreshToken: evt.getRefreshToken(),
                    refreshTokenExpiration: evt.getRefreshTokenExpiresAt()
                        ? evt.getRefreshTokenExpiresAt().toDate().getTime() : 0
                };
                if (!data.accessToken || !data.refreshToken) {
                    setStatus(BF.i18n.t('qr.emptyTokens'), 'err');
                    return;
                }
                // Tempmode-чекбокс не используется для QR-входа (по аналогии с WPF/macOS).
                BF.tokens.setTempMode(false);
                BF.tokens.save(data);
                window.location.href = '/messenger';
                break;

            case FS.FAST_AUTH_STATUS_REJECTED:
                setPhase('rejected');
                clearTimers();
                cancelStream();
                setStatus(BF.i18n.t('qr.rejected'), 'err');
                scheduleRestart(1000);
                break;

            case FS.FAST_AUTH_STATUS_EXPIRED:
                setPhase('expired');
                clearTimers();
                cancelStream();
                setStatus(BF.i18n.t('qr.expired'));
                scheduleRestart(500);
                break;

            default:
                break;
        }
    }

    function startSession() {
        if (generating) return;
        if (!started) return;
        generating = true;

        // Сбрасываем предыдущее состояние
        cancelStream();
        clearTimers();
        currentFastAuthId = null;
        showLoading();
        setPhase('loading');
        setStatus(BF.i18n.t('qr.loading'));

        generateToken().then(function (res) {
            generating = false;
            if (!started) return;
            if (!res.ok) {
                setStatus(BF.i18n.t('qr.error'), 'err');
                scheduleRestart(backoff);
                backoff = Math.min(backoff * 2, MAX_BACKOFF);
                return;
            }
            backoff = INITIAL_BACKOFF;
            currentFastAuthId = res.fastAuthId;
            showQr(res.pngBase64);
            setPhase('pending');
            setStatus(BF.i18n.t('qr.waiting'));
            startCountdown(res.expiresAtMs);
            subscribeResult(res.fastAuthId);
        });
    }

    function start() {
        var e = bindEls();
        if (!e.card) return; // на странице нет блока — не активируем
        if (started) return;
        started = true;
        backoff = INITIAL_BACKOFF;
        startSession();
    }

    function cancel() {
        started = false;
        generating = false;
        currentFastAuthId = null;
        clearTimers();
        cancelStream();
    }

    window.addEventListener('pagehide', cancel);
    window.addEventListener('beforeunload', cancel);

    window.BF.fastAuth = {
        start: start,
        cancel: cancel
    };
})();
