/** Email-first sign-in flow for index.html. */
(function () {
    'use strict';

    var $ = function (selector) { return document.querySelector(selector); };
    var loginForm = $('#loginForm');
    var loginInput = $('#loginInput');
    var loginError = $('#loginError');
    var passwordGroup = $('#passwordGroup');
    var passwordInput = $('#passwordInput');
    var passwordError = $('#passwordError');
    var signInBtn = $('#signInBtn');
    var signInLabel = signInBtn.querySelector('.btn-label');
    var changeLoginBtn = $('#changeLoginBtn');
    var factorGroup = $('#factorGroup');
    var factorInput = $('#loginFactor');
    var recoveryGroup = $('#recoveryGroup');
    var recoveryInput = $('#loginRecovery');
    var challengeGroup = $('#challengeGroup');
    var challengeStatus = $('#challengeStatus');
    var codeGroup = $('#codeGroup');
    var codeInput = $('#confirmationCode');
    var codeLabel = $('#confirmationCodeLabel');
    var codeError = $('#confirmationError');
    var confirmCodeBtn = $('#confirmCodeBtn');
    var retryBtn = $('#challengeRetryBtn');
    var resendBtn = $('#challengeResendBtn');
    var tempLoginCheck = $('#tempLoginCheck');
    var toRegisterBtn = $('#toRegisterBtn');
    var legalCheck = $('#legalAcceptCheck');
    var legalRow = $('#legalConsentRow');
    var fastAuthCard = $('#fastAuthCard');
    var nodeBar = $('#nodeBar');
    var nodeBarName = $('#nodeBarName');
    var nodeChangeBtn = $('#nodeChangeBtn');

    var upgrade = $('#loginUpgrade');
    var upgradeMessage = $('#loginUpgradeMessage');
    var upgradeBtn = $('#loginUpgradeBtn');
    var upgradeRequired = !hasLoginContract();
    var runtimeReady = false;
    var runtimeController = window.navigator && window.navigator.serviceWorker && window.navigator.serviceWorker.controller;
    var cancelUpgrade = null;
    var reloading = false;

    // Old installed shells cache the proto and dictionaries independently of this page.
    function hasLoginContract() {
        var identity = window.proto && window.proto.barkfluff && window.proto.barkfluff.identity;
        var client = window.barkfluff && window.barkfluff.IdentityApiClient;
        return !!(client && typeof client.prototype.resolveSignIn === 'function' && identity &&
            typeof identity.ResolveSignInRequest === 'function' && identity.BeginSignInRequest &&
            typeof identity.BeginSignInRequest.prototype.setReportLoginModeMismatch === 'function');
    }

    function showUpgrade() {
        upgradeRequired = true;
        resetFlow(false);
        upgradeMessage.textContent = t('pwa.updateAvailable');
        upgradeBtn.textContent = t('common.apply');
    }

    function applyUpgrade() {
        if (cancelUpgrade || reloading) return;
        var sw = window.navigator && window.navigator.serviceWorker;
        var generation = flow.generation;
        var originalController = sw && sw.controller;
        var cleanups = [];
        var stopped = false;
        var sent = null;
        upgradeBtn.disabled = true;
        upgradeMessage.textContent = t('common.loadingShort');
        function listen(target, event, callback) {
            target.addEventListener(event, callback);
            cleanups.push(function () { target.removeEventListener(event, callback); });
        }
        function finish(failed) {
            if (stopped) return;
            stopped = true;
            cleanups.forEach(function (cleanup) { cleanup(); });
            cancelUpgrade = null;
            upgradeBtn.disabled = false;
            if (!failed && !reloading) upgradeMessage.textContent = t('pwa.updateAvailable');
            if (failed) {
                upgradeMessage.textContent = t('auth.error.network');
                upgradeBtn.textContent = t('connection.retry');
            }
        }
        cancelUpgrade = function () { finish(false); };
        var timeout = setTimeout(function () { finish(true); }, 15000);
        cleanups.push(function () { clearTimeout(timeout); });
        if (!sw || !originalController) { finish(true); return; }
        // Another tab or a timed-out update may already have activated the new shell.
        if (runtimeController && originalController !== runtimeController) {
            reloading = true;
            finish(false);
            window.location.reload();
            return;
        }
        listen(sw, 'controllerchange', function () {
            if (stopped || generation !== flow.generation || !sw.controller || sw.controller === originalController) return;
            reloading = true;
            finish(false);
            window.location.reload();
        });
        Promise.resolve().then(function () { return sw.getRegistration(); }).then(function (registration) {
            if (stopped) return;
            if (!registration) { finish(true); return; }
            function activateWaiting() {
                if (stopped || !registration.waiting || sent === registration.waiting) return;
                sent = registration.waiting;
                try { sent.postMessage({ type: 'SKIP_WAITING' }); }
                catch { finish(true); }
            }
            function watchInstalling() {
                var worker = registration.installing;
                if (worker) listen(worker, 'statechange', function () {
                    if (worker.state === 'redundant') finish(true);
                    else activateWaiting();
                });
                activateWaiting();
            }
            listen(registration, 'updatefound', watchInstalling);
            watchInstalling();
            if (!sent) return registration.update();
        }).catch(function () { finish(true); });
    }

    var flow = {
        generation: 0,
        operation: 0,
        pollOperation: 0,
        responseOperation: 0,
        origin: '',
        stage: 'identifier',
        mode: 0,
        login: '',
        password: '',
        factor: 0,
        recovery: false,
        needsCode: false,
        challengeState: 0,
        availableFactors: [],
        attempt: null,
        resolveController: null,
        busy: false,
        terminal: false,
        retryKind: '',
        retryCode: '',
        retryRecovery: false,
        submitRetry: false,
        resendAt: 0,
        resendTimer: null,
        pendingSession: null
    };
    var renderedFactors = null;
    var renderedFactor = null;
    var renderedFactorOperation = -1;

    function t(key) { return BF.i18n.t(key); }

    function renderNodeBar() {
        if (BF.node.pinned()) { nodeBar.classList.add('hidden'); return; }
        var meta = BF.node.meta();
        nodeBarName.textContent = (meta && meta.name) || BF.node.origin() || '';
        nodeBar.classList.remove('hidden');
    }

    function legalAccepted() {
        return legalCheck.checked && (!BF.legal.isAccepted || BF.legal.isAccepted());
    }

    function startFastAuth() {
        if (runtimeReady && !upgradeRequired && flow.stage === 'identifier' && !flow.busy && legalAccepted() && BF.node.origin() && BF.fastAuth)
            BF.fastAuth.start();
    }

    function ensureNode() {
        if (BF.node.origin()) { renderNodeBar(); return true; }
        BF.nodePicker.open({
            onSelected: function () {
                BF.nodePicker.close();
                renderNodeBar();
                resumeOrShowLogin();
            }
        });
        return false;
    }

    function resumeOrShowLogin() {
        if (!runtimeReady || upgradeRequired) return;
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

    function clearResendTimer() {
        if (flow.resendTimer) {
            clearInterval(flow.resendTimer);
            flow.resendTimer = null;
        }
    }

    function cancelAttempt() {
        flow.operation++;
        if (flow.attempt) {
            flow.attempt.cancel();
            flow.attempt = null;
        }
        clearResendTimer();
    }

    function restorePreviousSessionIfNeeded() {
        var saved = flow.pendingSession;
        flow.pendingSession = null;
        if (!saved || BF.node.origin() !== saved.origin || BF.tokens.getAccessToken() !== saved.accessToken) return;
        if (saved.previous) {
            BF.tokens.setTempMode(saved.previousTemp);
            BF.tokens.save(saved.previous);
        } else {
            BF.tokens.clear();
        }
    }

    function clearError(el, input) {
        el.textContent = '';
        el.classList.remove('visible');
        if (input) input.classList.remove('error');
    }

    function showError(el, input, message) {
        el.textContent = message || '';
        el.classList.toggle('visible', !!message);
        if (input) input.classList.toggle('error', !!message);
        if (message) BF.sound.play('droplet');
    }

    function clearErrors() {
        clearError(loginError, loginInput);
        clearError(passwordError, passwordInput);
        clearError(codeError, codeInput);
        challengeStatus.classList.remove('is-error');
    }

    function setChallengeMessage(message, isError) {
        challengeStatus.textContent = message || '';
        challengeStatus.classList.toggle('is-error', !!isError);
    }

    function setChallengeRetry(kind, code, recovery) {
        flow.retryKind = kind || '';
        flow.retryCode = code || '';
        flow.retryRecovery = !!recovery;
    }

    function factorLabel(value) {
        if (value === 1) return t('security.factor.app');
        if (value === 2) return t('twofa.email');
        if (value === 3) return 'Telegram';
        return String(value);
    }

    function renderFactors() {
        var values = flow.availableFactors.filter(function (value, index, list) {
            return Number(value) > 0 && list.indexOf(value) === index;
        });
        var optionsChanged = !renderedFactors || values.length !== renderedFactors.length ||
            values.some(function (value, index) { return value !== renderedFactors[index]; });
        if (optionsChanged) {
            while (factorInput.firstChild) factorInput.removeChild(factorInput.firstChild);
            values.forEach(function (value) {
                var option = document.createElement('option');
                option.value = String(value);
                option.textContent = factorLabel(Number(value));
                factorInput.appendChild(option);
            });
            renderedFactors = values.slice();
        }
        if (optionsChanged || flow.factor !== renderedFactor || flow.operation !== renderedFactorOperation) {
            if (values.indexOf(flow.factor) !== -1) factorInput.value = String(flow.factor);
            else if (values.length) factorInput.value = String(values[0]);
            renderedFactor = flow.factor;
            renderedFactorOperation = flow.operation;
        }
        factorGroup.hidden = flow.stage !== 'challenge' || flow.mode !== 3 || values.length < 2 || flow.recovery;
    }

    function syncUi() {
        loginForm.hidden = upgradeRequired || !runtimeReady;
        upgrade.hidden = !upgradeRequired;
        if (fastAuthCard) fastAuthCard.hidden = upgradeRequired || !runtimeReady;
        if (!runtimeReady || upgradeRequired) return;
        var legal = legalAccepted();
        var identifierStage = flow.stage === 'identifier';
        var passwordStage = flow.stage === 'password';
        var challengeStage = flow.stage === 'challenge';
        var codeVisible = challengeStage && flow.needsCode && !flow.terminal;
        var canResend = challengeStage && !flow.terminal && !flow.recovery && flow.challengeState === 1 && flow.factor !== 1;
        var remaining = Math.max(0, 60000 - (Date.now() - flow.resendAt));

        loginInput.readOnly = !identifierStage || flow.busy;
        loginInput.setAttribute('aria-readonly', String(!identifierStage || flow.busy));
        changeLoginBtn.hidden = identifierStage;
        changeLoginBtn.disabled = false;

        passwordGroup.hidden = !passwordStage;
        passwordInput.disabled = !passwordStage || flow.busy;
        var factorCount = flow.availableFactors.filter(function (value, index, list) {
            return Number(value) > 0 && list.indexOf(value) === index;
        }).length;
        factorGroup.hidden = !challengeStage || flow.mode !== 3 || factorCount < 2 || flow.recovery;
        factorInput.disabled = flow.busy || !legal;
        recoveryGroup.hidden = !challengeStage || (flow.mode !== 2 && flow.mode !== 3);
        recoveryInput.disabled = flow.busy || !legal;
        codeLabel.textContent = t(flow.recovery ? 'security.recoveryCode' : 'twofa.confirmationCode');

        challengeGroup.hidden = !challengeStage;
        codeGroup.hidden = !codeVisible;
        codeInput.disabled = !codeVisible || flow.busy;
        confirmCodeBtn.hidden = !codeVisible;
        confirmCodeBtn.disabled = flow.busy || !legal;
        confirmCodeBtn.classList.toggle('loading', flow.busy && codeVisible);
        retryBtn.hidden = !challengeStage || !flow.retryKind;
        retryBtn.disabled = flow.busy || !legal;
        resendBtn.hidden = !canResend;
        resendBtn.disabled = flow.busy || !legal || remaining > 0;
        if (canResend) resendBtn.textContent = t(flow.mode === 2 ? 'security.resendApproval' : 'register.resendCode');

        signInBtn.hidden = challengeStage;
        signInBtn.disabled = !legal || flow.busy;
        signInBtn.classList.toggle('loading', flow.busy && !challengeStage);
        signInLabel.textContent = t(flow.submitRetry ? 'loginFlow.retry' : identifierStage ? 'loginFlow.continue' : 'auth.signIn');
        toRegisterBtn.disabled = !legal;
        $('#recoverPassword').disabled = !legal;

        if (fastAuthCard) fastAuthCard.classList.toggle('gated', !legal);
        if (!legal) {
            legalRow.classList.remove('nudge');
            if (fastAuthCard) $('#fastAuthStatus').textContent = t('qr.legalRequired');
        }
    }

    function current(generation, origin) {
        return generation === flow.generation && origin === BF.node.origin() && origin === flow.origin;
    }

    function currentOperation(generation, operation, origin) {
        return current(generation, origin) && operation === flow.operation;
    }

    function resetFlow(restartQr) {
        if (cancelUpgrade) cancelUpgrade();
        var login = loginInput.value.trim();
        flow.generation++;
        if (flow.resolveController) {
            flow.resolveController.abort();
            flow.resolveController = null;
        }
        cancelAttempt();
        restorePreviousSessionIfNeeded();
        flow.origin = BF.node.origin();
        flow.stage = 'identifier';
        flow.mode = 0;
        flow.login = login;
        flow.password = '';
        flow.factor = 0;
        flow.recovery = false;
        flow.needsCode = false;
        flow.challengeState = 0;
        flow.availableFactors = [];
        flow.pollOperation = 0;
        flow.responseOperation = 0;
        flow.busy = false;
        flow.terminal = false;
        flow.retryKind = '';
        flow.submitRetry = false;
        flow.resendAt = 0;
        recoveryInput.checked = false;
        passwordInput.value = '';
        codeInput.value = '';
        clearErrors();
        if (BF.fastAuth) BF.fastAuth.cancel();
        syncUi();
        if (restartQr) startFastAuth();
        return flow.generation;
    }

    function applyGate() {
        var ok = legalAccepted();
        if (!legalCheck.checked) legalRow.classList.add('nudge');
        else legalRow.classList.remove('nudge');
        syncUi();
        if (!ok) {
            if (flow.stage !== 'identifier' || flow.busy) resetFlow(false);
            else if (BF.fastAuth) BF.fastAuth.cancel();
        } else {
            startFastAuth();
        }
    }

    function networkMessage(error) {
        if (error && (error.kind === 'timeout' || error.kind === 'transport' || error.code === 4 || error.code === 14))
            return t('auth.error.network');
        return BF.authUI.errorText(error);
    }

    function isRetryablePollError(error) {
        return Boolean(error && (
            error.retryable === true || error.kind === 'timeout' || error.kind === 'transport' ||
            error.code === 4 || error.code === 14
        ));
    }

    function showResolveError(error) {
        if (error && error.code === 12) return t('loginFlow.serverUpdateRequired');
        return networkMessage(error);
    }

    function resolveMode(generation, origin, preserveMessage) {
        var controller = new AbortController();
        flow.resolveController = controller;
        flow.busy = true;
        syncUi();
        return BF.confirmations.call(
            'resolveSignIn',
            'ResolveSignInRequest',
            { login: flow.login },
            { read: true, origin: origin, signal: controller.signal }
        ).then(function (response) {
            if (!current(generation, origin) || controller.signal.aborted) return;
            var errorCode = response.getErrorCode && response.getErrorCode();
            if (errorCode === 'account_not_found') {
                flow.stage = 'identifier';
                flow.mode = 0;
                showError(loginError, loginInput, t('loginFlow.accountNotFound'));
                loginInput.focus();
                return;
            }
            if (errorCode) {
                flow.stage = 'identifier';
                showError(loginError, loginInput, t('auth.error.server'));
                return;
            }
            var mode = Number(response.getLoginMode());
            if (mode !== 1 && mode !== 2 && mode !== 3) {
                flow.stage = 'identifier';
                showError(loginError, loginInput, t('auth.error.server'));
                return;
            }
            flow.mode = mode;
            flow.stage = preserveMessage ? 'identifier' : mode === 2 ? 'challenge' : 'password';
            if (!preserveMessage) clearError(loginError, loginInput);
            if (flow.stage === 'password') {
                flow.password = '';
                passwordInput.value = '';
            }
        }).catch(function (error) {
            if (!current(generation, origin) || error.name === 'AbortError') return;
            flow.stage = 'identifier';
            showError(loginError, loginInput, showResolveError(error));
            loginInput.focus();
        }).finally(function () {
            if (!current(generation, origin)) return;
            flow.resolveController = null;
            flow.busy = false;
            flow.submitRetry = !!loginError.textContent && loginError.textContent === t('auth.error.network');
            syncUi();
            if (flow.stage === 'password') passwordInput.focus();
            if (flow.stage === 'identifier') startFastAuth();
        });
    }

    function resolveSignIn() {
        if (!runtimeReady || upgradeRequired) return;
        if (flow.busy) return;
        if (!legalAccepted()) { legalRow.classList.add('nudge'); return; }
        var login = loginInput.value.trim();
        if (!login) {
            showError(loginError, loginInput, t('loginFlow.identifierRequired'));
            loginInput.focus();
            return;
        }
        clearErrors();
        if (BF.fastAuth) BF.fastAuth.cancel();
        flow.generation++;
        flow.origin = BF.node.origin();
        flow.login = login;
        loginInput.value = login;
        flow.mode = 0;
        flow.stage = 'identifier';
        flow.submitRetry = false;
        flow.password = '';
        passwordInput.value = '';
        var generation = flow.generation;
        var origin = flow.origin;
        resolveMode(generation, origin, false).then(function () {
            if (!current(generation, origin) || flow.mode !== 2 || flow.stage !== 'challenge' || flow.busy) return;
            flow.factor = 3;
            flow.recovery = false;
            flow.attempt = BF.confirmations.create();
            startAttempt(generation, origin);
        });
    }

    function beginFields() {
        return {
            login: flow.login,
            password: flow.mode === 2 ? '' : flow.password,
            loginMode: flow.mode,
            factor: flow.mode === 2 ? 3 : flow.factor,
            useRecoveryCode: flow.recovery,
            reportLoginModeMismatch: true
        };
    }

    function mismatch() {
        var origin = flow.origin;
        var login = flow.login;
        resetFlow(false);
        flow.origin = origin;
        flow.login = login;
        loginInput.value = login;
        showError(loginError, loginInput, t('loginFlow.modeChanged'));
        var generation = flow.generation;
        resolveMode(generation, origin, true);
    }

    function responseError(response) {
        return response && response.getErrorCode ? response.getErrorCode() : '';
    }

    function startAttempt(generation, origin) {
        if (!current(generation, origin) || !legalAccepted() || flow.busy) return;
        if (!flow.attempt) flow.attempt = BF.confirmations.create();
        flow.operation++;
        var operation = flow.operation;
        flow.busy = true;
        flow.terminal = false;
        flow.retryKind = '';
        flow.needsCode = false;
        flow.challengeState = 0;
        flow.availableFactors = [];
        flow.pollOperation = 0;
        flow.responseOperation = 0;
        codeInput.value = '';
        clearErrors();
        setChallengeMessage(t('common.loadingShort'), false);
        syncUi();

        flow.attempt.begin('beginSignIn', 'BeginSignInRequest', beginFields()).then(function (response) {
            if (!currentOperation(generation, operation, origin)) return;
            flow.busy = false;
            var errorCode = responseError(response);
            if (errorCode === 'login_mode_disabled') { mismatch(); return; }
            if (errorCode === 'account_not_found') {
                resetFlow(false);
                showError(loginError, loginInput, t('loginFlow.accountNotFound'));
                loginInput.focus();
                syncUi();
                startFastAuth();
                return;
            }
            if (errorCode) {
                flow.retryKind = 'begin';
                flow.stage = 'challenge';
                setChallengeMessage(t('auth.error.server'), true);
                syncUi();
                return;
            }
            flow.stage = 'challenge';
            flow.resendAt = Date.now();
            handleChallengeResponse(response, generation, operation, origin, false);
        }).catch(function (error) {
            if (!currentOperation(generation, operation, origin) || error.name === 'AbortError') return;
            flow.busy = false;
            if (error.errorCode === '21BFB9B5-C377-45D1-9B15-6B7F3432B397') {
                flow.stage = 'password';
                flow.submitRetry = false;
                showError(passwordError, passwordInput, t('auth.error.badCredentials'));
                passwordInput.focus();
            } else if (flow.mode === 2) {
                flow.stage = 'challenge';
                setChallengeRetry('begin');
                setChallengeMessage(networkMessage(error), true);
            } else {
                flow.stage = 'password';
                flow.submitRetry = error.kind === 'timeout' || error.kind === 'transport' || error.code === 14;
                showError(passwordError, passwordInput, networkMessage(error));
            }
            syncUi();
        }).finally(function () {
            if (!currentOperation(generation, operation, origin)) return;
            syncUi();
        });
    }

    function handleChallengeResponse(response, generation, operation, origin, fromPoll) {
        if (!currentOperation(generation, operation, origin)) return;
        var errorCode = responseError(response);
        if (errorCode === 'login_mode_disabled') { mismatch(); return; }
        if (errorCode === 'account_not_found') {
            resetFlow(false);
            showError(loginError, loginInput, t('loginFlow.accountNotFound'));
            loginInput.focus();
            syncUi();
            startFastAuth();
            return;
        }
        if (errorCode) {
            setChallengeMessage(t('auth.error.server'), true);
            setChallengeRetry('begin');
            syncUi();
            return;
        }

        flow.challengeState = Number(response.getState());
        flow.factor = Number(response.getFactor()) || flow.factor;
        flow.needsCode = !!response.getNeedsCode();
        flow.availableFactors = response.getAvailableFactorsList ? response.getAvailableFactorsList().map(Number) : [];
        flow.terminal = [3, 4, 6].indexOf(flow.challengeState) !== -1;
        flow.recovery = !!recoveryInput.checked;
        if (flow.responseOperation !== operation) {
            flow.responseOperation = operation;
            clearError(codeError, codeInput);
            codeInput.value = '';
        }
        renderFactors();

        var terminalText = {
            3: 'security.rejected',
            4: 'security.expired',
            6: 'security.cancelled'
        }[flow.challengeState];
        if (flow.terminal) {
            setChallengeMessage(t(terminalText), false);
            setChallengeRetry('begin');
        } else if (flow.challengeState === 2 || flow.challengeState === 5) {
            setChallengeMessage(t('common.loadingShort'), false);
            setChallengeRetry('');
            completeChallenge('', flow.recovery, generation, operation, origin);
        } else {
            setChallengeRetry('');
            setChallengeMessage(
                t(flow.needsCode ? 'security.enterCode' : flow.mode === 2 ? 'security.telegramLoginWaiting' : 'security.waiting'),
                false
            );
        }

        syncUi();
        if (!fromPoll && !flow.terminal && flow.challengeState === 1 && flow.attempt && flow.pollOperation !== operation) {
            flow.pollOperation = operation;
            if (flow.needsCode) codeInput.focus();
            flow.attempt.poll(
                function (state) {
                    handleChallengeResponse(state, generation, operation, origin, true);
                },
                function (error) {
                    if (!currentOperation(generation, operation, origin) || error?.name === 'AbortError') return;
                    if (isRetryablePollError(error)) {
                        setChallengeMessage(networkMessage(error), true);
                        syncUi();
                        return;
                    }
                    flow.attempt.cancel();
                    clearResendTimer();
                    setChallengeRetry('begin');
                    setChallengeMessage(networkMessage(error), true);
                    syncUi();
                }
            );
            startResendTimer(generation, operation, origin);
        }
    }

    function startResendTimer(generation, operation, origin) {
        clearResendTimer();
        flow.resendTimer = setInterval(function () {
            if (!currentOperation(generation, operation, origin)) { clearResendTimer(); return; }
            if (Date.now() - flow.resendAt >= 60000) clearResendTimer();
            syncUi();
        }, 1000);
    }

    function saveSession(result, generation, operation, origin) {
        if (!currentOperation(generation, operation, origin) || !legalAccepted() || flow.pendingSession) return;
        var response = result.getSession && result.getSession();
        if (!response) {
            flow.busy = false;
            setChallengeMessage(t('auth.error.server'), true);
            syncUi();
            return;
        }
        var session = BF.confirmations.session(response);
        if (!session.accessToken || !session.refreshToken) {
            flow.busy = false;
            setChallengeMessage(t('auth.error.server'), true);
            syncUi();
            return;
        }
        var previous = BF.tokens.get();
        var previousTemp = false;
        try { previousTemp = localStorage.getItem(BF.node.key('barkfluff_temp')) === '1'; } catch {}
        flow.pendingSession = {
            origin: origin,
            accessToken: session.accessToken,
            previous: previous,
            previousTemp: previousTemp
        };
        if (!legalAccepted() || !currentOperation(generation, operation, origin)) {
            restorePreviousSessionIfNeeded();
            return;
        }
        BF.tokens.setTempMode(tempLoginCheck.checked);
        BF.tokens.save(session);
        flow.attempt = null;
        clearResendTimer();
        BF.legal.flushConsent().then(function () {
            if (!currentOperation(generation, operation, origin) || !legalAccepted()) {
                restorePreviousSessionIfNeeded();
                return;
            }
            flow.pendingSession = null;
            flow.stage = 'done';
            flow.busy = false;
            window.location.href = '/messenger';
        });
    }

    function completeChallenge(code, recovery, generation, operation, origin) {
        if (!currentOperation(generation, operation, origin) || flow.busy || !legalAccepted() || !flow.attempt) return;
        flow.busy = true;
        setChallengeRetry('');
        clearError(codeError, codeInput);
        syncUi();
        flow.attempt.complete(code, recovery).then(function (result) {
            if (!currentOperation(generation, operation, origin)) return;
            var errorCode = responseError(result);
            if (errorCode === 'login_mode_disabled') { mismatch(); return; }
            if (errorCode) {
                flow.busy = false;
                if (errorCode === 'invalid_code') {
                    showError(codeError, codeInput, t('auth.error.badCode'));
                    if (Number(result.getState()) === 3) {
                        flow.terminal = true;
                        flow.needsCode = false;
                        setChallengeMessage(t('security.rejected'), false);
                        setChallengeRetry('begin');
                    }
                } else {
                    setChallengeMessage(t('auth.error.server'), true);
                }
                syncUi();
                return;
            }
            if (Number(result.getState()) === 5 && result.getSession && result.getSession()) {
                saveSession(result, generation, operation, origin);
                return;
            }
            flow.busy = false;
            if ([3, 4, 6].indexOf(Number(result.getState())) !== -1) {
                flow.terminal = true;
                flow.needsCode = false;
                var terminalKey = { 3: 'security.rejected', 4: 'security.expired', 6: 'security.cancelled' }[Number(result.getState())];
                setChallengeMessage(terminalKey ? t(terminalKey) : t('auth.error.server'), false);
                setChallengeRetry('begin');
            } else {
                setChallengeMessage(t('auth.error.server'), true);
            }
            syncUi();
        }).catch(function (error) {
            if (!currentOperation(generation, operation, origin) || error.name === 'AbortError') return;
            flow.busy = false;
            setChallengeRetry('complete', code, recovery);
            setChallengeMessage(networkMessage(error), true);
            syncUi();
        }).finally(function () {
            if (!currentOperation(generation, operation, origin) || flow.stage === 'done') return;
            if (flow.pendingSession) return;
            flow.busy = false;
            syncUi();
        });
    }

    function submitPassword() {
        if (flow.busy) return;
        if (!legalAccepted()) { legalRow.classList.add('nudge'); return; }
        clearErrors();
        var password = passwordInput.value;
        if (!password) {
            showError(passwordError, passwordInput, t('auth.error.noPassword'));
            passwordInput.focus();
            return;
        }
        flow.password = password;
        flow.factor = 0;
        flow.recovery = false;
        flow.submitRetry = false;
        if (BF.fastAuth) BF.fastAuth.cancel();
        flow.stage = 'password';
        syncUi();
        startAttempt(flow.generation, flow.origin);
    }

    function retryChallenge() {
        if (flow.busy || !legalAccepted()) return;
        var kind = flow.retryKind;
        var code = flow.retryCode;
        var recovery = flow.retryRecovery;
        if (kind === 'complete') {
            completeChallenge(code, recovery, flow.generation, flow.operation, flow.origin);
        } else if (kind === 'resend') {
            resendChallenge();
        } else {
            startAttempt(flow.generation, flow.origin);
        }
    }

    function submitCode() {
        if (flow.busy || !legalAccepted() || !flow.needsCode || flow.terminal) return;
        var code = codeInput.value.trim();
        if (!code) {
            showError(codeError, codeInput, t('security.enterCode'));
            codeInput.focus();
            return;
        }
        completeChallenge(code, flow.recovery, flow.generation, flow.operation, flow.origin);
    }

    function resendChallenge() {
        if (flow.busy || !legalAccepted() || !flow.attempt || !flow.challengeState || flow.terminal) return;
        var generation = flow.generation;
        var operation = flow.operation;
        var origin = flow.origin;
        flow.busy = true;
        setChallengeRetry('');
        syncUi();
        flow.attempt.resend().then(function (response) {
            if (!currentOperation(generation, operation, origin)) return;
            flow.busy = false;
            var errorCode = responseError(response);
            if (errorCode === 'login_mode_disabled') { mismatch(); return; }
            if (errorCode) {
                setChallengeRetry('resend');
                setChallengeMessage(t('auth.error.server'), true);
                syncUi();
                return;
            }
            flow.resendAt = Date.now();
            flow.responseOperation = 0;
            codeInput.value = '';
            clearError(codeError, codeInput);
            handleChallengeResponse(response, generation, operation, origin, true);
        }).catch(function (error) {
            if (!currentOperation(generation, operation, origin) || error.name === 'AbortError') return;
            flow.busy = false;
            setChallengeRetry('resend');
            setChallengeMessage(networkMessage(error), true);
            syncUi();
        }).finally(function () {
            if (!currentOperation(generation, operation, origin)) return;
            syncUi();
        });
    }

    function recoverPassword() {
        if (!legalAccepted()) { legalRow.classList.add('nudge'); return; }
        var login = loginInput.value.trim();
        var generation = resetFlow(false);
        flow.login = login;
        loginInput.value = login;
        flow.busy = true;
        syncUi();
        BF.authUI.recover(login).then(function () {
            if (generation !== flow.generation || !legalAccepted()) return;
            showError(loginError, loginInput, t('security.passwordChanged'));
        }).catch(function (error) {
            if (generation === flow.generation && error.name !== 'AbortError')
                showError(loginError, loginInput, networkMessage(error));
        }).finally(function () {
            if (generation !== flow.generation) return;
            flow.busy = false;
            syncUi();
            startFastAuth();
        });
    }

    BF.nodePicker.init();

    nodeChangeBtn.addEventListener('click', function () {
        resetFlow(false);
        BF.authUI.cancelAll();
        if (BF.register) BF.register.close();
        if (BF.fastAuth) BF.fastAuth.cancel();
        BF.node.clear();
        BF.nodePicker.open({
            onSelected: function () {
                BF.nodePicker.close();
                renderNodeBar();
                resumeOrShowLogin();
            }
        });
    });

    legalCheck.addEventListener('change', function () {
        if (legalCheck.checked) BF.legal.accept();
        else resetFlow(false);
        applyGate();
    });

    loginForm.addEventListener('submit', function (event) {
        event.preventDefault();
        if (upgradeRequired) return;
        if (flow.stage === 'identifier') resolveSignIn();
        else if (flow.stage === 'password') submitPassword();
    });

    changeLoginBtn.addEventListener('click', function () {
        resetFlow(true);
        loginInput.focus();
    });

    factorInput.addEventListener('change', function () {
        if (flow.busy || flow.recovery) return;
        var next = Number(factorInput.value);
        if (!flow.availableFactors.includes(next) || next === flow.factor) return;
        flow.factor = next;
        if (flow.attempt) flow.attempt.cancel();
        startAttempt(flow.generation, flow.origin);
    });

    recoveryInput.addEventListener('change', function () {
        if (flow.busy || (flow.mode !== 2 && flow.mode !== 3)) return;
        flow.recovery = recoveryInput.checked;
        if (flow.attempt) flow.attempt.cancel();
        startAttempt(flow.generation, flow.origin);
    });

    confirmCodeBtn.addEventListener('click', submitCode);
    retryBtn.addEventListener('click', retryChallenge);
    resendBtn.addEventListener('click', resendChallenge);
    codeInput.addEventListener('keydown', function (event) {
        if (event.key === 'Enter') {
            event.preventDefault();
            submitCode();
        }
    });
    codeInput.addEventListener('input', function () {
        clearError(codeError, codeInput);
    });
    passwordInput.addEventListener('input', function () {
        flow.password = this.value;
        clearError(passwordError, passwordInput);
    });
    loginInput.addEventListener('input', function () {
        clearError(loginError, loginInput);
        flow.submitRetry = false;
        syncUi();
    });
    $('#recoverPassword').addEventListener('click', recoverPassword);

    // This listener runs before register.js opens its overlay and cancels the active sign-in attempt.
    toRegisterBtn.addEventListener('click', function () { resetFlow(false); }, true);

    window.addEventListener('pagehide', function () { resetFlow(false); });

    upgradeBtn.addEventListener('click', applyUpgrade);
    if (upgradeRequired) showUpgrade();
    legalCheck.checked = BF.legal.isAccepted();
    syncUi();
    applyGate();

    BF.i18n.ready.then(function () {
        runtimeReady = true;
        if (upgradeRequired || t('loginFlow.continue') === 'loginFlow.continue') showUpgrade();
        return BF.legal.init();
    }).then(function () {
        legalCheck.checked = BF.legal.isAccepted();
        applyGate();
        if (!ensureNode()) return;
        resumeOrShowLogin();
    });
})();
