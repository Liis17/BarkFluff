/**
 * Approving a sign-in of another device by its FastAuth QR (the signed-in side of fast-auth.js):
 * camera / image / manual code → ScanFastAuth → the device details → explicit AcceptFastAuth or RejectFastAuth.
 * QR decoding: BarcodeDetector when the browser has it, otherwise the vendored jsQR (loaded on demand).
 * Requires: BF.clients, BF.network, BF.i18n, window.proto.barkfluff.fast.auth
 * Exposes: BF.fastAuthLink
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var JSQR_SRC = '/js/vendor/jsqr.min.js?v=1.4.0';
    // QR содержит голый fast_auth_id (GUID сессии FastAuth); всё остальное — чужие коды.
    var CODE_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    var ERRORS = {
        'A5E94C7D-1B82-4F36-9CDE-78B1F4A7E2C5': 'fastAuthLink.error.notFound',
        'D2F71E8A-3C5B-4197-8A6D-4E9B27C5F1A8': 'fastAuthLink.error.expired',
        '3C8A1E5F-7D29-4B83-9E16-5A2C8F4B7D31': 'fastAuthLink.error.state',
        '7B3F8E92-5D14-4C68-A7E2-1F9B6D3C8A45': 'fastAuthLink.error.state'
    };

    var jsQrPromise = null;
    var session = null; // текущий экран: { container, stream, timer, pending: {fastAuthId, code}, done }

    function pkg() {
        return window.proto.barkfluff.fast.auth;
    }

    function call(method, req) {
        var client = BF.clients.fastAuth;
        return BF.clients.authCall(client[method].bind(client), req, BF.network.POLICIES.MUTATION);
    }

    function scan(fastAuthId) {
        var req = new (pkg().ScanFastAuthRequest)();
        req.setFastAuthId(fastAuthId);
        return call('scanFastAuth', req).then(function (resp) {
            return {
                deviceName: resp.getDeviceName(),
                operationSystem: resp.getOperationSystem(),
                appName: resp.getAppName(),
                appVersion: resp.getAppVersion(),
                ipAddress: resp.getIpAddress(),
                confirmationCode: resp.getConfirmationCode()
            };
        });
    }

    function decide(accept, fastAuthId, confirmationCode) {
        var req = new (pkg()[accept ? 'AcceptFastAuthRequest' : 'RejectFastAuthRequest'])();
        req.setFastAuthId(fastAuthId);
        req.setConfirmationCode(confirmationCode);
        return call(accept ? 'acceptFastAuth' : 'rejectFastAuth', req);
    }

    function errorText(err) {
        return BF.i18n.t((err && ERRORS[String(err.errorCode || '').toUpperCase()]) || 'common.error');
    }

    function loadJsQr() {
        if (window.jsQR) return Promise.resolve(window.jsQR);
        if (!jsQrPromise) {
            jsQrPromise = new Promise(function (resolve, reject) {
                var script = document.createElement('script');
                script.src = JSQR_SRC;
                script.onload = function () {
                    if (window.jsQR) resolve(window.jsQR);
                    else reject(new Error('jsqr_unavailable'));
                };
                script.onerror = function () {
                    jsQrPromise = null;
                    reject(new Error('jsqr_unavailable'));
                };
                document.head.appendChild(script);
            });
        }
        return jsQrPromise;
    }

    // Возвращает функцию source(canvas|video|image) → Promise<string|null>.
    function createDecoder() {
        if (typeof window.BarcodeDetector === 'function') {
            return Promise.resolve(
                window.BarcodeDetector.getSupportedFormats ? window.BarcodeDetector.getSupportedFormats() : ['qr_code']
            )
                .then(function (formats) {
                    if (formats.indexOf('qr_code') < 0) throw new Error('no_qr');
                    var detector = new window.BarcodeDetector({ formats: ['qr_code'] });
                    return function (source) {
                        return detector.detect(source).then(function (codes) {
                            return codes.length ? codes[0].rawValue : null;
                        });
                    };
                })
                .catch(jsQrDecoder);
        }
        return jsQrDecoder();
    }

    function jsQrDecoder() {
        return loadJsQr().then(function (jsQR) {
            var canvas = document.createElement('canvas');
            var ctx = canvas.getContext('2d', { willReadFrequently: true });
            return function (source) {
                var w = source.videoWidth || source.naturalWidth || source.width;
                var h = source.videoHeight || source.naturalHeight || source.height;
                if (!w || !h) return Promise.resolve(null);
                var scale = Math.min(1, 800 / Math.max(w, h));
                canvas.width = Math.round(w * scale);
                canvas.height = Math.round(h * scale);
                ctx.drawImage(source, 0, 0, canvas.width, canvas.height);
                var image = ctx.getImageData(0, 0, canvas.width, canvas.height);
                var code = jsQR(image.data, image.width, image.height);
                return Promise.resolve(code ? code.data : null);
            };
        });
    }

    function stopCamera(s) {
        if (s.timer) {
            clearTimeout(s.timer);
            s.timer = null;
        }
        if (s.stream) {
            s.stream.getTracks().forEach(function (t) {
                t.stop();
            });
            s.stream = null;
        }
    }

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    }

    function button(className, key, onClick) {
        var b = el('button', className, BF.i18n.t(key));
        b.type = 'button';
        b.addEventListener('click', onClick);
        return b;
    }

    // Экран ввода: камера (если доступна), картинка с QR и ручной ввод кода.
    function renderInput(s) {
        stopCamera(s);
        s.container.innerHTML = '';
        s.container.appendChild(el('div', 'sd-hint', BF.i18n.t('fastAuthLink.hint')));

        var status = el('div', 'sd-hint');
        var video = el('video', 'fal-video');
        video.setAttribute('playsinline', '');
        video.muted = true;
        video.hidden = true;
        s.container.appendChild(video);

        var actions = el('div', 'fal-actions');
        var fileInput = el('input');
        fileInput.type = 'file';
        fileInput.accept = 'image/*';
        fileInput.hidden = true;
        // Экран ввода актуален, пока его не сменил submit или новый renderInput: ответы камеры,
        // декодера и картинки от прежнего экрана игнорируются, а пришедшая поздно камера гасится.
        var screen = ++s.screen;
        function current() {
            return session === s && s.screen === screen;
        }
        var decoderPromise = null;
        function decoder() {
            if (!decoderPromise) {
                decoderPromise = createDecoder().catch(function (e) {
                    decoderPromise = null; // следующий клик повторит загрузку jsQR
                    throw e;
                });
            }
            return decoderPromise;
        }

        var cameraBtn = button('sd-btn sd-btn-primary', 'fastAuthLink.camera', function () {
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                status.textContent = BF.i18n.t('fastAuthLink.error.camera');
                return;
            }
            cameraBtn.disabled = true;
            navigator.mediaDevices
                .getUserMedia({ video: { facingMode: 'environment' }, audio: false })
                .then(function (stream) {
                    if (!current()) {
                        stream.getTracks().forEach(function (t) {
                            t.stop();
                        });
                        return;
                    }
                    s.stream = stream;
                    video.srcObject = stream;
                    video.hidden = false;
                    video.play().catch(function () {});
                    return decoder().then(function (detect) {
                        if (current() && s.stream === stream) scanLoop(stream, detect);
                    });
                })
                .catch(function () {
                    if (!current()) return;
                    stopCamera(s);
                    video.hidden = true;
                    cameraBtn.disabled = false;
                    status.textContent = BF.i18n.t('fastAuthLink.error.camera');
                });
        });

        function scanLoop(stream, detect) {
            var tick = function () {
                if (!current() || s.stream !== stream) return;
                detect(video)
                    .catch(function () {
                        return null;
                    })
                    .then(function (value) {
                        if (!current() || s.stream !== stream) return;
                        if (value && CODE_PATTERN.test(value.trim())) {
                            submit(s, value, status);
                            return;
                        }
                        // Чужой QR в кадре — подсказываем и продолжаем сканировать.
                        if (value) status.textContent = BF.i18n.t('fastAuthLink.error.invalid');
                        s.timer = setTimeout(tick, 250);
                    });
            };
            tick();
        }
        var imageBtn = button('sd-btn', 'fastAuthLink.image', function () {
            fileInput.click();
        });
        fileInput.addEventListener('change', function () {
            var file = fileInput.files && fileInput.files[0];
            fileInput.value = '';
            if (!file) return;
            var url = URL.createObjectURL(file);
            var img = new Image();
            img.onload = function () {
                decoder()
                    .then(function (detect) {
                        return detect(img);
                    })
                    .then(function (value) {
                        URL.revokeObjectURL(url);
                        if (!current()) return;
                        if (value) submit(s, value, status);
                        else status.textContent = BF.i18n.t('fastAuthLink.error.noQr');
                    })
                    .catch(function () {
                        URL.revokeObjectURL(url);
                        if (current()) status.textContent = BF.i18n.t('fastAuthLink.error.noQr');
                    });
            };
            img.onerror = function () {
                URL.revokeObjectURL(url);
                if (current()) status.textContent = BF.i18n.t('fastAuthLink.error.noQr');
            };
            img.src = url;
        });
        actions.appendChild(cameraBtn);
        actions.appendChild(imageBtn);
        actions.appendChild(fileInput);
        s.container.appendChild(actions);

        var manual = el('input', 'sd-input');
        manual.type = 'text';
        manual.autocomplete = 'off';
        manual.placeholder = BF.i18n.t('fastAuthLink.manual');
        var manualBtn = button('sd-btn', 'fastAuthLink.continue', function () {
            submit(s, manual.value, status);
        });
        var manualRow = el('div', 'fal-actions');
        manualRow.appendChild(manual);
        manualRow.appendChild(manualBtn);
        s.container.appendChild(manualRow);
        s.container.appendChild(status);
        return status;
    }

    function submit(s, raw, status) {
        var fastAuthId = String(raw || '').trim();
        if (!CODE_PATTERN.test(fastAuthId)) {
            status.textContent = BF.i18n.t('fastAuthLink.error.invalid');
            return;
        }
        s.screen++; // экран ввода закрыт: его камера, картинка и кнопки больше не вызовут submit
        stopCamera(s);
        s.container.innerHTML = '';
        s.container.appendChild(el('div', 'sd-hint', BF.i18n.t('common.loadingShort')));
        scan(fastAuthId)
            .then(function (info) {
                if (session !== s) {
                    // Экран закрыт во время запроса — отказываем, чтобы не оставлять сессию отсканированной.
                    decide(false, fastAuthId, info.confirmationCode).catch(function () {});
                    return;
                }
                s.pending = { fastAuthId: fastAuthId, code: info.confirmationCode };
                renderConfirm(s, info);
            })
            .catch(function (err) {
                if (session !== s) return;
                renderInput(s).textContent = errorText(err);
            });
    }

    // Явное подтверждение: показываем, какое устройство получит доступ к аккаунту.
    function renderConfirm(s, info) {
        s.container.innerHTML = '';
        s.container.appendChild(el('div', 'fal-warning', BF.i18n.t('fastAuthLink.warning')));
        [
            ['fastAuthLink.device', info.deviceName],
            ['fastAuthLink.os', info.operationSystem],
            ['fastAuthLink.app', [info.appName, info.appVersion].filter(Boolean).join(' ')],
            ['fastAuthLink.ip', info.ipAddress]
        ].forEach(function (pair) {
            var row = el('div', 'sd-about-row');
            row.appendChild(el('div', 'sd-about-label', BF.i18n.t(pair[0])));
            row.appendChild(el('div', 'sd-about-value', pair[1] || '—'));
            s.container.appendChild(row);
        });
        var status = el('div', 'sd-hint');
        var actions = el('div', 'fal-actions');
        var acceptBtn = button('sd-btn sd-btn-primary', 'fastAuthLink.accept', function () {
            finish(true);
        });
        var rejectBtn = button('sd-btn', 'fastAuthLink.reject', function () {
            finish(false);
        });
        function finish(accept) {
            var pending = s.pending;
            if (!pending) return;
            acceptBtn.disabled = true;
            rejectBtn.disabled = true;
            s.deciding = true; // уход с экрана во время решения не должен слать встречный Reject
            decide(accept, pending.fastAuthId, pending.code)
                .then(function () {
                    s.pending = null;
                    s.deciding = false;
                    if (session !== s) return;
                    s.container.innerHTML = '';
                    s.container.appendChild(
                        el('div', 'sd-hint', BF.i18n.t(accept ? 'fastAuthLink.accepted' : 'fastAuthLink.rejected'))
                    );
                    s.container.appendChild(
                        button('sd-btn sd-btn-primary', 'fastAuthLink.again', function () {
                            renderInput(s);
                        })
                    );
                })
                .catch(function (err) {
                    s.deciding = false;
                    if (session !== s) return;
                    acceptBtn.disabled = false;
                    rejectBtn.disabled = false;
                    status.textContent = errorText(err);
                    status.className = 'sd-hint error';
                });
        }
        actions.appendChild(acceptBtn);
        actions.appendChild(rejectBtn);
        s.container.appendChild(actions);
        s.container.appendChild(status);
    }

    function render(container) {
        stop();
        session = { container: container, stream: null, timer: null, pending: null, deciding: false, screen: 0 };
        renderInput(session);
    }

    // Уход с экрана: камера гасится, неподтверждённый вход отклоняется (best-effort).
    function stop() {
        var s = session;
        session = null;
        if (!s) return;
        stopCamera(s);
        if (s.pending && !s.deciding) {
            decide(false, s.pending.fastAuthId, s.pending.code).catch(function () {});
            s.pending = null;
        }
    }

    window.BF.fastAuthLink = { render: render, stop: stop };
})();
