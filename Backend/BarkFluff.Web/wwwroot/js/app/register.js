/**
 * Multi-step registration wizard for the login page (index.html).
 * Mirrors the 9-step flow of the mobile/desktop clients (Android RegisterActivity,
 * macOS/iOS RegisterView).
 *
 * Requires: barkfluff.bundle.js (window.barkfluff / window.proto), BF.metadata, BF.tokens, BF.device
 * Exposes: BF.register
 *
 * Auth model (mirrors auth.js): dedicated gRPC-Web clients, metadata built manually.
 *   Steps 2–4 are anonymous (no token); after ConfirmAccount+CreateToken the access
 *   token is saved into BF.tokens and used for steps 5–9.
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var bf = window.barkfluff;
    var origin = window.location.origin;

    var identPb = function () { return window.proto.barkfluff.identity; };
    var usrPb = function () { return window.proto.barkfluff.users; };
    var filePb = function () { return window.proto.barkfluff.files; };

    var identityClient = new bf.IdentityApiClient(origin);
    var usersClient = new bf.UsersApiClient(origin);
    var filesClient = new bf.FilesApiClient(origin);

    var ERROR_CODES = {
        INVALID_OTP: '803B632C-4457-4B05-9435-9C3DD0F41E00'
    };

    var TOTAL_STEPS = 9;
    var STEP_TITLES = {
        1: 'Создать аккаунт', 2: 'Имя пользователя', 3: 'Электронная почта',
        4: 'Подтверждение', 5: 'Пароль', 6: 'Фото профиля', 7: 'О себе',
        8: 'Безопасность', 9: 'Готово'
    };

    var state = {
        firstName: '', lastName: '', username: '', email: '', codeId: '',
        accessToken: '', accessTokenExpiration: 0,
        refreshToken: '', refreshTokenExpiration: 0,
        twoFaSecret: '', avatarBlob: null, avatarFileId: ''
    };

    var step = 1;
    var usernameOk = false;
    var emailOk = false;
    var twoFaMode = 'intro'; // 'intro' | 'setup'
    var resendCooldown = 0;
    var resendTimer = null;

    // ─────────────── gRPC helpers ───────────────

    function meta(token) { return BF.metadata.build(token); }

    function errorCodeOf(err) {
        return (err && err.metadata && err.metadata['x-error-code']) || null;
    }

    function checkUsername(username) {
        var req = new (usrPb().CheckExistUsernameRequest)();
        req.setUsername(username);
        return new Promise(function (resolve, reject) {
            usersClient.checkExistUsername(req, meta(), function (err, resp) {
                if (err) return reject(err);
                resolve(resp.getExist());
            });
        });
    }

    function checkEmail(email) {
        var req = new (usrPb().CheckExistEmailRequest)();
        req.setEmail(email);
        return new Promise(function (resolve, reject) {
            usersClient.checkExistEmail(req, meta(), function (err, resp) {
                if (err) return reject(err);
                resolve(resp.getExist());
            });
        });
    }

    function createAccount() {
        var req = new (identPb().CreateAccountRequest)();
        req.setFirstName(state.firstName);
        req.setLastName(state.lastName);
        req.setUsername(state.username);
        req.setEmail(state.email);
        return new Promise(function (resolve, reject) {
            identityClient.createAccount(req, meta(), function (err, resp) {
                if (err) return reject(err);
                state.codeId = resp.getCodeId();
                resolve();
            });
        });
    }

    function confirmAccount(code) {
        var req = new (identPb().ConfirmAccountRequest)();
        req.setCodeId(state.codeId);
        req.setCodeValue(code);
        return new Promise(function (resolve, reject) {
            identityClient.confirmAccount(req, meta(), function (err, resp) {
                if (err) return reject(err);
                var rt = resp.getRefreshToken();
                if (!rt) return reject(new Error('no_refresh'));
                state.refreshToken = rt.getValue();
                state.refreshTokenExpiration = rt.getExpirationDate().toDate().getTime();

                // Exchange refresh token for an access token.
                var treq = new (identPb().CreateTokenRequest)();
                treq.setRefreshToken(state.refreshToken);
                identityClient.createToken(treq, meta(), function (terr, tresp) {
                    if (terr || !tresp) return reject(terr || new Error('no_token'));
                    var at = tresp.getAccessToken();
                    if (!at) return reject(new Error('no_token'));
                    state.accessToken = at.getValue();
                    state.accessTokenExpiration = at.getExpirationDate().toDate().getTime();
                    BF.tokens.save({
                        accessToken: state.accessToken,
                        accessTokenExpiration: state.accessTokenExpiration,
                        refreshToken: state.refreshToken,
                        refreshTokenExpiration: state.refreshTokenExpiration
                    });
                    resolve();
                });
            });
        });
    }

    function setPassword(password) {
        var req = new (identPb().SetPasswordRequest)();
        req.setPassword(password);
        req.setOldPassword('');
        return new Promise(function (resolve, reject) {
            identityClient.setPassword(req, meta(state.accessToken), function (err) {
                if (err) return reject(err);
                resolve();
            });
        });
    }

    function uploadAvatar(blob) {
        var req = new (filePb().GetUploadUrlRequest)();
        req.setFileType(filePb().UploadFileType.USER_AVATAR);
        return new Promise(function (resolve, reject) {
            filesClient.getUploadUrl(req, meta(state.accessToken), function (err, resp) {
                if (err) return reject(err);
                var fileId = resp.getFileId();
                var fd = new FormData();
                fd.append('file', blob, 'avatar.jpg');
                fetch('/api/files/upload/' + fileId, { method: 'POST', body: fd })
                    .then(function (r) {
                        if (!r.ok) throw new Error('upload_' + r.status);
                        return r.json();
                    })
                    .then(function (body) {
                        var fid = (body && body.fileId) || fileId;
                        var sreq = new (usrPb().SetProfilePictureRequest)();
                        sreq.setFileId(fid);
                        usersClient.setProfilePicture(sreq, meta(state.accessToken), function (serr) {
                            if (serr) return reject(serr);
                            state.avatarFileId = fid;
                            resolve();
                        });
                    })
                    .catch(reject);
            });
        });
    }

    function setBio(bio) {
        var req = new (usrPb().ChangeBioRequest)();
        req.setBio(bio);
        return new Promise(function (resolve, reject) {
            usersClient.changeBio(req, meta(state.accessToken), function (err) {
                if (err) return reject(err);
                resolve();
            });
        });
    }

    function enable2fa() {
        var req = new (identPb().EnableOtpVerificationRequest)();
        req.setOtpType(identPb().OtpTypeId.AUTHENTICATOR);
        return new Promise(function (resolve, reject) {
            identityClient.enableOtpVerification(req, meta(state.accessToken), function (err, resp) {
                if (err) return reject(err);
                resolve({ qr: resp.getOtpQr(), secret: resp.getOtpCode() });
            });
        });
    }

    function confirm2fa(code) {
        var req = new (identPb().ConfirmOtpVerificationRequest)();
        req.setOtpCode(code);
        return new Promise(function (resolve, reject) {
            identityClient.confirmOtpVerification(req, meta(state.accessToken), function (err) {
                if (err) return reject(err);
                resolve();
            });
        });
    }

    // ─────────────── DOM refs ───────────────

    var $ = function (id) { return document.getElementById(id); };
    var overlay, dialog, footer, backBtn, skipBtn, nextBtn, nextLabel,
        progressBar, stepLabel, titleEl;

    // ─────────────── small UI utils ───────────────

    function setLoading(btn, loading) {
        if (!btn) return;
        btn.disabled = loading;
        btn.classList.toggle('loading', loading);
    }

    function showFieldError(id, msg) {
        var el = $(id);
        if (!el) return;
        el.textContent = msg || '';
        el.classList.toggle('visible', !!msg);
    }

    function clearStepErrors() {
        var errs = dialog.querySelectorAll('.form-error');
        errs.forEach(function (e) { e.classList.remove('visible'); e.textContent = ''; });
        var inputs = dialog.querySelectorAll('.form-input');
        inputs.forEach(function (i) { i.classList.remove('error'); });
    }

    function currentStepEl() {
        return dialog.querySelector('.reg-step[data-step="' + step + '"]');
    }

    // ─────────────── OTP input wiring ───────────────

    function wireOtp(container, onComplete) {
        var inputs = Array.prototype.slice.call(container.querySelectorAll('.otp-input'));
        inputs.forEach(function (input, index) {
            input.addEventListener('input', function (e) {
                var v = e.target.value.replace(/[^0-9]/g, '');
                e.target.value = v;
                input.classList.toggle('filled', !!v);
                if (v && index < inputs.length - 1) inputs[index + 1].focus();
                var code = inputs.map(function (i) { return i.value; }).join('');
                if (code.length === inputs.length) onComplete(code);
            });
            input.addEventListener('keydown', function (e) {
                if (e.key === 'Backspace' && !e.target.value && index > 0) {
                    inputs[index - 1].focus();
                    inputs[index - 1].value = '';
                    inputs[index - 1].classList.remove('filled');
                }
            });
            input.addEventListener('paste', function (e) {
                e.preventDefault();
                var paste = (e.clipboardData || window.clipboardData).getData('text').replace(/[^0-9]/g, '');
                inputs.forEach(function (inp, i) {
                    if (i < paste.length) { inp.value = paste[i]; inp.classList.add('filled'); }
                });
                var next = Math.min(paste.length, inputs.length - 1);
                inputs[next].focus();
                var code = inputs.map(function (i) { return i.value; }).join('');
                if (code.length === inputs.length) onComplete(code);
            });
        });
        return {
            value: function () { return inputs.map(function (i) { return i.value; }).join(''); },
            clear: function () { inputs.forEach(function (i) { i.value = ''; i.classList.remove('filled'); }); },
            focus: function () { if (inputs[0]) inputs[0].focus(); }
        };
    }

    var otp4, otp8;

    // ─────────────── password strength ───────────────

    function passwordStrength(pw) {
        var score = 0;
        if (pw.length >= 8) score += 20;
        if (/[A-Z]/.test(pw)) score += 20;
        if (/[a-z]/.test(pw)) score += 20;
        if (/[0-9]/.test(pw)) score += 20;
        if (/[^A-Za-z0-9]/.test(pw)) score += 20;
        return score;
    }

    function updateStrength() {
        var pw = $('regPassword').value;
        var score = pw ? passwordStrength(pw) : 0;
        var bar = $('regStrengthBar');
        var label = $('regStrengthLabel');
        bar.style.width = score + '%';
        var color, text;
        if (score < 40) { color = 'var(--error)'; text = 'Слабый'; }
        else if (score < 60) { color = '#e67e22'; text = 'Средний'; }
        else if (score < 80) { color = '#f1c40f'; text = 'Хороший'; }
        else { color = 'var(--success)'; text = 'Надёжный'; }
        bar.style.background = color;
        label.textContent = pw ? text : '';
        label.style.color = color;
    }

    // ─────────────── interactive square cropper ───────────────

    var crop = {
        img: null, objUrl: null, canvas: null, ctx: null,
        stageW: 0, stageH: 0, baseScale: 1, zoom: 1,
        offX: 0, offY: 0, dragging: false, lastX: 0, lastY: 0
    };

    function cropDrawW() { return crop.img.naturalWidth * crop.baseScale * crop.zoom; }
    function cropDrawH() { return crop.img.naturalHeight * crop.baseScale * crop.zoom; }

    function clampCropOffset() {
        var dw = cropDrawW(), dh = cropDrawH();
        crop.offX = Math.min(0, Math.max(crop.stageW - dw, crop.offX));
        crop.offY = Math.min(0, Math.max(crop.stageH - dh, crop.offY));
    }

    function renderCrop() {
        if (!crop.img) return;
        clampCropOffset();
        crop.ctx.clearRect(0, 0, crop.stageW, crop.stageH);
        crop.ctx.drawImage(crop.img, crop.offX, crop.offY, cropDrawW(), cropDrawH());
    }

    function openCropper(file) {
        var stage = $('regCropStage');
        crop.canvas = $('regCropCanvas');
        crop.ctx = crop.canvas.getContext('2d');
        crop.stageW = stage.clientWidth || 260;
        crop.stageH = crop.stageW;
        crop.canvas.width = crop.stageW;
        crop.canvas.height = crop.stageH;

        if (crop.objUrl) URL.revokeObjectURL(crop.objUrl);
        crop.objUrl = URL.createObjectURL(file);
        var img = new Image();
        img.onload = function () {
            crop.img = img;
            crop.baseScale = Math.max(crop.stageW / img.naturalWidth, crop.stageH / img.naturalHeight);
            crop.zoom = 1;
            $('regCropZoom').value = '1';
            // center
            crop.offX = (crop.stageW - cropDrawW()) / 2;
            crop.offY = (crop.stageH - cropDrawH()) / 2;
            renderCrop();
        };
        img.src = crop.objUrl;
        showAvatarMode('crop');
    }

    function applyCrop() {
        var out = document.createElement('canvas');
        out.width = 512; out.height = 512;
        var octx = out.getContext('2d');
        var eff = crop.baseScale * crop.zoom;
        var srcX = -crop.offX / eff;
        var srcY = -crop.offY / eff;
        var srcSize = crop.stageW / eff;
        octx.drawImage(crop.img, srcX, srcY, srcSize, srcSize, 0, 0, 512, 512);
        out.toBlob(function (blob) {
            state.avatarBlob = blob;
            state.avatarFileId = '';
            var prev = $('regAvatarPreview');
            prev.src = out.toDataURL('image/jpeg', 0.9);
            prev.hidden = false;
            $('regAvatarPlus').hidden = true;
            $('regAvatarPick').textContent = 'Изменить фото';
            showAvatarMode('empty');
        }, 'image/jpeg', 0.85);
    }

    function wireCropper() {
        var canvas = $('regCropCanvas');
        function down(x, y) { crop.dragging = true; crop.lastX = x; crop.lastY = y; }
        function move(x, y) {
            if (!crop.dragging) return;
            crop.offX += (x - crop.lastX);
            crop.offY += (y - crop.lastY);
            crop.lastX = x; crop.lastY = y;
            renderCrop();
        }
        function up() { crop.dragging = false; }

        canvas.addEventListener('mousedown', function (e) { down(e.clientX, e.clientY); });
        window.addEventListener('mousemove', function (e) { move(e.clientX, e.clientY); });
        window.addEventListener('mouseup', up);
        canvas.addEventListener('touchstart', function (e) {
            var t = e.touches[0]; down(t.clientX, t.clientY);
        }, { passive: true });
        canvas.addEventListener('touchmove', function (e) {
            var t = e.touches[0]; move(t.clientX, t.clientY); e.preventDefault();
        }, { passive: false });
        canvas.addEventListener('touchend', up);

        $('regCropZoom').addEventListener('input', function () {
            var stage = $('regCropStage');
            var cx = crop.stageW / 2, cy = crop.stageH / 2;
            // keep center anchored while zooming
            var prevEff = crop.baseScale * crop.zoom;
            var imgCX = (cx - crop.offX) / prevEff;
            var imgCY = (cy - crop.offY) / prevEff;
            crop.zoom = parseFloat(this.value);
            var eff = crop.baseScale * crop.zoom;
            crop.offX = cx - imgCX * eff;
            crop.offY = cy - imgCY * eff;
            renderCrop();
        });

        $('regCropApply').addEventListener('click', applyCrop);
        $('regCropCancel').addEventListener('click', function () { showAvatarMode('empty'); });
    }

    function showAvatarMode(mode) {
        $('regAvatarEmpty').hidden = (mode === 'crop');
        $('regCropper').hidden = (mode !== 'crop');
        footer.style.display = (mode === 'crop') ? 'none' : '';
    }

    // ─────────────── validation ───────────────

    var USERNAME_RE = /^[a-z0-9_-]+$/;
    var EMAIL_RE = /^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$/;

    function setFieldStatus(id, kind) {
        // kind: '' | 'loading' | 'ok' | 'err'
        var el = $(id);
        if (!el) return;
        el.className = 'reg-field-status';
        if (kind === 'loading') { el.classList.add('reg-status-loading'); el.textContent = ''; }
        else if (kind === 'ok') { el.classList.add('reg-status-ok'); el.textContent = '✓'; }
        else if (kind === 'err') { el.classList.add('reg-status-err'); el.textContent = '✗'; }
        else { el.textContent = ''; }
    }

    var usernameDebounce, emailDebounce;

    function onUsernameInput() {
        var v = $('regUsername').value.toLowerCase();
        if ($('regUsername').value !== v) $('regUsername').value = v;
        usernameOk = false;
        showFieldError('regUsernameErr', '');
        clearTimeout(usernameDebounce);
        if (!v) { setFieldStatus('regUsernameStatus', ''); return; }
        if (v.length < 3 || v.length > 30 || !USERNAME_RE.test(v) || /^[0-9]/.test(v)) {
            setFieldStatus('regUsernameStatus', 'err');
            return;
        }
        setFieldStatus('regUsernameStatus', 'loading');
        usernameDebounce = setTimeout(function () {
            checkUsername(v).then(function (exist) {
                if ($('regUsername').value.toLowerCase() !== v) return;
                if (exist) {
                    usernameOk = false;
                    setFieldStatus('regUsernameStatus', 'err');
                    showFieldError('regUsernameErr', 'Это имя уже занято');
                } else {
                    usernameOk = true;
                    setFieldStatus('regUsernameStatus', 'ok');
                }
            }).catch(function () {
                // On network error allow proceeding (server validates again on CreateAccount)
                usernameOk = true;
                setFieldStatus('regUsernameStatus', '');
            });
        }, 500);
    }

    function onEmailInput() {
        var v = $('regEmail').value.trim().toLowerCase();
        emailOk = false;
        showFieldError('regEmailErr', '');
        clearTimeout(emailDebounce);
        if (!v) { setFieldStatus('regEmailStatus', ''); return; }
        if (!EMAIL_RE.test(v)) { setFieldStatus('regEmailStatus', 'err'); return; }
        setFieldStatus('regEmailStatus', 'loading');
        emailDebounce = setTimeout(function () {
            checkEmail(v).then(function (exist) {
                if ($('regEmail').value.trim().toLowerCase() !== v) return;
                if (exist) {
                    emailOk = false;
                    setFieldStatus('regEmailStatus', 'err');
                    showFieldError('regEmailErr', 'Этот email уже зарегистрирован');
                } else {
                    emailOk = true;
                    setFieldStatus('regEmailStatus', 'ok');
                }
            }).catch(function () {
                emailOk = true;
                setFieldStatus('regEmailStatus', '');
            });
        }, 500);
    }

    // ─────────────── navigation ───────────────

    function configFooter() {
        backBtn.hidden = !(step === 2 || step === 3);
        skipBtn.hidden = !(step === 6 || step === 7 || step === 8);
        var label = 'Далее';
        if (step === 4 || step === 8) label = 'Подтвердить';
        if (step === 9) label = 'Перейти в чаты';
        nextLabel.textContent = label;
        nextBtn.hidden = (step === 8 && twoFaMode === 'intro');
        progressBar.style.width = (step / TOTAL_STEPS * 100) + '%';
        stepLabel.textContent = 'Шаг ' + step + ' из ' + TOTAL_STEPS;
        titleEl.textContent = STEP_TITLES[step];
        footer.style.display = '';
    }

    function goToStep(n) {
        step = n;
        dialog.querySelectorAll('.reg-step').forEach(function (el) {
            el.hidden = (parseInt(el.getAttribute('data-step'), 10) !== n);
        });
        clearStepErrors();
        configFooter();
        onEnterStep(n);
    }

    function onEnterStep(n) {
        var el = currentStepEl();
        if (n === 4) {
            $('regOtpDesc').textContent = 'Мы отправили 6-значный код на ' + state.email;
            otp4.clear();
            setTimeout(function () { otp4.focus(); }, 50);
        } else if (n === 8) {
            twoFaMode = 'intro';
            $('reg2faIntro').hidden = false;
            $('reg2faSetup').hidden = true;
            otp8.clear();
            configFooter();
        } else if (n === 9) {
            var name = state.firstName + (state.lastName ? ' ' + state.lastName : '');
            $('regCompleteName').textContent = 'Добро пожаловать, ' + name + '! @' + state.username;
        } else {
            // focus first text input
            var input = el && el.querySelector('input.form-input, textarea.form-input');
            if (input) setTimeout(function () { input.focus(); }, 50);
        }
    }

    function fail(msg, fieldErrId) {
        setLoading(nextBtn, false);
        if (fieldErrId) showFieldError(fieldErrId, msg);
    }

    function handleNext() {
        clearStepErrors();
        switch (step) {
            case 1: return doStep1();
            case 2: return doStep2();
            case 3: return doStep3();
            case 4: return doStep4(otp4.value());
            case 5: return doStep5();
            case 6: return doStep6();
            case 7: return doStep7();
            case 8: return doStep8(otp8.value());
            case 9: window.location.href = '/messenger'; return;
        }
    }

    function doStep1() {
        var first = $('regFirstName').value.trim();
        var last = $('regLastName').value.trim();
        if (first.length < 3) return showFieldError('regFirstNameErr', 'Минимум 3 символа');
        if (first.length > 40) return showFieldError('regFirstNameErr', 'Максимум 40 символов');
        if (last.length > 40) return showFieldError('regLastNameErr', 'Максимум 40 символов');
        state.firstName = first;
        state.lastName = last;
        goToStep(2);
    }

    function doStep2() {
        var v = $('regUsername').value.toLowerCase().trim();
        if (v.length < 3 || v.length > 30 || !USERNAME_RE.test(v) || /^[0-9]/.test(v)) {
            return showFieldError('regUsernameErr', 'Латиница, цифры, _ и -; от 3 до 30, не с цифры');
        }
        state.username = v;
        setLoading(nextBtn, true);
        checkUsername(v).then(function (exist) {
            setLoading(nextBtn, false);
            if (exist) return showFieldError('regUsernameErr', 'Это имя уже занято');
            goToStep(3);
        }).catch(function () {
            // allow proceeding; CreateAccount will re-validate
            setLoading(nextBtn, false);
            goToStep(3);
        });
    }

    function doStep3() {
        var v = $('regEmail').value.trim().toLowerCase();
        if (!EMAIL_RE.test(v)) return showFieldError('regEmailErr', 'Введите корректный email');
        state.email = v;
        setLoading(nextBtn, true);
        checkEmail(v).then(function (exist) {
            if (exist) { setLoading(nextBtn, false); return showFieldError('regEmailErr', 'Этот email уже зарегистрирован'); }
            return createAccount().then(function () {
                setLoading(nextBtn, false);
                startResendCooldown();
                goToStep(4);
            });
        }).catch(function (err) {
            setLoading(nextBtn, false);
            showFieldError('regEmailErr', 'Не удалось создать аккаунт. Попробуйте ещё раз');
        });
    }

    function doStep4(code) {
        if (code.length !== 6) return showFieldError('regOtpErr', 'Введите все 6 цифр');
        setLoading(nextBtn, true);
        confirmAccount(code).then(function () {
            setLoading(nextBtn, false);
            goToStep(5);
        }).catch(function (err) {
            setLoading(nextBtn, false);
            if (errorCodeOf(err) === ERROR_CODES.INVALID_OTP) {
                showFieldError('regOtpErr', 'Неверный код');
            } else {
                showFieldError('regOtpErr', 'Неверный или просроченный код');
            }
            otp4.clear();
            otp4.focus();
        });
    }

    function doStep5() {
        var pw = $('regPassword').value;
        var pw2 = $('regPassword2').value;
        if (pw.length < 8) return showFieldError('regPasswordErr', 'Минимум 8 символов');
        if (pw !== pw2) return showFieldError('regPasswordErr', 'Пароли не совпадают');
        setLoading(nextBtn, true);
        setPassword(pw).then(function () {
            setLoading(nextBtn, false);
            goToStep(6);
        }).catch(function () {
            setLoading(nextBtn, false);
            showFieldError('regPasswordErr', 'Не удалось установить пароль');
        });
    }

    function doStep6() {
        if (state.avatarBlob && !state.avatarFileId) {
            setLoading(nextBtn, true);
            uploadAvatar(state.avatarBlob).then(function () {
                setLoading(nextBtn, false);
                goToStep(7);
            }).catch(function () {
                setLoading(nextBtn, false);
                // avatar is optional — don't block registration
                goToStep(7);
            });
        } else {
            goToStep(7);
        }
    }

    function doStep7() {
        var bio = $('regBio').value.trim();
        if (!bio) return goToStep(8);
        setLoading(nextBtn, true);
        setBio(bio).then(function () {
            setLoading(nextBtn, false);
            goToStep(8);
        }).catch(function () {
            setLoading(nextBtn, false);
            goToStep(8); // bio optional
        });
    }

    function doStep8(code) {
        if (twoFaMode !== 'setup') { goToStep(9); return; }
        if (code.length !== 6) return showFieldError('reg2faErr', 'Введите все 6 цифр');
        setLoading(nextBtn, true);
        confirm2fa(code).then(function () {
            setLoading(nextBtn, false);
            goToStep(9);
        }).catch(function () {
            setLoading(nextBtn, false);
            showFieldError('reg2faErr', 'Неверный код');
            otp8.clear();
            otp8.focus();
        });
    }

    // ─────────────── resend OTP ───────────────

    function startResendCooldown() {
        resendCooldown = 60;
        var btn = $('regOtpResend');
        if (!btn) return;
        btn.disabled = true;
        clearInterval(resendTimer);
        resendTimer = setInterval(function () {
            resendCooldown--;
            if (resendCooldown <= 0) {
                clearInterval(resendTimer);
                btn.disabled = false;
                btn.textContent = 'Отправить код повторно';
            } else {
                btn.textContent = 'Отправить повторно через ' + resendCooldown + 'с';
            }
        }, 1000);
    }

    function handleResend() {
        showFieldError('regOtpErr', '');
        createAccount().then(function () {
            startResendCooldown();
        }).catch(function () {
            showFieldError('regOtpErr', 'Не удалось отправить код повторно');
        });
    }

    // ─────────────── open / close ───────────────

    function resetState() {
        state = {
            firstName: '', lastName: '', username: '', email: '', codeId: '',
            accessToken: '', accessTokenExpiration: 0,
            refreshToken: '', refreshTokenExpiration: 0,
            twoFaSecret: '', avatarBlob: null, avatarFileId: ''
        };
        usernameOk = false; emailOk = false; twoFaMode = 'intro';
        clearInterval(resendTimer);
        dialog.querySelectorAll('input.form-input, textarea.form-input').forEach(function (i) { i.value = ''; });
        if (otp4) otp4.clear();
        if (otp8) otp8.clear();
        setFieldStatus('regUsernameStatus', '');
        setFieldStatus('regEmailStatus', '');
        var prev = $('regAvatarPreview');
        if (prev) { prev.hidden = true; prev.removeAttribute('src'); }
        if ($('regAvatarPlus')) $('regAvatarPlus').hidden = false;
        if ($('regAvatarPick')) $('regAvatarPick').textContent = 'Выбрать фото';
        if ($('regCropper')) $('regCropper').hidden = true;
        if ($('regAvatarEmpty')) $('regAvatarEmpty').hidden = false;
        if (crop.objUrl) { URL.revokeObjectURL(crop.objUrl); crop.objUrl = null; }
        crop.img = null;
        $('regStrengthBar').style.width = '0%';
        $('regStrengthLabel').textContent = '';
        $('regBioCount').textContent = '0';
    }

    function open() {
        resetState();
        goToStep(1);
        overlay.classList.add('visible');
        document.body.style.overflow = 'hidden';
    }

    function close() {
        overlay.classList.remove('visible');
        document.body.style.overflow = '';
    }

    // ─────────────── init ───────────────

    function init() {
        overlay = $('registerOverlay');
        if (!overlay) return;
        dialog = overlay.querySelector('.reg-dialog');
        footer = $('regFooter');
        backBtn = $('regBack');
        skipBtn = $('regSkip');
        nextBtn = $('regNext');
        nextLabel = $('regNextLabel');
        progressBar = $('regProgressBar');
        stepLabel = $('regStepLabel');
        titleEl = $('regTitle');

        otp4 = wireOtp($('regOtp4'), function () { if (step === 4) doStep4(otp4.value()); });
        otp8 = wireOtp($('regOtp8'), function () { if (step === 8 && twoFaMode === 'setup') doStep8(otp8.value()); });

        var openBtn = $('toRegisterBtn');
        if (openBtn) openBtn.addEventListener('click', open);

        $('regClose').addEventListener('click', close);
        backBtn.addEventListener('click', function () { if (step > 1) goToStep(step - 1); });
        skipBtn.addEventListener('click', function () {
            if (step === 6) goToStep(7);
            else if (step === 7) goToStep(8);
            else if (step === 8) goToStep(9);
        });
        nextBtn.addEventListener('click', handleNext);

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && overlay.classList.contains('visible')) close();
        });

        // Step 2/3 live checks
        $('regUsername').addEventListener('input', onUsernameInput);
        $('regEmail').addEventListener('input', onEmailInput);

        // Enter to advance on simple text steps
        ['regFirstName', 'regLastName', 'regUsername', 'regEmail', 'regPassword', 'regPassword2'].forEach(function (id) {
            var el = $(id);
            if (el) el.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') { e.preventDefault(); handleNext(); }
            });
        });

        // Step 5 password
        $('regPassword').addEventListener('input', updateStrength);
        $('regPwToggle').addEventListener('click', function () {
            var inp = $('regPassword');
            var show = inp.type === 'password';
            inp.type = show ? 'text' : 'password';
            this.textContent = show ? '🙈' : '👁';
        });

        // Step 6 avatar
        $('regAvatarPick').addEventListener('click', function () { $('regAvatarFile').click(); });
        $('regAvatarFile').addEventListener('change', function (e) {
            var file = e.target.files && e.target.files[0];
            if (file) openCropper(file);
            e.target.value = '';
        });
        wireCropper();

        // Step 7 bio counter
        $('regBio').addEventListener('input', function () {
            $('regBioCount').textContent = String(this.value.length);
        });

        // Step 8 2FA
        $('reg2faEnable').addEventListener('click', function () {
            var btn = this;
            setLoading(btn, true);
            enable2fa().then(function (res) {
                setLoading(btn, false);
                state.twoFaSecret = res.secret || '';
                twoFaMode = 'setup';
                $('reg2faIntro').hidden = true;
                $('reg2faSetup').hidden = false;
                $('reg2faSecret').textContent = res.secret || '';
                var qrImg = $('reg2faQr');
                if (res.qr) { qrImg.src = 'data:image/png;base64,' + res.qr; qrImg.hidden = false; }
                else { qrImg.hidden = true; }
                configFooter();
                otp8.focus();
            }).catch(function () {
                setLoading(btn, false);
                showFieldError('reg2faErr', 'Не удалось включить 2FA');
            });
        });
        $('reg2faCopy').addEventListener('click', function () {
            if (state.twoFaSecret && navigator.clipboard) {
                navigator.clipboard.writeText(state.twoFaSecret);
                this.textContent = 'Скопировано';
                var self = this;
                setTimeout(function () { self.textContent = 'Копировать'; }, 1500);
            }
        });

        var resendBtn = $('regOtpResend');
        if (resendBtn) resendBtn.addEventListener('click', handleResend);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.BF.register = { open: open, close: close };
})();
