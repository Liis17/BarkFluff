/**
 * Settings dialog — multi-view panel: profile, name/username, bio, password, 2FA, sessions.
 * Requires: BF.api, BF.files, BF.tokens, BF.realtime, BF.device, BF.utils
 * Exposes: BF.settings
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var overlay, backBtn, titleEl, body, confirmOverlay;
    var myUserId = null;
    var currentUser = null;
    var viewStack = [];

    // OtpTypeId enum values (mirrors proto)
    var OTP_AUTHENTICATOR = 1;
    var OTP_EMAIL = 2;

    // ProfileFieldVisibility enum values (mirrors proto)
    var VIS_ALL = 0;
    var VIS_FRIENDS = 1;
    var VIS_NONE = 2;
    var VIS_OPTIONS = [
        { value: VIS_ALL, label: 'Все' },
        { value: VIS_FRIENDS, label: 'Друзья' },
        { value: VIS_NONE, label: 'Никто' }
    ];

    // UploadFileType enum values used here
    var FT_MESSAGE_ATTACHMENT_IMAGE = 2;
    var FT_USER_PROFILE_POSTER = 10;

    // Web app version (shown in About)
    var WEB_VERSION = '1.0';

    // Error code for wrong old password
    var ERR_WRONG_OLD_PASSWORD = 'A7E3F1B2-9C4D-4E8A-B5F6-2D1A3C7E9F04';

    function init(opts) {
        myUserId = opts.myUserId;
        overlay = document.querySelector('#settingsOverlay');
        backBtn = document.querySelector('#sdBack');
        titleEl = document.querySelector('#sdTitle');
        body = document.querySelector('#sdBody');
        confirmOverlay = document.querySelector('#confirmOverlay');

        document.querySelector('#sdClose').addEventListener('click', close);
        backBtn.addEventListener('click', goBack);
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) close();
        });

        document.querySelector('#confirmCancel').addEventListener('click', function () {
            confirmOverlay.classList.remove('visible');
        });
        document.querySelector('#confirmOk').addEventListener('click', function () {
            BF.realtime.stopAll();
            BF.tokens.clear();
            window.location.href = '/';
        });
    }

    function open() {
        viewStack = [];
        currentUser = null;
        showView('main');
        overlay.classList.add('visible');
    }

    function close() {
        overlay.classList.remove('visible');
        viewStack = [];
    }

    function goBack() {
        viewStack.pop();
        if (viewStack.length === 0) {
            showView('main');
        } else {
            showView(viewStack[viewStack.length - 1]);
        }
    }

    function navigate(name) {
        viewStack.push(name);
        showView(name);
    }

    function showView(name) {
        backBtn.classList.toggle('visible', viewStack.length > 0);
        switch (name) {
            case 'main':            renderMain(); break;
            case 'profile':         renderProfile(); break;
            case 'name':            renderName(); break;
            case 'bio':             renderBio(); break;
            case 'password':        renderPassword(); break;
            case 'twofa':           renderTwoFA(); break;
            case 'sessions':        renderSessions(); break;
            case 'privacy':         renderPrivacy(); break;
            case 'personalization': renderPersonalization(); break;
            case 'about':           renderAbout(); break;
        }
    }

    // ========== HELPERS ==========

    function makeField(label, inputEl) {
        var wrap = document.createElement('div');
        wrap.className = 'sd-field';
        var lbl = document.createElement('div');
        lbl.className = 'sd-label';
        lbl.textContent = label;
        wrap.appendChild(lbl);
        wrap.appendChild(inputEl);
        return wrap;
    }

    function makeInput(type, placeholder, value) {
        var el = document.createElement('input');
        el.type = type || 'text';
        el.className = 'sd-input';
        el.placeholder = placeholder || '';
        if (value !== undefined && value !== null) el.value = value;
        return el;
    }

    function makeHint(text, isError) {
        var el = document.createElement('div');
        el.className = 'sd-hint' + (isError ? ' error' : '');
        el.textContent = text;
        return el;
    }

    function makeSaveBtn(label) {
        var btn = document.createElement('button');
        btn.className = 'sd-btn sd-btn-primary';
        btn.textContent = label || 'Сохранить';
        return btn;
    }

    function extractErrorCode(err) {
        if (!err) return null;
        var msg = (err.message || err.toString()) + (err.metadata ? JSON.stringify(err.metadata) : '');
        var m = msg.match(/[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}/i);
        return m ? m[0].toUpperCase() : null;
    }

    function loadCurrentUser() {
        if (currentUser) return Promise.resolve(currentUser);
        return BF.api.getUser(myUserId).then(function (d) {
            if (d && d.user) currentUser = d.user;
            return currentUser;
        });
    }

    function makeToggleRow(title, desc, initial, onChange) {
        var row = document.createElement('div');
        row.className = 'sd-toggle-row';
        var info = document.createElement('div');
        info.className = 'sd-toggle-info';
        var t = document.createElement('div');
        t.className = 'sd-toggle-title';
        t.textContent = title;
        info.appendChild(t);
        if (desc) {
            var d = document.createElement('div');
            d.className = 'sd-toggle-desc';
            d.textContent = desc;
            info.appendChild(d);
        }
        var sw = document.createElement('button');
        sw.type = 'button';
        sw.className = 'sd-switch' + (initial ? ' on' : '');
        sw.setAttribute('aria-pressed', initial ? 'true' : 'false');
        sw.addEventListener('click', function () {
            var next = !sw.classList.contains('on');
            sw.classList.toggle('on', next);
            sw.setAttribute('aria-pressed', next ? 'true' : 'false');
            if (onChange) onChange(next, sw);
        });
        row.appendChild(info);
        row.appendChild(sw);
        return { row: row, getValue: function () { return sw.classList.contains('on'); }, setValue: function (v) {
            sw.classList.toggle('on', !!v);
            sw.setAttribute('aria-pressed', v ? 'true' : 'false');
        }, setDisabled: function (v) { sw.disabled = !!v; } };
    }

    function makeSegmented(options, initial, onChange) {
        var wrap = document.createElement('div');
        wrap.className = 'sd-segmented';
        var current = initial;
        var btns = [];
        options.forEach(function (opt) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'sd-segmented-option' + (opt.value === initial ? ' active' : '');
            b.textContent = opt.label;
            b.addEventListener('click', function () {
                if (current === opt.value) return;
                current = opt.value;
                btns.forEach(function (x) {
                    x.classList.toggle('active', x.dataset.val === String(opt.value));
                });
                if (onChange) onChange(opt.value);
            });
            b.dataset.val = String(opt.value);
            btns.push(b);
            wrap.appendChild(b);
        });
        return { el: wrap, getValue: function () { return current; } };
    }

    function renderAvatarEl(user, className) {
        var av = document.createElement('div');
        av.className = className || 'sd-avatar';
        if (user && user.profilePicture) {
            var img = document.createElement('img');
            img.src = user.profilePicture;
            img.alt = '';
            av.appendChild(img);
        } else {
            var letter = (user && (user.firstName || user.username)) || '?';
            av.textContent = letter[0].toUpperCase();
        }
        return av;
    }

    // ========== VIEWS ==========

    function renderMain() {
        titleEl.textContent = 'Настройки';
        body.innerHTML = '';

        // Profile block (avatar + name + username) → navigate to profile
        var profileBlock = document.createElement('div');
        profileBlock.className = 'sd-profile-block';
        var avatarEl = document.createElement('div');
        avatarEl.className = 'sd-avatar';
        avatarEl.textContent = '…';
        var userInfo = document.createElement('div');
        var nameEl = document.createElement('div');
        nameEl.className = 'sd-user-name';
        nameEl.textContent = '…';
        var unameEl = document.createElement('div');
        unameEl.className = 'sd-user-username';
        var arrowEl = document.createElement('div');
        arrowEl.className = 'sd-user-arrow';
        arrowEl.textContent = '›';
        userInfo.appendChild(nameEl);
        userInfo.appendChild(unameEl);
        profileBlock.appendChild(avatarEl);
        profileBlock.appendChild(userInfo);
        profileBlock.appendChild(arrowEl);
        profileBlock.addEventListener('click', function () { navigate('profile'); });
        body.appendChild(profileBlock);

        loadCurrentUser().then(function (user) {
            if (!user) return;
            // Update avatar
            profileBlock.replaceChild(renderAvatarEl(user, 'sd-avatar'), avatarEl);
            nameEl.textContent = [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username || '';
            unameEl.textContent = user.username ? '@' + user.username : '';
        });

        // Section: Account
        var secAccount = makeSection('Аккаунт', [
            { icon: '✏️', label: 'Имя и юзернейм', view: 'name' },
            { icon: '📝', label: 'Биография', view: 'bio' }
        ]);
        body.appendChild(secAccount);

        // Section: Privacy
        var secPrivacy = makeSection('Конфиденциальность', [
            { icon: '🛡️', label: 'Приватность', view: 'privacy' }
        ]);
        body.appendChild(secPrivacy);

        // Section: Security
        var secSecurity = makeSection('Безопасность', [
            { icon: '🔒', label: 'Пароль', view: 'password' },
            { icon: '🛡️', label: 'Двухфакторная аутентификация', view: 'twofa' }
        ]);
        body.appendChild(secSecurity);

        // Section: Personalization
        var secPers = makeSection('Персонализация', [
            { icon: '🎨', label: 'Фон чата и постер', view: 'personalization' }
        ]);
        body.appendChild(secPers);

        // Section: Devices
        var secDevices = makeSection('Устройства', [
            { icon: '📱', label: 'Активные сессии', view: 'sessions' }
        ]);
        body.appendChild(secDevices);

        // Section: About
        var secAbout = makeSection('О приложении', [
            { icon: 'ℹ️', label: 'О BarkFluff', view: 'about' }
        ]);
        body.appendChild(secAbout);

        // Logout button with SVG icon
        var logoutBtn = document.createElement('button');
        logoutBtn.className = 'btn-logout-settings';
        logoutBtn.id = 'settingsLogoutBtn';
        logoutBtn.innerHTML =
            '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
            '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/>' +
            '<polyline points="16 17 21 12 16 7"/>' +
            '<line x1="21" y1="12" x2="9" y2="12"/>' +
            '</svg>' +
            '<span>Выйти из аккаунта</span>';
        logoutBtn.addEventListener('click', function () {
            confirmOverlay.classList.add('visible');
        });
        body.appendChild(logoutBtn);
    }

    function makeSection(title, items) {
        var sec = document.createElement('div');
        sec.className = 'sd-section';
        var titleEl2 = document.createElement('div');
        titleEl2.className = 'sd-section-title';
        titleEl2.textContent = title;
        sec.appendChild(titleEl2);
        items.forEach(function (item) {
            var row = document.createElement('div');
            row.className = 'sd-item';
            row.innerHTML =
                '<span class="sd-item-icon">' + item.icon + '</span>' +
                '<span class="sd-item-label">' + item.label + '</span>' +
                '<span class="sd-item-arrow">›</span>';
            row.addEventListener('click', function () { navigate(item.view); });
            sec.appendChild(row);
        });
        return sec;
    }

    // --- Profile (avatar upload) ---
    function renderProfile() {
        titleEl.textContent = 'Фото профиля';
        body.innerHTML = '';

        var avatarUpload = document.createElement('div');
        avatarUpload.className = 'sd-avatar-upload';

        var avatarLarge = document.createElement('div');
        avatarLarge.className = 'sd-avatar-large';
        avatarLarge.textContent = '…';

        var hint = document.createElement('div');
        hint.className = 'sd-avatar-hint';
        hint.textContent = 'Нажмите, чтобы изменить фото';

        var fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.accept = 'image/*';
        fileInput.style.display = 'none';

        var statusEl = document.createElement('div');
        statusEl.className = 'sd-hint';

        avatarUpload.appendChild(avatarLarge);
        avatarUpload.appendChild(hint);
        avatarUpload.appendChild(fileInput);
        avatarUpload.appendChild(statusEl);
        body.appendChild(avatarUpload);

        loadCurrentUser().then(function (user) {
            if (!user) return;
            // Replace placeholder with actual avatar
            var avEl = renderAvatarEl(user, 'sd-avatar-large');
            avatarLarge.parentNode.replaceChild(avEl, avatarLarge);
            avatarLarge = avEl;
            avatarLarge.style.cursor = 'pointer';
            avatarLarge.addEventListener('click', function () { fileInput.click(); });
        });

        avatarLarge.style.cursor = 'pointer';
        avatarLarge.addEventListener('click', function () { fileInput.click(); });

        fileInput.addEventListener('change', function () {
            var file = fileInput.files[0];
            if (!file) return;
            statusEl.textContent = 'Загрузка…';
            statusEl.className = 'sd-hint';
            // USER_AVATAR = 1
            BF.files.uploadFile(file, 1).then(function (fileId) {
                return BF.api.setProfilePicture(fileId).then(function () {
                    statusEl.textContent = 'Фото обновлено';
                    // Update cached user picture
                    if (currentUser) {
                        var url = URL.createObjectURL(file);
                        currentUser.profilePicture = url;
                        // Update avatar shown
                        var img = document.createElement('img');
                        img.src = url;
                        img.alt = '';
                        avatarLarge.innerHTML = '';
                        avatarLarge.appendChild(img);
                    }
                });
            }).catch(function () {
                statusEl.textContent = 'Ошибка загрузки';
                statusEl.className = 'sd-hint error';
            });
        });
    }

    // --- Name & Username ---
    function renderName() {
        titleEl.textContent = 'Имя и юзернейм';
        body.innerHTML = '';

        var form = document.createElement('div');
        form.className = 'sd-form';

        var fnInput = makeInput('text', 'Имя', '');
        var lnInput = makeInput('text', 'Фамилия', '');
        var unInput = makeInput('text', 'Юзернейм', '');
        var unHint = makeHint('');
        var saveBtn = makeSaveBtn();

        form.appendChild(makeField('Имя', fnInput));
        form.appendChild(makeField('Фамилия', lnInput));
        var unField = makeField('Юзернейм', unInput);
        unField.appendChild(unHint);
        form.appendChild(unField);
        form.appendChild(saveBtn);
        body.appendChild(form);

        var origUsername = '';
        loadCurrentUser().then(function (user) {
            if (!user) return;
            fnInput.value = user.firstName || '';
            lnInput.value = user.lastName || '';
            unInput.value = user.username || '';
            origUsername = user.username || '';
        });

        // Debounce username check
        var unTimer = null;
        var unAvailable = true;
        unInput.addEventListener('input', function () {
            clearTimeout(unTimer);
            var val = unInput.value.trim();
            if (!val || val === origUsername) {
                unHint.textContent = '';
                unHint.className = 'sd-hint';
                unAvailable = true;
                return;
            }
            unTimer = setTimeout(function () {
                BF.api.checkExistUsername(val).then(function (r) {
                    if (unInput.value.trim() !== val) return;
                    if (r.exist) {
                        unHint.textContent = 'Юзернейм уже занят';
                        unHint.className = 'sd-hint error';
                        unAvailable = false;
                    } else {
                        unHint.textContent = 'Юзернейм свободен';
                        unHint.className = 'sd-hint';
                        unAvailable = true;
                    }
                });
            }, 500);
        });

        saveBtn.addEventListener('click', function () {
            if (!unAvailable) return;
            saveBtn.disabled = true;
            var fn = fnInput.value.trim();
            var ln = lnInput.value.trim();
            var un = unInput.value.trim();

            var namePromise = BF.api.changeName(fn, ln);
            var unPromise = (un !== origUsername && un)
                ? BF.api.changeUsername(un)
                : Promise.resolve();

            Promise.all([namePromise, unPromise]).then(function () {
                if (currentUser) {
                    currentUser.firstName = fn;
                    currentUser.lastName = ln;
                    if (un !== origUsername) currentUser.username = un;
                }
                origUsername = un;
                saveBtn.disabled = false;
                // Show success feedback
                var ok = document.createElement('div');
                ok.className = 'sd-hint';
                ok.textContent = 'Сохранено';
                form.appendChild(ok);
                setTimeout(function () { if (ok.parentNode) ok.parentNode.removeChild(ok); }, 2000);
            }).catch(function () {
                saveBtn.disabled = false;
            });
        });
    }

    // --- Bio ---
    function renderBio() {
        titleEl.textContent = 'Биография';
        body.innerHTML = '';

        var form = document.createElement('div');
        form.className = 'sd-form';

        var bioInput = document.createElement('textarea');
        bioInput.className = 'sd-input';
        bioInput.rows = 4;
        bioInput.placeholder = 'Расскажите о себе…';
        bioInput.style.resize = 'vertical';

        var saveBtn = makeSaveBtn();
        form.appendChild(makeField('Биография', bioInput));
        form.appendChild(saveBtn);
        body.appendChild(form);

        loadCurrentUser().then(function (user) {
            if (user) bioInput.value = user.bio || '';
        });

        saveBtn.addEventListener('click', function () {
            saveBtn.disabled = true;
            BF.api.changeBio(bioInput.value.trim()).then(function () {
                if (currentUser) currentUser.bio = bioInput.value.trim();
                saveBtn.disabled = false;
                var ok = document.createElement('div');
                ok.className = 'sd-hint';
                ok.textContent = 'Сохранено';
                form.appendChild(ok);
                setTimeout(function () { if (ok.parentNode) ok.parentNode.removeChild(ok); }, 2000);
            }).catch(function () { saveBtn.disabled = false; });
        });
    }

    // --- Password ---
    function renderPassword() {
        titleEl.textContent = 'Пароль';
        body.innerHTML = '';

        var form = document.createElement('div');
        form.className = 'sd-form';

        var oldInput = makeInput('password', 'Текущий пароль', '');
        var newInput = makeInput('password', 'Новый пароль', '');
        var repInput = makeInput('password', 'Повтор пароля', '');
        var errEl = makeHint('', true);
        errEl.style.display = 'none';
        var saveBtn = makeSaveBtn('Изменить пароль');

        form.appendChild(makeField('Текущий пароль', oldInput));
        form.appendChild(makeField('Новый пароль', newInput));
        form.appendChild(makeField('Повтор нового пароля', repInput));
        form.appendChild(errEl);
        form.appendChild(saveBtn);
        body.appendChild(form);

        saveBtn.addEventListener('click', function () {
            errEl.style.display = 'none';
            var op = oldInput.value;
            var np = newInput.value;
            var rp = repInput.value;
            if (!np) { showErr('Введите новый пароль'); return; }
            if (np !== rp) { showErr('Пароли не совпадают'); return; }

            saveBtn.disabled = true;
            BF.api.setPassword(np, op || undefined).then(function () {
                saveBtn.disabled = false;
                oldInput.value = '';
                newInput.value = '';
                repInput.value = '';
                var ok = document.createElement('div');
                ok.className = 'sd-hint';
                ok.textContent = 'Пароль изменён';
                form.appendChild(ok);
                setTimeout(function () { if (ok.parentNode) ok.parentNode.removeChild(ok); }, 2000);
            }).catch(function (err) {
                saveBtn.disabled = false;
                var code = extractErrorCode(err);
                if (code === ERR_WRONG_OLD_PASSWORD) {
                    showErr('Неверный текущий пароль');
                } else {
                    showErr('Ошибка при изменении пароля');
                }
            });

            function showErr(msg) {
                errEl.textContent = msg;
                errEl.style.display = '';
            }
        });
    }

    // --- Two-Factor Authentication ---
    function renderTwoFA() {
        titleEl.textContent = 'Двухфакторная аутентификация';
        body.innerHTML = '';
        body.innerHTML = '<div class="sd-hint" style="padding:20px">Загрузка…</div>';

        BF.api.listOtpVerification().then(function (data) {
            body.innerHTML = '';
            renderTwoFARow('Authenticator (TOTP)', data.authenticatorEnabled, OTP_AUTHENTICATOR);
            renderTwoFARow('Почта (Email)', data.emailEnabled, OTP_EMAIL);
        }).catch(function () {
            body.innerHTML = '<div class="sd-hint error" style="padding:20px">Ошибка загрузки</div>';
        });
    }

    function renderTwoFARow(label, enabled, otpType) {
        var row = document.createElement('div');
        row.className = 'twofa-status';

        var typeEl = document.createElement('div');
        typeEl.className = 'twofa-type';
        typeEl.textContent = label;

        var badge = document.createElement('span');
        badge.className = 'twofa-badge ' + (enabled ? 'on' : 'off');
        badge.textContent = enabled ? 'Включён' : 'Выключен';

        var toggleBtn = document.createElement('button');
        toggleBtn.className = 'twofa-toggle ' + (enabled ? 'disable' : 'enable');
        toggleBtn.textContent = enabled ? 'Отключить' : 'Включить';

        row.appendChild(typeEl);
        row.appendChild(badge);
        row.appendChild(toggleBtn);
        body.appendChild(row);

        if (enabled) {
            // Disable flow
            toggleBtn.addEventListener('click', function () {
                if (otpType === OTP_AUTHENTICATOR) {
                    // Need OTP code to disable
                    renderTwoFADisableAuthenticator();
                } else {
                    // Email: disable without code
                    toggleBtn.disabled = true;
                    BF.api.disableOtpVerification(otpType, null).then(function () {
                        renderTwoFA();
                    }).catch(function () { toggleBtn.disabled = false; });
                }
            });
        } else {
            // Enable flow
            toggleBtn.addEventListener('click', function () {
                if (otpType === OTP_AUTHENTICATOR) {
                    renderTwoFAEnableAuthenticator();
                } else {
                    toggleBtn.disabled = true;
                    BF.api.enableOtpVerification(otpType).then(function () {
                        renderTwoFA();
                    }).catch(function () { toggleBtn.disabled = false; });
                }
            });
        }
    }

    function renderTwoFAEnableAuthenticator() {
        body.innerHTML = '<div class="sd-hint" style="padding:20px">Создание QR-кода…</div>';
        BF.api.enableOtpVerification(OTP_AUTHENTICATOR).then(function (data) {
            body.innerHTML = '';
            var form = document.createElement('div');
            form.className = 'sd-form';

            var instr = document.createElement('div');
            instr.className = 'sd-hint';
            instr.textContent = 'Отсканируйте QR-код в приложении аутентификатора (Google Authenticator, Authy и др.)';
            form.appendChild(instr);

            if (data.otpQr) {
                var img = document.createElement('img');
                img.src = 'data:image/png;base64,' + data.otpQr;
                img.style.cssText = 'width:180px;height:180px;display:block;margin:0 auto;border-radius:8px;';
                form.appendChild(img);
            }
            if (data.otpCode) {
                var codeEl = document.createElement('div');
                codeEl.style.cssText = 'text-align:center;font-family:monospace;font-size:16px;letter-spacing:2px;padding:8px;background:rgba(0,0,0,0.05);border-radius:8px;';
                codeEl.textContent = data.otpCode;
                form.appendChild(codeEl);
            }

            var otpInput = makeInput('text', 'Код из приложения', '');
            otpInput.maxLength = 8;
            var errEl = makeHint('', true);
            errEl.style.display = 'none';
            var confirmBtn = makeSaveBtn('Подтвердить');

            form.appendChild(makeField('Код подтверждения', otpInput));
            form.appendChild(errEl);
            form.appendChild(confirmBtn);
            body.appendChild(form);

            confirmBtn.addEventListener('click', function () {
                var code = otpInput.value.trim();
                if (!code) return;
                confirmBtn.disabled = true;
                BF.api.confirmOtpVerification(code).then(function () {
                    renderTwoFA();
                }).catch(function () {
                    confirmBtn.disabled = false;
                    errEl.textContent = 'Неверный код';
                    errEl.style.display = '';
                });
            });
        }).catch(function () {
            body.innerHTML = '<div class="sd-hint error" style="padding:20px">Ошибка</div>';
        });
    }

    function renderTwoFADisableAuthenticator() {
        body.innerHTML = '';
        var form = document.createElement('div');
        form.className = 'sd-form';

        var instr = document.createElement('div');
        instr.className = 'sd-hint';
        instr.textContent = 'Введите код из приложения аутентификатора для отключения.';
        form.appendChild(instr);

        var otpInput = makeInput('text', 'Код из приложения', '');
        otpInput.maxLength = 8;
        var errEl = makeHint('', true);
        errEl.style.display = 'none';
        var confirmBtn = makeSaveBtn('Отключить');
        confirmBtn.className = 'sd-btn';
        confirmBtn.style.cssText = 'background:rgba(220,38,38,0.1);color:var(--error);';

        form.appendChild(makeField('Код подтверждения', otpInput));
        form.appendChild(errEl);
        form.appendChild(confirmBtn);
        body.appendChild(form);

        confirmBtn.addEventListener('click', function () {
            var code = otpInput.value.trim();
            if (!code) return;
            confirmBtn.disabled = true;
            BF.api.disableOtpVerification(OTP_AUTHENTICATOR, code).then(function () {
                renderTwoFA();
            }).catch(function () {
                confirmBtn.disabled = false;
                errEl.textContent = 'Неверный код';
                errEl.style.display = '';
            });
        });
    }

    // --- Sessions ---
    function renderSessions() {
        titleEl.textContent = 'Активные сессии';
        body.innerHTML = '<div class="sd-hint" style="padding:20px">Загрузка…</div>';

        var currentDeviceId = BF.device ? BF.device.getDeviceId() : null;

        BF.api.getActiveSessions().then(function (data) {
            body.innerHTML = '';
            var sessions = data.sessions || [];
            if (sessions.length === 0) {
                body.innerHTML = '<div class="sd-hint" style="padding:20px">Нет активных сессий</div>';
                return;
            }

            var otherSessions = sessions.filter(function (s) {
                return !(s.deviceId && currentDeviceId && s.deviceId === currentDeviceId);
            });

            if (otherSessions.length > 0) {
                var termAllBtn = document.createElement('button');
                termAllBtn.className = 'sessions-terminate-all';
                termAllBtn.textContent = 'Завершить все остальные сессии (' + otherSessions.length + ')';
                termAllBtn.addEventListener('click', function () {
                    if (!window.confirm('Завершить ' + otherSessions.length + ' остальных сессий? Они будут разлогинены.')) return;
                    termAllBtn.disabled = true;
                    var p = Promise.resolve();
                    otherSessions.forEach(function (s) {
                        p = p.then(function () { return BF.api.removeActiveSession(s.deviceId).catch(function () {}); });
                    });
                    p.then(function () { renderSessions(); });
                });
                body.appendChild(termAllBtn);
            }

            sessions.forEach(function (s) { body.appendChild(buildSessionItem(s, currentDeviceId)); });
        }).catch(function () {
            body.innerHTML = '<div class="sd-hint error" style="padding:20px">Ошибка загрузки</div>';
        });
    }

    function buildSessionItem(s, currentDeviceId) {
        var isCurrent = s.deviceId && currentDeviceId && s.deviceId === currentDeviceId;
        var item = document.createElement('div');
        item.className = 'session-item' + (isCurrent ? ' current' : '');

        var info = document.createElement('div');
        info.className = 'session-info';

        var name = document.createElement('div');
        name.className = 'session-name';
        name.textContent = s.customName || s.originalName || s.appName || 'Устройство';

        var meta = document.createElement('div');
        meta.className = 'session-meta';
        var parts = [];
        if (s.operationSystem) parts.push(s.operationSystem);
        if (s.location) parts.push(s.location);
        if (s.createdAt) parts.push('с ' + new Date(s.createdAt).toLocaleDateString('ru'));
        meta.textContent = parts.join(' · ');

        info.appendChild(name);
        info.appendChild(meta);
        item.appendChild(info);

        if (isCurrent) {
            var badge = document.createElement('span');
            badge.style.cssText = 'font-size:11px;color:var(--primary);font-weight:600;flex-shrink:0;';
            badge.textContent = 'Это устройство';
            item.appendChild(badge);
            return item;
        }

        var actions = document.createElement('div');
        actions.className = 'session-actions';

        var renameBtn = document.createElement('button');
        renameBtn.className = 'session-rename';
        renameBtn.textContent = 'Переименовать';
        renameBtn.addEventListener('click', function () {
            if (item.querySelector('.session-rename-input')) return;
            var box = document.createElement('div');
            box.className = 'session-rename-input';
            var input = document.createElement('input');
            input.type = 'text';
            input.maxLength = 64;
            input.value = s.customName || s.originalName || '';
            input.placeholder = 'Имя устройства';
            var ok = document.createElement('button');
            ok.className = 'session-rename';
            ok.textContent = 'OK';
            ok.addEventListener('click', function () {
                var newName = input.value.trim();
                ok.disabled = true;
                BF.api.renameDevice(s.deviceId, newName).then(function () {
                    s.customName = newName;
                    name.textContent = newName || s.originalName || s.appName || 'Устройство';
                    info.removeChild(box);
                }).catch(function () { ok.disabled = false; });
            });
            box.appendChild(input);
            box.appendChild(ok);
            info.appendChild(box);
            input.focus();
            input.select();
        });

        var termBtn = document.createElement('button');
        termBtn.className = 'session-terminate';
        termBtn.textContent = 'Завершить';
        termBtn.addEventListener('click', function () {
            termBtn.disabled = true;
            BF.api.removeActiveSession(s.deviceId).then(function () {
                item.parentNode && item.parentNode.removeChild(item);
            }).catch(function () { termBtn.disabled = false; });
        });

        actions.appendChild(renameBtn);
        actions.appendChild(termBtn);
        item.appendChild(actions);
        return item;
    }

    // --- Privacy ---
    function renderPrivacy() {
        titleEl.textContent = 'Приватность';
        body.innerHTML = '<div class="sd-hint" style="padding:20px">Загрузка…</div>';

        BF.api.getPrivacySettings().then(function (data) {
            var s = data.settings || {
                profileVisibleOnSite: true, avatarVisibility: VIS_ALL,
                bioVisibility: VIS_ALL, emailVisibility: VIS_NONE,
                searchVisible: true, onlineVisibility: VIS_ALL
            };
            body.innerHTML = '';

            var togglesSec = document.createElement('div');
            togglesSec.className = 'sd-section';
            var profileToggle = makeToggleRow(
                'Профиль виден на сайте',
                'Страница barkfluff.com/' + (currentUser && currentUser.username ? currentUser.username : 'username') + ' доступна другим',
                s.profileVisibleOnSite, null
            );
            var searchToggle = makeToggleRow(
                'Показывать в поиске',
                'Другие пользователи смогут находить вас по имени',
                s.searchVisible, null
            );
            togglesSec.appendChild(profileToggle.row);
            togglesSec.appendChild(searchToggle.row);
            body.appendChild(togglesSec);

            var segments = [
                { key: 'avatarVisibility', label: 'Видимость аватара' },
                { key: 'bioVisibility',    label: 'Видимость описания' },
                { key: 'emailVisibility',  label: 'Видимость email' },
                { key: 'onlineVisibility', label: 'Видимость онлайн-статуса' }
            ];
            var segCtrls = {};
            segments.forEach(function (seg) {
                var field = document.createElement('div');
                field.className = 'sd-field';
                field.style.padding = '12px 20px 0';
                var lbl = document.createElement('div');
                lbl.className = 'sd-label';
                lbl.textContent = seg.label;
                field.appendChild(lbl);
                var ctrl = makeSegmented(VIS_OPTIONS, s[seg.key], null);
                segCtrls[seg.key] = ctrl;
                field.appendChild(ctrl.el);
                body.appendChild(field);
            });

            var hint = document.createElement('div');
            hint.className = 'sd-hint';
            hint.style.padding = '12px 20px 0';
            hint.textContent = 'На данный момент «Друзья» трактуется как «Никто» — система отношений в разработке.';
            body.appendChild(hint);

            var btnWrap = document.createElement('div');
            btnWrap.style.padding = '16px 20px 20px';
            var saveBtn = makeSaveBtn();
            btnWrap.appendChild(saveBtn);
            body.appendChild(btnWrap);

            saveBtn.addEventListener('click', function () {
                saveBtn.disabled = true;
                BF.api.updatePrivacySettings({
                    profileVisibleOnSite: profileToggle.getValue(),
                    searchVisible: searchToggle.getValue(),
                    avatarVisibility: segCtrls.avatarVisibility.getValue(),
                    bioVisibility: segCtrls.bioVisibility.getValue(),
                    emailVisibility: segCtrls.emailVisibility.getValue(),
                    onlineVisibility: segCtrls.onlineVisibility.getValue()
                }).then(function () {
                    saveBtn.disabled = false;
                    var ok = document.createElement('div');
                    ok.className = 'sd-hint';
                    ok.style.padding = '6px 20px 0';
                    ok.textContent = 'Сохранено';
                    btnWrap.appendChild(ok);
                    setTimeout(function () { if (ok.parentNode) ok.parentNode.removeChild(ok); }, 2000);
                }).catch(function () {
                    saveBtn.disabled = false;
                    var err = document.createElement('div');
                    err.className = 'sd-hint error';
                    err.style.padding = '6px 20px 0';
                    err.textContent = 'Ошибка сохранения';
                    btnWrap.appendChild(err);
                    setTimeout(function () { if (err.parentNode) err.parentNode.removeChild(err); }, 3000);
                });
            });
        }).catch(function () {
            body.innerHTML = '<div class="sd-hint error" style="padding:20px">Ошибка загрузки</div>';
        });
    }

    // --- Personalization (poster + chat backgrounds) ---
    function renderPersonalization() {
        titleEl.textContent = 'Персонализация';
        body.innerHTML = '<div class="sd-hint" style="padding:20px">Загрузка…</div>';

        var p = Promise.all([loadCurrentUser(), BF.api.getPersonalization()]);
        p.then(function (results) {
            var user = results[0] || {};
            var data = results[1] || {};
            var pers = (data && data.personalization) || { profilePosterFileId: '', chatBackgroundFileIds: [] };
            // Server is source of truth for poster
            if (user && pers.profilePosterFileId !== undefined) {
                user.profilePosterFileId = pers.profilePosterFileId || '';
            }
            body.innerHTML = '';
            renderPersonalizationContent(user, pers);
        }).catch(function () {
            body.innerHTML = '<div class="sd-hint error" style="padding:20px">Ошибка загрузки</div>';
        });
    }

    function renderPersonalizationContent(user, pers) {
        // ===== Profile preview =====
        var preview = document.createElement('div');
        preview.className = 'sd-profile-preview';

        var poster = document.createElement('div');
        poster.className = 'sd-profile-preview-poster';
        preview.appendChild(poster);

        var pbody = document.createElement('div');
        pbody.className = 'sd-profile-preview-body';

        var pav = document.createElement('div');
        pav.className = 'sd-profile-preview-avatar';
        if (user && user.profilePicture) {
            var img = document.createElement('img'); img.src = user.profilePicture; img.alt = '';
            pav.appendChild(img);
        } else {
            pav.textContent = ((user && (user.firstName || user.username)) || '?')[0].toUpperCase();
        }
        pbody.appendChild(pav);

        var pname = document.createElement('div');
        pname.className = 'sd-profile-preview-name';
        pname.textContent = (user && ([user.firstName, user.lastName].filter(Boolean).join(' ') || user.username)) || '';
        pbody.appendChild(pname);

        var punm = document.createElement('div');
        punm.className = 'sd-profile-preview-username';
        punm.textContent = user && user.username ? '@' + user.username : '';
        pbody.appendChild(punm);

        preview.appendChild(pbody);

        // Poster actions
        var actions = document.createElement('div');
        actions.className = 'sd-poster-actions';
        var setBtn = document.createElement('button');
        setBtn.className = 'sd-btn sd-btn-primary';
        setBtn.textContent = 'Установить новый постер';
        var rmBtn = document.createElement('button');
        rmBtn.className = 'sd-btn-danger';
        rmBtn.textContent = 'Удалить';
        actions.appendChild(setBtn);
        actions.appendChild(rmBtn);

        var posterStatus = document.createElement('div');
        posterStatus.className = 'sd-hint';
        posterStatus.style.padding = '6px 20px 0';

        body.appendChild(preview);
        body.appendChild(actions);
        body.appendChild(posterStatus);

        function paintPoster(fileId) {
            if (fileId) {
                BF.files.getFileUrls([fileId]).then(function (urls) {
                    var u = urls && urls[0];
                    var url = u && (u.url || u.previewUrl);
                    if (url) {
                        poster.classList.add('has-image');
                        poster.style.setProperty('--preview-poster-image', 'url("' + url + '")');
                    }
                });
                rmBtn.style.display = '';
            } else {
                poster.classList.remove('has-image');
                poster.style.removeProperty('--preview-poster-image');
                rmBtn.style.display = 'none';
            }
        }
        paintPoster(pers.profilePosterFileId || '');

        setBtn.addEventListener('click', function () {
            openPosterFilePicker(function (blob) {
                if (!blob) return;
                posterStatus.textContent = 'Загрузка…';
                posterStatus.className = 'sd-hint';
                posterStatus.style.padding = '6px 20px 0';
                BF.files.uploadFile(blob, FT_USER_PROFILE_POSTER).then(function (fileId) {
                    return BF.api.setProfilePoster(fileId).then(function () {
                        pers.profilePosterFileId = fileId;
                        paintPoster(fileId);
                        if (currentUser) currentUser.profilePosterFileId = fileId;
                        posterStatus.textContent = 'Постер обновлён';
                        setTimeout(function () { posterStatus.textContent = ''; }, 2000);
                    });
                }).catch(function () {
                    posterStatus.textContent = 'Ошибка загрузки';
                    posterStatus.className = 'sd-hint error';
                    posterStatus.style.padding = '6px 20px 0';
                });
            });
        });

        rmBtn.addEventListener('click', function () {
            rmBtn.disabled = true;
            BF.api.setProfilePoster('').then(function () {
                pers.profilePosterFileId = '';
                if (currentUser) currentUser.profilePosterFileId = '';
                paintPoster('');
                rmBtn.disabled = false;
            }).catch(function () { rmBtn.disabled = false; });
        });

        // ===== Appearance section =====
        var apHead = document.createElement('div');
        apHead.className = 'sd-section-heading';
        apHead.textContent = 'Внешний вид сообщений';
        body.appendChild(apHead);

        // Chat preview
        var chatPreview = document.createElement('div');
        chatPreview.className = 'sd-chat-preview';
        var bgEl = document.createElement('div');
        bgEl.className = 'sd-chat-preview-bg';
        chatPreview.appendChild(bgEl);
        var dimEl = document.createElement('div');
        dimEl.className = 'sd-chat-preview-dim';
        chatPreview.appendChild(dimEl);

        var msgs = document.createElement('div');
        msgs.className = 'sd-chat-preview-msgs';
        var mockMsgs = [
            { side: 'incoming', text: 'Привет! Как настроение?' },
            { side: 'outgoing', text: 'Отлично! Только что закончил работу' },
            { side: 'incoming', text: 'Здорово, может встретимся вечером?' },
            { side: 'outgoing', text: 'Конечно, в 19:00 у кафе?' },
            { side: 'incoming', text: 'Договорились! 👍' }
        ];
        mockMsgs.forEach(function (m) {
            var b = document.createElement('div');
            b.className = 'sd-preview-msg ' + m.side;
            b.textContent = m.text;
            msgs.appendChild(b);
        });
        chatPreview.appendChild(msgs);
        body.appendChild(chatPreview);

        // Slider helpers
        function buildSlider(label, min, max, step, initial, unit, onChange) {
            var row = document.createElement('div');
            row.className = 'sd-slider-row';
            var head = document.createElement('div');
            head.className = 'sd-slider-row-header';
            var l = document.createElement('span');
            l.textContent = label;
            var v = document.createElement('span');
            v.className = 'sd-slider-value';
            v.textContent = initial + (unit || '');
            head.appendChild(l);
            head.appendChild(v);
            row.appendChild(head);
            var sl = document.createElement('input');
            sl.type = 'range';
            sl.className = 'sd-slider';
            sl.min = String(min); sl.max = String(max); sl.step = String(step || 1);
            sl.value = String(initial);
            sl.addEventListener('input', function () {
                var n = parseInt(sl.value, 10);
                v.textContent = n + (unit || '');
                onChange(n);
            });
            row.appendChild(sl);
            return { row: row, slider: sl, valueEl: v };
        }

        function applyPreview() {
            var r = BF.personalization.getRadius();
            var blurOn = BF.personalization.getBlurEnabled();
            var blurR = BF.personalization.getBlurRadius();
            var dim = BF.personalization.getDim();
            chatPreview.style.setProperty('--preview-radius', r + 'px');
            chatPreview.style.setProperty('--preview-bg-blur', (blurOn ? blurR : 0) + 'px');
            chatPreview.style.setProperty('--preview-bg-dim', (dim / 100).toFixed(3));
            var bgUrl = BF.personalization.getResolvedBackgroundUrl();
            chatPreview.style.setProperty('--preview-bg-image', bgUrl ? ('url("' + bgUrl + '")') : 'none');
        }

        var radiusCtl = buildSlider('Закругление пузырей', 0, 20, 1,
            BF.personalization.getRadius(), 'px',
            function (n) { BF.personalization.setRadius(n); applyPreview(); });
        body.appendChild(radiusCtl.row);

        // Blur toggle + slider
        var blurInitial = BF.personalization.getBlurEnabled();
        var blurToggleSec = document.createElement('div');
        blurToggleSec.className = 'sd-section';
        blurToggleSec.style.marginTop = '8px';
        var blurToggle = makeToggleRow(
            'Размытие фона',
            'Применяется к выбранному изображению фона',
            blurInitial,
            function (next) {
                BF.personalization.setBlurEnabled(next);
                blurSliderCtl.slider.disabled = !next;
                applyPreview();
            }
        );
        blurToggleSec.appendChild(blurToggle.row);
        body.appendChild(blurToggleSec);

        var blurSliderCtl = buildSlider('Радиус размытия', 1, 25, 1,
            BF.personalization.getBlurRadius(), '',
            function (n) { BF.personalization.setBlurRadius(n); applyPreview(); });
        blurSliderCtl.slider.disabled = !blurInitial;
        body.appendChild(blurSliderCtl.row);

        var dimCtl = buildSlider('Затенение фона', 0, 100, 1,
            BF.personalization.getDim(), '%',
            function (n) { BF.personalization.setDim(n); applyPreview(); });
        body.appendChild(dimCtl.row);

        // ===== Backgrounds section =====
        var bgHead = document.createElement('div');
        bgHead.className = 'sd-section-heading';
        bgHead.textContent = 'Фон чата';
        body.appendChild(bgHead);

        var bgWrap = document.createElement('div');
        bgWrap.style.padding = '8px 20px 24px';
        var grid = document.createElement('div');
        grid.className = 'sd-bg-grid';
        bgWrap.appendChild(grid);

        var bgStatus = document.createElement('div');
        bgStatus.className = 'sd-hint';
        bgStatus.style.marginTop = '8px';
        bgWrap.appendChild(bgStatus);

        var bgInput = document.createElement('input');
        bgInput.type = 'file';
        bgInput.accept = 'image/*';
        bgInput.style.display = 'none';
        bgWrap.appendChild(bgInput);
        body.appendChild(bgWrap);

        function rerenderGrid() {
            grid.innerHTML = '';
            var ids = pers.chatBackgroundFileIds || [];
            var activeId = BF.personalization.getBackgroundFileId();

            // "None" tile
            var none = document.createElement('div');
            none.className = 'sd-bg-card none-card' + (!activeId ? ' active' : '');
            none.textContent = 'Без фона';
            none.addEventListener('click', function () {
                BF.personalization.setBackgroundFileId('').then(applyPreview);
                rerenderGrid();
            });
            grid.appendChild(none);

            ids.forEach(function (fid) {
                var card = document.createElement('div');
                card.className = 'sd-bg-card' + (fid === activeId ? ' active' : '');
                var im = document.createElement('img');
                im.alt = '';
                card.appendChild(im);
                BF.files.getFileUrls([fid]).then(function (urls) {
                    var u = urls && urls[0];
                    if (u) im.src = u.previewUrl || u.url;
                });
                card.addEventListener('click', function (e) {
                    if (e.target.classList.contains('sd-bg-card-remove')) return;
                    BF.personalization.setBackgroundFileId(fid).then(applyPreview);
                    rerenderGrid();
                });

                var rm = document.createElement('button');
                rm.type = 'button';
                rm.className = 'sd-bg-card-remove';
                rm.textContent = '×';
                rm.title = 'Удалить из коллекции';
                rm.addEventListener('click', function (e) {
                    e.stopPropagation();
                    rm.disabled = true;
                    var nextIds = ids.filter(function (x) { return x !== fid; });
                    BF.api.updatePersonalization({
                        profilePosterFileId: pers.profilePosterFileId || '',
                        chatBackgroundFileIds: nextIds
                    }).then(function () {
                        pers.chatBackgroundFileIds = nextIds;
                        if (BF.personalization.getBackgroundFileId() === fid) {
                            BF.personalization.setBackgroundFileId('').then(applyPreview);
                        }
                        rerenderGrid();
                    }).catch(function () { rm.disabled = false; });
                });
                card.appendChild(rm);
                grid.appendChild(card);
            });

            var add = document.createElement('div');
            add.className = 'sd-bg-card sd-bg-card-add';
            add.textContent = '+';
            add.title = 'Добавить новый фон';
            add.addEventListener('click', function () { bgInput.click(); });
            grid.appendChild(add);
        }

        bgInput.addEventListener('change', function () {
            var f = bgInput.files[0];
            if (!f) return;
            bgStatus.textContent = 'Загрузка…';
            bgStatus.className = 'sd-hint';
            BF.files.uploadFile(f, FT_MESSAGE_ATTACHMENT_IMAGE).then(function (fileId) {
                var nextIds = (pers.chatBackgroundFileIds || []).concat([fileId]);
                return BF.api.updatePersonalization({
                    profilePosterFileId: pers.profilePosterFileId || '',
                    chatBackgroundFileIds: nextIds
                }).then(function () {
                    pers.chatBackgroundFileIds = nextIds;
                    // Auto-select newly added bg
                    BF.personalization.setBackgroundFileId(fileId).then(applyPreview);
                    rerenderGrid();
                    bgStatus.textContent = 'Фон добавлен';
                    setTimeout(function () { bgStatus.textContent = ''; }, 2000);
                });
            }).catch(function () {
                bgStatus.textContent = 'Ошибка загрузки';
                bgStatus.className = 'sd-hint error';
            });
            bgInput.value = '';
        });

        rerenderGrid();
        applyPreview();
    }

    // ===== Poster crop modal =====
    var cropState = null;
    function openPosterFilePicker(onResult) {
        var inp = document.createElement('input');
        inp.type = 'file';
        inp.accept = 'image/*';
        inp.style.display = 'none';
        inp.addEventListener('change', function () {
            var f = inp.files[0];
            if (!f) return;
            openPosterCrop(f, onResult);
        });
        document.body.appendChild(inp);
        inp.click();
        setTimeout(function () { if (inp.parentNode) inp.parentNode.removeChild(inp); }, 100);
    }

    function openPosterCrop(file, onResult) {
        var overlay = document.getElementById('posterCropOverlay');
        var stage = document.getElementById('posterCropStage');
        var imgEl = document.getElementById('posterCropImage');
        var frame = document.getElementById('posterCropFrame');
        var btnCancel = document.getElementById('posterCropCancel');
        var btnSave = document.getElementById('posterCropSave');
        if (!overlay || !stage || !imgEl || !frame) return;

        var url = URL.createObjectURL(file);
        imgEl.onload = function () {
            overlay.classList.add('visible');
            initCrop();
        };
        imgEl.src = url;

        function cleanup() {
            overlay.classList.remove('visible');
            URL.revokeObjectURL(url);
            imgEl.src = '';
            btnCancel.onclick = null;
            btnSave.onclick = null;
            if (cropState && cropState.detach) cropState.detach();
            cropState = null;
        }

        function initCrop() {
            // Compute image rect inside stage (object-fit: contain by max-width/max-height)
            var imgRect = imgEl.getBoundingClientRect();
            var stageRect = stage.getBoundingClientRect();
            var ix = imgRect.left - stageRect.left;
            var iy = imgRect.top - stageRect.top;
            var iw = imgRect.width;
            var ih = imgRect.height;

            // Init frame: max width that fits aspect 3:1 inside image area
            var fw = iw;
            var fh = fw / 3;
            if (fh > ih) { fh = ih; fw = fh * 3; }
            var fx = ix + (iw - fw) / 2;
            var fy = iy + (ih - fh) / 2;

            frame.style.left = fx + 'px';
            frame.style.top = fy + 'px';
            frame.style.width = fw + 'px';
            frame.style.height = fh + 'px';

            cropState = {
                bounds: { x: ix, y: iy, w: iw, h: ih },
                naturalW: imgEl.naturalWidth,
                naturalH: imgEl.naturalHeight,
                detach: bindCropDrag(frame, stage)
            };
        }

        btnCancel.onclick = function () { cleanup(); onResult(null); };
        btnSave.onclick = function () {
            if (!cropState) { cleanup(); return; }
            var b = cropState.bounds;
            var fx = parseFloat(frame.style.left) - b.x;
            var fy = parseFloat(frame.style.top) - b.y;
            var fw = parseFloat(frame.style.width);
            var fh = parseFloat(frame.style.height);
            var scale = cropState.naturalW / b.w;
            var sx = Math.max(0, fx * scale);
            var sy = Math.max(0, fy * scale);
            var sw = Math.min(cropState.naturalW - sx, fw * scale);
            var sh = Math.min(cropState.naturalH - sy, fh * scale);

            // Cap to 2400x800 (3:1) like Android
            var outW = Math.min(2400, sw);
            var outH = outW / 3;
            var canvas = document.createElement('canvas');
            canvas.width = Math.round(outW);
            canvas.height = Math.round(outH);
            var ctx = canvas.getContext('2d');
            ctx.drawImage(imgEl, sx, sy, sw, sh, 0, 0, canvas.width, canvas.height);
            canvas.toBlob(function (blob) {
                cleanup();
                if (blob) {
                    var jpeg = new File([blob], 'poster.jpg', { type: 'image/jpeg' });
                    onResult(jpeg);
                } else {
                    onResult(null);
                }
            }, 'image/jpeg', 0.9);
        };
    }

    function bindCropDrag(frame, stage) {
        var dragging = null;

        function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }

        function onDown(e) {
            var target = e.target;
            var handle = target.classList && target.classList.contains('sd-crop-handle') ? target.dataset.handle : null;
            if (target !== frame && !handle) return;
            e.preventDefault();
            var pt = pointFromEvent(e);
            var rect = stage.getBoundingClientRect();
            var b = cropState.bounds;
            dragging = {
                mode: handle ? 'resize-' + handle : 'move',
                startX: pt.clientX,
                startY: pt.clientY,
                origLeft: parseFloat(frame.style.left),
                origTop: parseFloat(frame.style.top),
                origW: parseFloat(frame.style.width),
                origH: parseFloat(frame.style.height),
                stage: rect,
                bounds: b
            };
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
            document.addEventListener('touchmove', onMove, { passive: false });
            document.addEventListener('touchend', onUp);
        }

        function pointFromEvent(e) {
            if (e.touches && e.touches[0]) return { clientX: e.touches[0].clientX, clientY: e.touches[0].clientY };
            return { clientX: e.clientX, clientY: e.clientY };
        }

        function onMove(e) {
            if (!dragging) return;
            e.preventDefault();
            var pt = pointFromEvent(e);
            var dx = pt.clientX - dragging.startX;
            var dy = pt.clientY - dragging.startY;
            var b = dragging.bounds;
            var minSize = 60;

            if (dragging.mode === 'move') {
                var nl = clamp(dragging.origLeft + dx, b.x, b.x + b.w - dragging.origW);
                var nt = clamp(dragging.origTop + dy, b.y, b.y + b.h - dragging.origH);
                frame.style.left = nl + 'px';
                frame.style.top = nt + 'px';
                return;
            }

            // Resize maintaining 3:1 aspect
            var nl2 = dragging.origLeft, nt2 = dragging.origTop;
            var nw = dragging.origW, nh = dragging.origH;
            var m = dragging.mode;

            if (m === 'resize-br') {
                nw = clamp(dragging.origW + dx, minSize, b.x + b.w - dragging.origLeft);
                nh = nw / 3;
                if (nt2 + nh > b.y + b.h) { nh = b.y + b.h - nt2; nw = nh * 3; }
            } else if (m === 'resize-bl') {
                nw = clamp(dragging.origW - dx, minSize, dragging.origLeft + dragging.origW - b.x);
                nh = nw / 3;
                nl2 = dragging.origLeft + dragging.origW - nw;
                if (nt2 + nh > b.y + b.h) { nh = b.y + b.h - nt2; nw = nh * 3; nl2 = dragging.origLeft + dragging.origW - nw; }
            } else if (m === 'resize-tr') {
                nw = clamp(dragging.origW + dx, minSize, b.x + b.w - dragging.origLeft);
                nh = nw / 3;
                nt2 = dragging.origTop + dragging.origH - nh;
                if (nt2 < b.y) { nt2 = b.y; nh = dragging.origTop + dragging.origH - nt2; nw = nh * 3; }
            } else if (m === 'resize-tl') {
                nw = clamp(dragging.origW - dx, minSize, dragging.origLeft + dragging.origW - b.x);
                nh = nw / 3;
                nl2 = dragging.origLeft + dragging.origW - nw;
                nt2 = dragging.origTop + dragging.origH - nh;
                if (nt2 < b.y) { nt2 = b.y; nh = dragging.origTop + dragging.origH - nt2; nw = nh * 3; nl2 = dragging.origLeft + dragging.origW - nw; }
            }

            frame.style.left = nl2 + 'px';
            frame.style.top = nt2 + 'px';
            frame.style.width = nw + 'px';
            frame.style.height = nh + 'px';
        }

        function onUp() {
            dragging = null;
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            document.removeEventListener('touchmove', onMove);
            document.removeEventListener('touchend', onUp);
        }

        stage.addEventListener('mousedown', onDown);
        stage.addEventListener('touchstart', onDown, { passive: false });
        return function () {
            stage.removeEventListener('mousedown', onDown);
            stage.removeEventListener('touchstart', onDown);
        };
    }

    // --- About ---
    function renderAbout() {
        titleEl.textContent = 'О BarkFluff';
        body.innerHTML = '';

        var dev = window.BF && BF.device ? BF.device : null;
        var rows = [
            { label: 'Версия веб-клиента', value: WEB_VERSION },
            { label: 'Браузер', value: dev ? dev.browserName : '—' },
            { label: 'ОС', value: dev ? dev.osName : '—' },
            { label: 'Device ID', value: dev ? dev.getDeviceId() : '—' },
            { label: 'Сервер', value: window.location.origin }
        ];

        rows.forEach(function (r) {
            var row = document.createElement('div');
            row.className = 'sd-about-row';
            var l = document.createElement('div');
            l.className = 'sd-about-label';
            l.textContent = r.label;
            var v = document.createElement('div');
            v.className = 'sd-about-value';
            v.textContent = r.value || '—';
            row.appendChild(l);
            row.appendChild(v);
            body.appendChild(row);
        });

        var link = document.createElement('a');
        link.href = 'https://barkfluff.com';
        link.target = '_blank';
        link.rel = 'noopener';
        link.textContent = 'barkfluff.com';
        link.style.cssText = 'display:block;text-align:center;padding:18px;color:var(--primary);font-size:14px;font-weight:600;text-decoration:none;';
        body.appendChild(link);
    }

    window.BF.settings = {
        init: init,
        open: open,
        close: close
    };
})();
