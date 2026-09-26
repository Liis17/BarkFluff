/* Registration wizard: collect a password, confirm email/Telegram, then finish the profile. */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var bf = window.barkfluff;

    var usrPb = function () { return window.proto.barkfluff.users; };
    var filePb = function () { return window.proto.barkfluff.files; };

    // Ленивая инициализация: на шелле мастер регистрации живёт на той же странице,
    // что и выбор ноды, поэтому origin известен только к моменту первого вызова.
    var clientCache = { origin: null };

    function clients() {
        var origin = BF.node.origin();
        if (clientCache.origin !== origin) {
            clientCache = {
                origin: origin,
                identity: new bf.IdentityApiClient(origin),
                users: new bf.UsersApiClient(origin),
                files: new bf.FilesApiClient(origin)
            };
        }
        return clientCache;
    }

    // Прокси перед реальным клиентом: вызовы остаются вида identityClient.createAccount(...),
    // но клиент берётся из clients() в момент вызова, с актуальной нодой.
    function lazyClient(name) {
        return new Proxy({}, {
            get: function (_, method) {
                var client = clients()[name];
                var value = client[method];
                return typeof value === 'function' ? value.bind(client) : value;
            }
        });
    }

    var usersClient = lazyClient('users');
    var filesClient = lazyClient('files');

    var TOTAL_STEPS = 8;
    var STEP_ORDER = [1, 2, 3, 5, 6, 7, 8, 9];
    var registrationGeneration = 0;
    var registrationController = new AbortController();
    var registrationChannelReady = false;
    var STEP_TITLE_KEYS = {
        1: 'register.createAccount', 2: 'register.username', 3: 'security.confirmation',
        4: 'register.confirmation', 5: 'common.password', 6: 'settings.profilePhoto', 7: 'register.aboutYou',
        8: 'settings.section.security', 9: 'common.done'
    };

    var state = {
        firstName: '', lastName: '', username: '', email: '', codeId: '',
        accessToken: '', accessTokenExpiration: 0,
        refreshToken: '', refreshTokenExpiration: 0,
        twoFaSecret: '', avatarBlob: null, avatarFileId: ''
    };

    var step = 1;

    // ─────────────── gRPC helpers ───────────────

    function meta(token) { return BF.metadata.build(token); }

    function rpc(method, request, token, policy) {
        var generation = registrationGeneration, origin = BF.node.origin();
        return BF.network.unary(method, request, meta(token), BF.network.withSignal(policy || BF.network.POLICIES.MUTATION, registrationController.signal))
            .then(function (response) {
                if (generation !== registrationGeneration || origin !== BF.node.origin()) throw new DOMException('Cancelled', 'AbortError');
                return response;
            });
    }

    function checkUsername(username) {
        var req = new (usrPb().CheckExistUsernameRequest)();
        req.setUsername(username);
        return rpc(usersClient.checkExistUsername, req, null, BF.network.POLICIES.READ)
            .then(function (resp) { return resp.getExist(); });
    }

    function checkEmail(email) {
        var req = new (usrPb().CheckExistEmailRequest)();
        req.setEmail(email);
        return rpc(usersClient.checkExistEmail, req, null, BF.network.POLICIES.READ)
            .then(function (resp) { return resp.getExist(); });
    }

    function uploadAvatar(blob) {
        var req = new (filePb().GetUploadUrlRequest)();
        req.setFileType(filePb().UploadFileType.USER_AVATAR);
        return rpc(filesClient.getUploadUrl, req, state.accessToken).then(function (resp) {
                var fileId = resp.getFileId();
                var fd = new FormData();
                fd.append('file', blob, 'avatar.jpg');
                return fetch(BF.node.origin() + '/api/files/upload/' + fileId, { method: 'POST', body: fd })
                    .then(function (r) {
                        if (!r.ok) throw new Error('upload_' + r.status);
                        return r.json();
                    })
                    .then(function (body) {
                        var fid = (body && body.fileId) || fileId;
                        var sreq = new (usrPb().SetProfilePictureRequest)();
                        sreq.setFileId(fid);
                        return rpc(usersClient.setProfilePicture, sreq, state.accessToken).then(function () {
                            state.avatarFileId = fid;
                        });
                    });
            });
    }

    function setBio(bio) {
        var req = new (usrPb().ChangeBioRequest)();
        req.setBio(bio);
        return rpc(usersClient.changeBio, req, state.accessToken).then(function () {});
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
        if (msg) BF.sound.play('droplet');
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
        if (score < 40) { color = 'var(--error)'; text = BF.i18n.t('register.password.weak'); }
        else if (score < 60) { color = '#e67e22'; text = BF.i18n.t('register.password.medium'); }
        else if (score < 80) { color = '#f1c40f'; text = BF.i18n.t('register.password.good'); }
        else { color = 'var(--success)'; text = BF.i18n.t('register.password.strong'); }
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
            $('regAvatarPick').textContent = BF.i18n.t('register.changePhoto');
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

    var USERNAME_RE = /^[a-z0-9_]+$/;
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
                    setFieldStatus('regUsernameStatus', 'err');
                    showFieldError('regUsernameErr', BF.i18n.t('register.error.usernameTaken'));
                } else {
                    setFieldStatus('regUsernameStatus', 'ok');
                }
            }).catch(function () {
                // On network error allow proceeding (server validates again on CreateAccount)
                setFieldStatus('regUsernameStatus', '');
            });
        }, 500);
    }

    function onEmailInput() {
        var v = $('regEmail').value.trim().toLowerCase();
        showFieldError('regEmailErr', '');
        clearTimeout(emailDebounce);
        if (!v) { setFieldStatus('regEmailStatus', ''); return; }
        if (!EMAIL_RE.test(v)) { setFieldStatus('regEmailStatus', 'err'); return; }
        setFieldStatus('regEmailStatus', 'loading');
        emailDebounce = setTimeout(function () {
            checkEmail(v).then(function (exist) {
                if ($('regEmail').value.trim().toLowerCase() !== v) return;
                if (exist) {
                    setFieldStatus('regEmailStatus', 'err');
                    showFieldError('regEmailErr', BF.i18n.t('register.error.emailTaken'));
                } else {
                    setFieldStatus('regEmailStatus', 'ok');
                }
            }).catch(function () {
                setFieldStatus('regEmailStatus', '');
            });
        }, 500);
    }

    // ─────────────── navigation ───────────────

    function configFooter() {
        backBtn.hidden = !(step === 2 || step === 3 || step === 5);
        skipBtn.hidden = !(step === 6 || step === 7 || step === 8);
        var label = BF.i18n.t('common.next.step');
        if (step === 9) label = BF.i18n.t('register.goToChats');
        nextLabel.textContent = label;
        nextBtn.hidden = step === 8;
        progressBar.style.width = ((STEP_ORDER.indexOf(step) + 1) / TOTAL_STEPS * 100) + '%';
        stepLabel.textContent = BF.i18n.t('register.step', { step: STEP_ORDER.indexOf(step) + 1, total: TOTAL_STEPS });
        titleEl.textContent = BF.i18n.t(STEP_TITLE_KEYS[step]);
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
        if (n === 9) {
            var name = state.firstName + (state.lastName ? ' ' + state.lastName : '');
            $('regCompleteName').textContent = BF.i18n.t('register.welcomeUser', { name: name, username: state.username });
        } else {
            // focus first text input
            var input = el && el.querySelector('input.form-input, textarea.form-input');
            if (input) setTimeout(function () { input.focus(); }, 50);
        }
    }

    function handleNext() {
        clearStepErrors();
        switch (step) {
            case 1: return doStep1();
            case 2: return doStep2();
            case 3: return doStep3();
            case 5: return doStep5();
            case 6: return doStep6();
            case 7: return doStep7();
            case 8: return goToStep(9);
            case 9: window.location.href = '/messenger'; return;
        }
    }

    function doStep1() {
        var first = $('regFirstName').value.trim();
        var last = $('regLastName').value.trim();
        if (first.length < 3) return showFieldError('regFirstNameErr', BF.i18n.t('register.error.min3'));
        if (first.length > 40) return showFieldError('regFirstNameErr', BF.i18n.t('register.error.max40'));
        if (last.length > 40) return showFieldError('regLastNameErr', BF.i18n.t('register.error.max40'));
        state.firstName = first;
        state.lastName = last;
        goToStep(2);
    }

    function doStep2() {
        var v = $('regUsername').value.toLowerCase().trim();
        if (v.length < 3 || v.length > 30 || !USERNAME_RE.test(v) || /^[0-9]/.test(v)) {
            return showFieldError('regUsernameErr', BF.i18n.t('register.error.usernameFormat'));
        }
        state.username = v;
        setLoading(nextBtn, true);
        checkUsername(v).then(function (exist) {
            setLoading(nextBtn, false);
            if (exist) return showFieldError('regUsernameErr', BF.i18n.t('register.error.usernameTaken'));
            goToStep(3);
        }).catch(function () {
            // allow proceeding; CreateAccount will re-validate
            setLoading(nextBtn, false);
            goToStep(3);
        });
    }

    function doStep3() {
        var channel = Number($('regChannel').value);
        var email = $('regEmail').value.trim().toLowerCase();
        if (!registrationChannelReady || $('regChannel').selectedOptions[0].disabled) {
            return showFieldError('regEmailErr', BF.i18n.t('auth.error.network'));
        }
        if (channel === 2 && !EMAIL_RE.test(email)) return showFieldError('regEmailErr', BF.i18n.t('register.error.badEmail'));
        state.email = channel === 2 ? email : '';
        goToStep(5);
    }

    async function doStep5() {
        var pw = $('regPassword').value, pw2 = $('regPassword2').value;
        if (pw.length < 8 || new TextEncoder().encode(pw).length > 72) return showFieldError('regPasswordErr', BF.i18n.t('security.passwordLength'));
        if (pw !== pw2) return showFieldError('regPasswordErr', BF.i18n.t('password.error.mismatch'));
        var origin = BF.node.origin(), generation = registrationGeneration;
        setLoading(nextBtn, true);
        try {
            var response = await BF.authUI.run('beginRegistration', 'BeginRegistrationRequest', {
                username: state.username, firstName: state.firstName, lastName: state.lastName,
                password: pw, email: state.email, confirmationMethod: Number($('regChannel').value), loginMode: Number($('regLoginMode').value)
            });
            if (origin !== BF.node.origin() || generation !== registrationGeneration) return;
            var session = BF.confirmations.session(response.getSession());
            Object.assign(state, session); BF.tokens.setTempMode(false); BF.tokens.save(session);
            $('regPassword').value = ''; $('regPassword2').value = '';
            BF.legal.flushConsent(); goToStep(6);
        } catch (error) {
            if (error.name !== 'AbortError') showFieldError('regPasswordErr', BF.authUI.errorText(error));
        } finally { setLoading(nextBtn, false); }
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

    // ─────────────── open / close ───────────────

    function resetState() {
        state = {
            firstName: '', lastName: '', username: '', email: '', codeId: '',
            accessToken: '', accessTokenExpiration: 0,
            refreshToken: '', refreshTokenExpiration: 0,
            twoFaSecret: '', avatarBlob: null, avatarFileId: ''
        };
        dialog.querySelectorAll('input.form-input, textarea.form-input').forEach(function (i) { i.value = ''; });
        setFieldStatus('regUsernameStatus', '');
        setFieldStatus('regEmailStatus', '');
        var prev = $('regAvatarPreview');
        if (prev) { prev.hidden = true; prev.removeAttribute('src'); }
        if ($('regAvatarPlus')) $('regAvatarPlus').hidden = false;
        if ($('regAvatarPick')) $('regAvatarPick').textContent = BF.i18n.t('register.pickPhoto');
        if ($('regCropper')) $('regCropper').hidden = true;
        if ($('regAvatarEmpty')) $('regAvatarEmpty').hidden = false;
        if (crop.objUrl) { URL.revokeObjectURL(crop.objUrl); crop.objUrl = null; }
        crop.img = null;
        $('regStrengthBar').style.width = '0%';
        $('regStrengthLabel').textContent = '';
        $('regBioCount').textContent = '0';
    }

    function open() {
        registrationGeneration++; registrationController.abort(); registrationController = new AbortController();
        if (BF.fastAuth) BF.fastAuth.cancel();
        resetState();
        var generation = registrationGeneration;
        registrationChannelReady = false;
        $('regChannel').options[0].disabled = true;
        $('regChannel').options[1].disabled = true;
        $('regChannel').value = '2';
        updateChannel();
        BF.confirmations.call('getAuthCapabilities', 'GetAuthCapabilitiesRequest', {}, { read: true, signal: registrationController.signal }).then(function (capabilities) {
            if (generation !== registrationGeneration) return;
            var emailAvailable = capabilities.getEmailAvailable();
            var telegramAvailable = capabilities.getTelegramAvailable();
            $('regChannel').options[0].disabled = !emailAvailable;
            $('regChannel').options[1].disabled = !telegramAvailable;
            registrationChannelReady = emailAvailable || telegramAvailable;
            $('regChannel').value = emailAvailable ? '2' : '3';
            updateChannel();
            if (!registrationChannelReady) showFieldError('regEmailErr', BF.i18n.t('auth.error.network'));
        }).catch(function (error) { if (error.name !== 'AbortError') showFieldError('regEmailErr', BF.authUI.errorText(error)); });
        goToStep(1);
        BF.utils.openOverlay(overlay);
        document.body.style.overflow = 'hidden';
    }

    function close() {
        registrationGeneration++; registrationController.abort(); BF.authUI.cancelAll();
        clearTimeout(usernameDebounce); clearTimeout(emailDebounce);
        if (overlay) BF.utils.closeOverlay(overlay);
        document.body.style.overflow = '';
    }

    // ─────────────── init ───────────────

    function updateChannel() {
        var telegram = $('regChannel').value === '3';
        $('regEmail').closest('.form-group').hidden = telegram;
        $('regLoginMode').querySelector('option[value="2"]').disabled = !telegram;
        $('regLoginMode').value = telegram ? '3' : '1';
    }

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


        var openBtn = $('toRegisterBtn');
        if (openBtn) openBtn.addEventListener('click', open);

        $('regClose').addEventListener('click', close);
        backBtn.addEventListener('click', function () { if (step > 1) goToStep(STEP_ORDER[STEP_ORDER.indexOf(step) - 1]); });
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

        $('regChannel').addEventListener('change', updateChannel);
        $('reg2faEnable').addEventListener('click', async function () {
            this.disabled = true;
            try { await BF.authUI.setupFactor(1); if (!registrationController.signal.aborted) goToStep(9); }
            catch (error) { if (error.name !== 'AbortError') showFieldError('reg2faErr', BF.authUI.errorText(error)); }
            finally { this.disabled = false; }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.BF.register = { open: open, close: close };
})();
