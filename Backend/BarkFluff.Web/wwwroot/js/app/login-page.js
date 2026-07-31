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

    // --- Согласие с документами ---

    /**
     * QR — такой же полноценный вход, как форма, поэтому сессия не запрашивается,
     * пока документы не приняты: иначе согласие обходится в один клик.
     */
    function startFastAuth() {
        if (BF.fastAuth && legalCheck.checked) BF.fastAuth.start();
    }

    function applyGate() {
        var ok = legalCheck.checked;
        signInBtn.disabled = !ok;
        toRegisterBtn.disabled = !ok;
        fastAuthCard.classList.toggle('gated', !ok);
        if (ok) {
            legalRow.classList.remove('nudge');
        } else {
            $('#fastAuthStatus').textContent = 'Примите документы, чтобы войти по QR';
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

        if (!login) { showError(loginError, loginInput, 'Введите логин или email'); return; }
        if (!password) { showError(passwordError, passwordInput, 'Введите пароль'); return; }

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
                showError(passwordError, passwordInput, 'Неверный логин или пароль');
                return;
            }
            if (result.error === 'invalid_otp') {
                showError(passwordError, null, 'Неверный код подтверждения');
                return;
            }
            if (result.error) {
                showError(passwordError, null, 'Ошибка сервера');
                return;
            }

            BF.tokens.setTempMode(tempLoginCheck.checked);
            BF.tokens.save(result.data);
            BF.legal.flushConsent().then(function () {
                window.location.href = '/messenger';
            });
        }).catch(function () {
            showError(passwordError, null, 'Не удалось подключиться к серверу');
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
            otpError.textContent = 'Введите все 6 цифр';
            otpError.classList.add('visible');
            BF.sound.play('droplet');
            return;
        }

        setLoading(otpSubmitBtn, true);

        BF.auth.login({ login: pendingLogin, password: pendingPassword, otpCode: code }).then(function (result) {
            if (result.error === 'invalid_otp') {
                otpError.textContent = 'Неверный код подтверждения';
                otpError.classList.add('visible');
                BF.sound.play('droplet');
                otpInputs.forEach(function (i) { i.value = ''; });
                otpInputs[0].focus();
                return;
            }
            if (result.error) {
                otpError.textContent = 'Ошибка сервера';
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
            otpError.textContent = 'Не удалось подключиться к серверу';
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

    BF.legal.init().then(function () {
        legalCheck.checked = BF.legal.isAccepted();
        applyGate();

        if (BF.tokens.get()) {
            document.body.style.visibility = 'hidden';
            BF.auth.getValidAccessToken().then(function (token) {
                if (token) {
                    window.location.href = '/messenger';
                } else {
                    document.body.style.visibility = '';
                    startFastAuth();
                }
            });
        } else {
            startFastAuth();
        }
    });
})();
