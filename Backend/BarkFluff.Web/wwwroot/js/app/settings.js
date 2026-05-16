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
            case 'notifications':   renderNotifications(); break;
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

        // Section: Privacy & notifications
        var secPrivacy = makeSection('Конфиденциальность', [
            { icon: '🛡️', label: 'Приватность', view: 'privacy' },
            { icon: '🔔', label: 'Уведомления', view: 'notifications' }
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

    // --- Notifications ---
    function renderNotifications() {
        titleEl.textContent = 'Уведомления';
        body.innerHTML = '';

        var deviceId = BF.device ? BF.device.getDeviceId() : null;
        var storageKey = 'bf_notif_push_' + (deviceId || 'default');
        var stored = localStorage.getItem(storageKey);
        var initial = stored === null ? true : stored === '1';

        var sec = document.createElement('div');
        sec.className = 'sd-section';
        var toggle = makeToggleRow(
            'Push-уведомления',
            'Получать уведомления на это устройство (этот браузер)',
            initial,
            function (next, swEl) {
                swEl.disabled = true;
                BF.api.setNotificationsEnabled(next).then(function () {
                    localStorage.setItem(storageKey, next ? '1' : '0');
                    swEl.disabled = false;
                }).catch(function () {
                    toggle.setValue(!next);
                    swEl.disabled = false;
                });
            }
        );
        sec.appendChild(toggle.row);
        body.appendChild(sec);

        var hint = document.createElement('div');
        hint.className = 'sd-hint';
        hint.style.padding = '12px 20px';
        hint.textContent = 'Управляет уведомлениями только для текущего устройства. Чтобы изменить настройки для других устройств, войдите в аккаунт на них.';
        body.appendChild(hint);
    }

    // --- Personalization (poster + chat backgrounds) ---
    function renderPersonalization() {
        titleEl.textContent = 'Фон чата и постер';
        body.innerHTML = '<div class="sd-hint" style="padding:20px">Загрузка…</div>';

        BF.api.getPersonalization().then(function (data) {
            var pers = (data && data.personalization) || { profilePosterFileId: '', chatBackgroundFileIds: [] };
            body.innerHTML = '';
            renderPersonalizationContent(pers);
        }).catch(function () {
            body.innerHTML = '<div class="sd-hint error" style="padding:20px">Ошибка загрузки</div>';
        });
    }

    function renderPersonalizationContent(pers) {
        // --- Poster block ---
        var posterField = document.createElement('div');
        posterField.className = 'sd-field';
        posterField.style.padding = '16px 20px 0';

        var posterLbl = document.createElement('div');
        posterLbl.className = 'sd-label';
        posterLbl.textContent = 'Постер профиля';
        posterField.appendChild(posterLbl);

        var posterEl = document.createElement('div');
        posterEl.className = 'sd-poster-uploader';
        posterField.appendChild(posterEl);

        var posterStatus = document.createElement('div');
        posterStatus.className = 'sd-hint';
        posterStatus.style.marginTop = '6px';
        posterField.appendChild(posterStatus);

        var posterInput = document.createElement('input');
        posterInput.type = 'file';
        posterInput.accept = 'image/*';
        posterInput.style.display = 'none';
        posterField.appendChild(posterInput);

        body.appendChild(posterField);

        function paintPoster(fileId) {
            posterEl.innerHTML = '';
            if (fileId) {
                posterEl.classList.add('has-image');
                var img = document.createElement('img');
                img.alt = '';
                posterEl.appendChild(img);
                BF.files.getFileUrls([fileId]).then(function (urls) {
                    var u = urls && urls[0];
                    if (u) img.src = u.previewUrl || u.url;
                });
                var rm = document.createElement('button');
                rm.type = 'button';
                rm.className = 'sd-poster-remove';
                rm.textContent = '×';
                rm.title = 'Удалить постер';
                rm.addEventListener('click', function (e) {
                    e.stopPropagation();
                    rm.disabled = true;
                    BF.api.setProfilePoster('').then(function () {
                        pers.profilePosterFileId = '';
                        paintPoster('');
                    }).catch(function () { rm.disabled = false; });
                });
                posterEl.appendChild(rm);
            } else {
                posterEl.classList.remove('has-image');
                var ph = document.createElement('div');
                ph.className = 'sd-poster-placeholder';
                ph.textContent = 'Нажмите чтобы загрузить постер (соотношение 3:1)';
                posterEl.appendChild(ph);
            }
        }

        posterEl.addEventListener('click', function () { posterInput.click(); });
        posterInput.addEventListener('change', function () {
            var f = posterInput.files[0];
            if (!f) return;
            posterStatus.textContent = 'Загрузка…';
            posterStatus.className = 'sd-hint';
            BF.files.uploadFile(f, FT_USER_PROFILE_POSTER).then(function (fileId) {
                return BF.api.setProfilePoster(fileId).then(function () {
                    pers.profilePosterFileId = fileId;
                    paintPoster(fileId);
                    posterStatus.textContent = 'Постер обновлён';
                    setTimeout(function () { posterStatus.textContent = ''; }, 2000);
                });
            }).catch(function () {
                posterStatus.textContent = 'Ошибка загрузки';
                posterStatus.className = 'sd-hint error';
            });
            posterInput.value = '';
        });

        paintPoster(pers.profilePosterFileId);

        // --- Chat backgrounds block ---
        var bgField = document.createElement('div');
        bgField.className = 'sd-field';
        bgField.style.padding = '20px 20px 24px';

        var bgLbl = document.createElement('div');
        bgLbl.className = 'sd-label';
        bgLbl.textContent = 'Фоны чата';
        bgField.appendChild(bgLbl);

        var grid = document.createElement('div');
        grid.className = 'sd-bg-grid';
        bgField.appendChild(grid);

        var bgStatus = document.createElement('div');
        bgStatus.className = 'sd-hint';
        bgStatus.style.marginTop = '8px';
        bgField.appendChild(bgStatus);

        var bgInput = document.createElement('input');
        bgInput.type = 'file';
        bgInput.accept = 'image/*';
        bgInput.style.display = 'none';
        bgField.appendChild(bgInput);

        body.appendChild(bgField);

        function rerenderGrid() {
            grid.innerHTML = '';
            var ids = pers.chatBackgroundFileIds || [];
            ids.forEach(function (fid) {
                var card = document.createElement('div');
                card.className = 'sd-bg-card';
                var img = document.createElement('img');
                img.alt = '';
                card.appendChild(img);
                BF.files.getFileUrls([fid]).then(function (urls) {
                    var u = urls && urls[0];
                    if (u) img.src = u.previewUrl || u.url;
                });
                var rm = document.createElement('button');
                rm.type = 'button';
                rm.className = 'sd-bg-card-remove';
                rm.textContent = '×';
                rm.addEventListener('click', function () {
                    rm.disabled = true;
                    var nextIds = ids.filter(function (x) { return x !== fid; });
                    BF.api.updatePersonalization({
                        profilePosterFileId: pers.profilePosterFileId || '',
                        chatBackgroundFileIds: nextIds
                    }).then(function () {
                        pers.chatBackgroundFileIds = nextIds;
                        rerenderGrid();
                    }).catch(function () { rm.disabled = false; });
                });
                card.appendChild(rm);
                grid.appendChild(card);
            });

            var add = document.createElement('div');
            add.className = 'sd-bg-card sd-bg-card-add';
            add.textContent = '+';
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
