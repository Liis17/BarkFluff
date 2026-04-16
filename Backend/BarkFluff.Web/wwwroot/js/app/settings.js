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
            case 'main':      renderMain(); break;
            case 'profile':   renderProfile(); break;
            case 'name':      renderName(); break;
            case 'bio':       renderBio(); break;
            case 'password':  renderPassword(); break;
            case 'twofa':     renderTwoFA(); break;
            case 'sessions':  renderSessions(); break;
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

        // Section: Security
        var secSecurity = makeSection('Безопасность', [
            { icon: '🔒', label: 'Пароль', view: 'password' },
            { icon: '🛡️', label: 'Двухфакторная аутентификация', view: 'twofa' }
        ]);
        body.appendChild(secSecurity);

        // Section: Devices
        var secDevices = makeSection('Устройства', [
            { icon: '📱', label: 'Активные сессии', view: 'sessions' }
        ]);
        body.appendChild(secDevices);

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
            sessions.forEach(function (s) {
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
                } else {
                    var termBtn = document.createElement('button');
                    termBtn.className = 'session-terminate';
                    termBtn.textContent = 'Завершить';
                    termBtn.addEventListener('click', function () {
                        termBtn.disabled = true;
                        BF.api.removeActiveSession(s.deviceId).then(function () {
                            item.parentNode && item.parentNode.removeChild(item);
                        }).catch(function () { termBtn.disabled = false; });
                    });
                    item.appendChild(termBtn);
                }

                body.appendChild(item);
            });
        }).catch(function () {
            body.innerHTML = '<div class="sd-hint error" style="padding:20px">Ошибка загрузки</div>';
        });
    }

    window.BF.settings = {
        init: init,
        open: open,
        close: close
    };
})();
