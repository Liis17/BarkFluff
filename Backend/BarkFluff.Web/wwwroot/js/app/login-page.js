/**
 * Login page bootstrap (index.html).
 * Requires: BF.auth, BF.tokens, BF.legal
 * Wires up login form, OTP flow, temp-login checkbox, legal consent gate.
 */
(function () {
    'use strict';

    var $ = function (sel) { return document.querySelector(sel); };

    var loginForm = $('#loginForm');
    var loginInput = $('#loginInput');
    var passwordInput = $('#passwordInput');
    var loginError = $('#loginError');
    var passwordError = $('#passwordError');
    var signInBtn = $('#signInBtn');
    var tempLoginCheck = $('#tempLoginCheck');

    var toRegisterBtn = $('#toRegisterBtn');
    var legalCheck = $('#legalAcceptCheck');
    var legalRow = $('#legalConsentRow');
    var fastAuthCard = $('#fastAuthCard');
    var nodeBar = $('#nodeBar');
    var nodeBarName = $('#nodeBarName');
    var nodeChangeBtn = $('#nodeChangeBtn');

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
        configureAuthAvailability();
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
        BF.authUI.cancelAll();
        if (BF.register) BF.register.close();
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

    var modeInput = $('#loginMode');
    var factorInput = $('#loginFactor');
    var recoveryInput = $('#loginRecovery');
    var capabilityVersion = 0;
    function configureAuthAvailability() {
        var version = ++capabilityVersion;
        var origin = BF.node.origin();
        var telegramMode = modeInput.querySelector('option[value="2"]');
        var emailFactor = factorInput.querySelector('option[value="2"]');
        var telegramFactor = factorInput.querySelector('option[value="3"]');
        telegramMode.disabled = true;
        emailFactor.disabled = true;
        telegramFactor.disabled = true;
        BF.confirmations.call('getAuthCapabilities', 'GetAuthCapabilitiesRequest', {}, { read: true, origin: origin })
            .then(function (capabilities) {
                if (version !== capabilityVersion || BF.node.origin() !== origin) return;
                telegramMode.disabled = !capabilities.getTelegramAvailable();
                emailFactor.disabled = !capabilities.getEmailAvailable();
                telegramFactor.disabled = !capabilities.getTelegramAvailable();
                if (modeInput.selectedOptions[0].disabled) modeInput.value = '1';
                if (factorInput.selectedOptions[0].disabled) factorInput.value = '0';
                updateMode();
            }).catch(function () {
                // A capability timeout must never present Telegram or email as available.
            });
    }
    function updateMode() {
        passwordInput.closest('.form-group').hidden = modeInput.value === '2';
        factorInput.closest('.form-group').hidden = modeInput.value !== '3';
        recoveryInput.closest('.checkbox-group').hidden = modeInput.value === '1';
    }
    modeInput.addEventListener('change', updateMode);
    updateMode();
    loginForm.addEventListener('submit', async function (event) {
        event.preventDefault(); clearErrors();
        if (!legalCheck.checked) { legalRow.classList.add('nudge'); return; }
        var login = loginInput.value.trim(), password = passwordInput.value;
        var mode = Number(modeInput.value), origin = BF.node.origin();
        if (!login) { showError(loginError, loginInput, BF.i18n.t('auth.error.noLogin')); return; }
        if (mode !== 2 && !password) { showError(passwordError, passwordInput, BF.i18n.t('auth.error.noPassword')); return; }
        setLoading(signInBtn, true);
        if (BF.fastAuth) BF.fastAuth.cancel();
        try {
            var result = await BF.authUI.run('beginSignIn', 'BeginSignInRequest', {
                login: login, password: password, loginMode: mode, factor: Number(factorInput.value),
                useRecoveryCode: mode !== 1 && recoveryInput.checked
            }, { switchFactor: mode === 3, telegramLogin: mode === 2 });
            if (BF.node.origin() !== origin || !result.getSession()) return;
            passwordInput.value = '';
            BF.tokens.setTempMode(tempLoginCheck.checked);
            BF.tokens.save(BF.confirmations.session(result.getSession()));
            await BF.legal.flushConsent();
            if (BF.node.origin() === origin) window.location.href = '/messenger';
        } catch (error) {
            if (error.name !== 'AbortError') showError(passwordError, null, BF.authUI.errorText(error));
        } finally { setLoading(signInBtn, false); applyGate(); startFastAuth(); }
    });
    $('#recoverPassword').addEventListener('click', async function () {
        if (BF.fastAuth) BF.fastAuth.cancel();
        try { await BF.authUI.recover(loginInput.value.trim()); showError(passwordError, null, BF.i18n.t('security.passwordChanged')); }
        catch (error) { if (error.name !== 'AbortError') showError(passwordError, null, BF.authUI.errorText(error)); }
        finally { startFastAuth(); }
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
