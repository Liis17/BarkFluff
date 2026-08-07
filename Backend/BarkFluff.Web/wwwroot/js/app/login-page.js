/**
 * Login page bootstrap (index.html).
 * Requires: BF.auth, BF.tokens, BF.legal
 * Wires up login form, OTP flow, temp-login checkbox, legal consent gate.
 */
(function () {
    'use strict';

    var $ = function (sel) { return document.querySelector(sel); };

    var loginSection = $('#loginSection');
    var otpSection = $('#otpSection');
    var welcomeSection = $('#welcomeSection');

    var loginForm = $('#loginForm');
    var loginInput = $('#loginInput');
    var passwordInput = $('#passwordInput');
    var loginError = $('#loginError');
    var passwordError = $('#passwordError');
    var signInBtn = $('#signInBtn');
    var tempLoginCheck = $('#tempLoginCheck');

    var otpInputs = document.querySelectorAll('.otp-input');
    var otpError = $('#otpError');
    var otpSubmitBtn = $('#otpSubmitBtn');
    var otpBack = $('#otpBack');

    var toRegisterBtn = $('#toRegisterBtn');
    var legalCheck = $('#legalAcceptCheck');
    var legalRow = $('#legalConsentRow');
    var fastAuthCard = $('#fastAuthCard');
    var nodeBar = $('#nodeBar');
    var nodeBarName = $('#nodeBarName');
    var nodeChangeBtn = $('#nodeChangeBtn');

    var pendingLogin = '';
    var pendingPassword = '';

    function showSection(name) {
        loginSection.classList.toggle('hidden', name !== 'login');
        otpSection.classList.toggle('hidden', name !== 'otp');
        welcomeSection.classList.toggle('hidden', name !== 'welcome');

        // QR-блок имеет смысл только на главной login-секции — на OTP/welcome скрываем
        // и останавливаем стрим, чтобы не висел зря.
        if (fastAuthCard) fastAuthCard.style.display = (name === 'login') ? '' : 'none';
        if (BF.fastAuth) {
            if (name === 'login') startFastAuth();
            else BF.fastAuth.cancel();
        }
    }

    // --- Выбор ноды ---
    // На шелле входа без ноды не существует: пока адрес не выбран, форма и QR скрыты.

    function renderNodeBar() {
        if (BF.node.pinned()) { nodeBar.classList.add('hidden'); return; }
        var meta = BF.node.meta();
        nodeBarName.textContent = (meta && meta.name) || BF.node.origin() || '';
        nodeBar.classList.remove('hidden');
    }

    function openNodePicker() {
        BF.nodePicker.open({
            onSelected: function () {
                BF.nodePicker.close();
                renderNodeBar();
                // Токены лежат под неймспейсом ноды: у вернувшегося пользователя
                // сессия уже есть, и показывать ему форму входа не за чем.
                resumeOrShowLogin();
            }
        });
    }

    /** Живая сессия на выбранной ноде уводит сразу в мессенджер, иначе показываем вход. */
    function resumeOrShowLogin() {
        if (!BF.tokens.get()) { startFastAuth(); return; }

        document.body.style.visibility = 'hidden';
        BF.auth.getValidAccessToken().then(function (token) {
            if (token) {
                window.location.href = '/messenger';
            } else {
                document.body.style.visibility = '';
                startFastAuth();
            }
        });
    }

    function ensureNode() {
        if (BF.node.origin()) { renderNodeBar(); return true; }
        openNodePicker();
        return false;
    }

    BF.nodePicker.init();

    nodeChangeBtn.addEventListener('click', function () {
        // Токены остаются под неймспейсом прежней ноды — вернувшись, вход не потребуется.
        if (BF.fastAuth) BF.fastAuth.cancel();
        BF.node.clear();
        openNodePicker();
    });

    // --- Согласие с документами ---

    /**
     * QR — такой же полноценный вход, как форма, поэтому сессия не запрашивается,
     * пока документы не приняты: иначе согласие обходится в один клик.
     */
    function startFastAuth() {
        if (BF.fastAuth && legalCheck.checked && BF.node.origin()) BF.fastAuth.start();
    }

    function applyGate() {
        var ok = legalCheck.checked;
        signInBtn.disabled = !ok;
        toRegisterBtn.disabled = !ok;
        fastAuthCard.classList.toggle('gated', !ok);
        if (ok) {
            legalRow.classList.remove('nudge');
        } else {
            $('#fastAuthStatus').textContent = BF.i18n.t('qr.legalRequired');
        }
    }

    legalCheck.addEventListener('change', function () {
        if (legalCheck.checked) BF.legal.accept();
        applyGate();
        if (legalCheck.checked) startFastAuth();
        else if (BF.fastAuth) BF.fastAuth.cancel();
    });

    function clearErrors() {
        loginError.classList.remove('visible');
        passwordError.classList.remove('visible');
        otpError.classList.remove('visible');
        loginInput.classList.remove('error');
        passwordInput.classList.remove('error');
    }

    function showError(el, inputEl, msg) {
        el.textContent = msg;
        el.classList.add('visible');
        if (inputEl) inputEl.classList.add('error');
        BF.sound.play('droplet');
    }

    function setLoading(btn, loading) {
        btn.classList.toggle('loading', loading);
        btn.disabled = loading;
    }

    // --- Login form ---
    loginForm.addEventListener('submit', function (e) {
        e.preventDefault();
        clearErrors();

        if (!legalCheck.checked) {
            legalRow.classList.add('nudge');
            BF.sound.play('droplet');
            return;
        }

        var login = loginInput.value.trim();
        var password = passwordInput.value;

        if (!login) { showError(loginError, loginInput, BF.i18n.t('auth.error.noLogin')); return; }
        if (!password) { showError(passwordError, passwordInput, BF.i18n.t('auth.error.noPassword')); return; }

        pendingLogin = login;
        pendingPassword = password;

        setLoading(signInBtn, true);

        BF.auth.login({ login: login, password: password }).then(function (result) {
            if (result.needOtp) {
                showSection('otp');
                otpInputs[0].focus();
                return;
            }
            if (result.error === 'invalid_credentials') {
                showError(passwordError, passwordInput, BF.i18n.t('auth.error.badCredentials'));
                return;
            }
            if (result.error === 'invalid_otp') {
                showError(passwordError, null, BF.i18n.t('auth.error.badCode'));
                return;
            }
            if (result.error) {
                showError(passwordError, null, BF.i18n.t('auth.error.server'));
                return;
            }

            BF.tokens.setTempMode(tempLoginCheck.checked);
            BF.tokens.save(result.data);
            BF.legal.flushConsent().then(function () {
                window.location.href = '/messenger';
            });
        }).catch(function () {
            showError(passwordError, null, BF.i18n.t('auth.error.network'));
        }).then(function () {
            setLoading(signInBtn, false);
        });
    });

    // --- OTP inputs ---
    otpInputs.forEach(function (input, index) {
        input.addEventListener('input', function (e) {
            var value = e.target.value.replace(/[^0-9]/g, '');
            e.target.value = value;
            if (value) BF.sound.play('tick');
            if (value && index < otpInputs.length - 1) {
                otpInputs[index + 1].focus();
            }
        });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Backspace' && !e.target.value && index > 0) {
                otpInputs[index - 1].focus();
            }
        });
        input.addEventListener('paste', function (e) {
            e.preventDefault();
            var paste = (e.clipboardData || window.clipboardData).getData('text').replace(/[^0-9]/g, '');
            otpInputs.forEach(function (inp, i) {
                if (i < paste.length) inp.value = paste[i];
            });
            var nextIndex = Math.min(paste.length, otpInputs.length - 1);
            otpInputs[nextIndex].focus();
        });
    });

    otpSubmitBtn.addEventListener('click', function () {
        clearErrors();
        var code = Array.from(otpInputs).map(function (i) { return i.value; }).join('');

        if (code.length !== 6) {
            otpError.textContent = BF.i18n.t('auth.error.incompleteCode');
            otpError.classList.add('visible');
            BF.sound.play('droplet');
            return;
        }

        setLoading(otpSubmitBtn, true);

        BF.auth.login({ login: pendingLogin, password: pendingPassword, otpCode: code }).then(function (result) {
            if (result.error === 'invalid_otp') {
                otpError.textContent = BF.i18n.t('auth.error.badCode');
                otpError.classList.add('visible');
                BF.sound.play('droplet');
                otpInputs.forEach(function (i) { i.value = ''; });
                otpInputs[0].focus();
                return;
            }
            if (result.error) {
                otpError.textContent = BF.i18n.t('auth.error.server');
                otpError.classList.add('visible');
                BF.sound.play('droplet');
                return;
            }

            BF.tokens.setTempMode(tempLoginCheck.checked);
            BF.tokens.save(result.data);
            BF.legal.flushConsent().then(function () {
                window.location.href = '/messenger';
            });
        }).catch(function () {
            otpError.textContent = BF.i18n.t('auth.error.network');
            otpError.classList.add('visible');
            BF.sound.play('droplet');
        }).then(function () {
            setLoading(otpSubmitBtn, false);
        });
    });

    otpBack.addEventListener('click', function () {
        clearErrors();
        otpInputs.forEach(function (i) { i.value = ''; });
        showSection('login');
    });

    // --- Check existing session on load ---
    // Гейт ставим до init(): у вернувшегося пользователя cookie уже есть, и форма не мигает
    // заблокированной. init() дочитывает редакцию документов и при её смене снимает галочку.
    legalCheck.checked = BF.legal.isAccepted();
    applyGate();

    // Словарь нужен до первых статусов QR, текстов ошибок и карточек нод
    BF.i18n.ready.then(function () {
        return BF.legal.init();
    }).then(function () {
        legalCheck.checked = BF.legal.isAccepted();
        applyGate();

        // Без ноды проверять сессию не у кого — токены хранятся по нодам.
        if (!ensureNode()) return;

        resumeOrShowLogin();
    });
})();
