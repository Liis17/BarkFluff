/* Shared confirmation UI for login, registration and security settings. */
(function () {
    'use strict';
    var active = new Set();
    var lifecycle = new AbortController();
    function ensureActive(signal) {
        if (signal.aborted) throw new DOMException('Cancelled', 'AbortError');
    }
    var t = function (key, params) {
        return BF.i18n.t(key, params);
    };
    function el(tag, text, parent) {
        var node = document.createElement(tag);
        if (text) node.textContent = text;
        if (parent) parent.appendChild(node);
        return node;
    }
    function field(parent, label, type) {
        var wrapper = el('label', label, parent);
        var input = el('input', '', wrapper);
        input.type = type || 'text';
        return input;
    }
    function select(parent, label, values, current) {
        var wrapper = el('label', label, parent),
            input = el('select', '', wrapper);
        values.forEach(function (item) {
            var option = el('option', item[1], input);
            option.value = item[0];
        });
        input.value = String(current);
        return input;
    }
    function button(parent, label, action) {
        var value = el('button', label, parent);
        value.type = 'button';
        if (action) value.addEventListener('click', action);
        return value;
    }
    function errorText(error) {
        if (error?.name === 'AbortError') return '';
        if (error?.code === 8) return t('security.rateLimited');
        var code = error?.metadata?.['x-error-code'];
        if (code === '21BFB9B5-C377-45D1-9B15-6B7F3432B397') return t('auth.error.badCredentials');
        return error?.message || t('auth.error.network');
    }
    function modal(title) {
        var dialog = el('dialog', '', document.body);
        dialog.className = 'auth-dialog';
        el('h2', title, dialog);
        var content = el('div', '', dialog);
        var error = el('p', '', dialog);
        error.setAttribute('role', 'alert');
        var actions = el('div', '', dialog);
        var resolve,
            reject,
            settled = false;
        var promise = new Promise(function (yes, no) {
            resolve = yes;
            reject = no;
        });
        var controller = new AbortController();
        function finish(value, cancelled) {
            if (settled) return;
            settled = true;
            controller.abort();
            active.delete(cancel);
            dialog.close();
            dialog.remove();
            if (cancelled) reject(new DOMException('Cancelled', 'AbortError'));
            else resolve(value);
        }
        function cancel() {
            finish(null, true);
        }
        button(actions, t('common.cancel'), cancel);
        dialog.addEventListener('cancel', function (event) {
            event.preventDefault();
            cancel();
        });
        active.add(cancel);
        dialog.showModal();
        return {
            dialog: dialog,
            body: content,
            actions: actions,
            error: error,
            promise: promise,
            signal: controller.signal,
            done: function (value) {
                finish(value, false);
            },
            cancel: cancel
        };
    }
    var modes = function () {
        return [
            [1, t('security.mode.password')],
            [2, t('security.mode.telegram')],
            [3, t('security.mode.factor')]
        ];
    };
    var factors = function () {
        return [
            [0, t('security.preferred')],
            [1, t('security.factor.app')],
            [2, t('twofa.email')],
            [3, 'Telegram']
        ];
    };
    function codes(values) {
        if (!values?.length) return Promise.resolve();
        var box = modal(t('security.recoveryCodes'));
        el('p', t('security.codesHint'), box.body).className = 'auth-hint';
        el('pre', values.join('\n'), box.body);
        button(box.actions, t('common.copy'), async function () {
            try {
                await navigator.clipboard.writeText(values.join('\n'));
            } catch (error) {
                box.error.textContent = errorText(error);
            }
        });
        button(box.actions, t('security.codesSaved'), function () {
            box.done();
        });
        return box.promise;
    }
    async function run(method, type, fields, options) {
        options = options || {};
        var box = modal(t('security.confirmation'));
        var attempt = BF.confirmations.create({ auth: options.auth });
        box.signal.addEventListener('abort', function () {
            attempt.cancel();
        });
        var status = el('p', t('common.loadingShort'), box.body);
        status.setAttribute('role', 'status');
        var link = el('a', t('security.openBot'), box.body);
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
        link.hidden = true;
        var factor = select(box.body, t('security.factor'), factors(), fields.factor || 0);
        factor.parentElement.hidden = true;
        var code = field(box.body, t(fields.useRecoveryCode ? 'security.recoveryCode' : 'twofa.confirmationCode'));
        code.autocomplete = 'one-time-code';
        code.maxLength = 64;
        code.parentElement.hidden = true;
        var confirm = button(box.actions, t('common.confirm'), function () {
            finish();
        });
        confirm.hidden = true;
        var resend = button(box.actions, t('register.resendCode'), async function () {
            var generation = operationGeneration;
            resend.disabled = true;
            try {
                var response = await attempt.resend();
                if (box.signal.aborted || generation !== operationGeneration) return;
                render(response, generation);
                sentAt = Date.now();
            } catch (error) {
                if (!box.signal.aborted && generation === operationGeneration && error?.name !== 'AbortError')
                    box.error.textContent = errorText(error);
            }
        });
        if (options.telegramLogin) resend.textContent = t('security.resendApproval');
        resend.hidden = true;
        var sentAt = Date.now(),
            busy = false,
            terminal = false,
            operationGeneration = 0;
        var timer = setInterval(function () {
            resend.disabled = busy || terminal || Date.now() - sentAt < 60000;
        }, 1000);
        box.signal.addEventListener('abort', function () {
            clearInterval(timer);
        });
        function render(response, generation) {
            if (box.signal.aborted || generation !== operationGeneration) return;
            var challengeError = response.getErrorCode?.();
            if (challengeError) {
                terminal = true;
                status.textContent = '';
                box.error.textContent = t(
                    challengeError === 'login_mode_disabled' ? 'auth.error.loginModeDisabled' : 'auth.error.server'
                );
                code.parentElement.hidden = true;
                confirm.hidden = true;
                resend.hidden = true;
                link.hidden = true;
                return;
            }
            var state = response.getState();
            terminal = [3, 4, 6].includes(state);
            status.textContent = t(
                terminal
                    ? {
                          3: 'security.rejected',
                          4: 'security.expired',
                          6: 'security.cancelled'
                      }[state]
                    : response.getNeedsCode()
                      ? 'security.enterCode'
                      : options.telegramLogin
                        ? 'security.telegramLoginWaiting'
                        : 'security.waiting'
            );
            code.parentElement.hidden = !response.getNeedsCode() || terminal;
            confirm.hidden = code.parentElement.hidden;
            resend.hidden = terminal || fields.useRecoveryCode || response.getFactor() === 1 || state !== 1;
            if (response.getTelegramUrl()) {
                link.href = response.getTelegramUrl();
                link.hidden = terminal;
            }
            if (terminal) link.hidden = true;
            var available = response.getAvailableFactorsList();
            if (options.switchFactor && !fields.useRecoveryCode && available.length > 1) {
                factor.parentElement.hidden = terminal;
                Array.from(factor.options).forEach(function (option) {
                    option.hidden = !available.includes(Number(option.value));
                });
                factor.value = String(response.getFactor());
            }
            if (state === 2) finish(generation);
        }
        async function finish(generation) {
            generation = generation === undefined ? operationGeneration : generation;
            if (busy || terminal || box.signal.aborted || generation !== operationGeneration) return;
            busy = true;
            confirm.disabled = true;
            factor.disabled = true;
            box.error.textContent = '';
            try {
                var result = await attempt.complete(code.value.trim(), fields.useRecoveryCode);
                if (box.signal.aborted || generation !== operationGeneration) return;
                if (result.getErrorCode()) {
                    box.error.textContent = t('auth.error.badCode');
                    if (result.getState() === 3) {
                        terminal = true;
                        status.textContent = t('security.rejected');
                    }
                } else if (result.getState() === 5) {
                    // Acknowledge the one-time display before leaving the registration/settings flow.
                    await codes(result.getRecoveryCodesList());
                    if (!box.signal.aborted && generation === operationGeneration) box.done(result);
                } else {
                    terminal = true;
                    status.textContent = t('security.expired');
                }
            } catch (error) {
                if (!box.signal.aborted && generation === operationGeneration && error?.name !== 'AbortError') {
                    box.error.textContent = errorText(error);
                    confirm.hidden = false;
                }
            } finally {
                if (generation === operationGeneration) {
                    busy = false;
                    confirm.disabled = false;
                    factor.disabled = false;
                }
            }
        }
        async function begin() {
            var generation = ++operationGeneration;
            busy = false;
            terminal = false;
            code.value = '';
            code.parentElement.hidden = true;
            confirm.hidden = true;
            confirm.disabled = false;
            factor.disabled = false;
            resend.hidden = true;
            link.hidden = true;
            status.textContent = t('common.loadingShort');
            box.error.textContent = '';
            try {
                var response = await attempt.begin(method, type, fields);
                if (box.signal.aborted || generation !== operationGeneration) return;
                sentAt = Date.now();
                render(response, generation);
                if (response.getErrorCode?.()) return;
                attempt.poll(
                    function (state) {
                        render(state, generation);
                    },
                    function (error) {
                        if (!box.signal.aborted && generation === operationGeneration && error?.name !== 'AbortError')
                            box.error.textContent = errorText(error);
                    }
                );
            } catch (error) {
                if (!box.signal.aborted && generation === operationGeneration && error?.name !== 'AbortError') {
                    box.error.textContent = errorText(error);
                    status.textContent = '';
                }
            }
        }
        factor.addEventListener('change', function () {
            fields.factor = Number(factor.value);
            begin();
        });
        code.addEventListener('keydown', function (event) {
            if (event.key === 'Enter') {
                event.preventDefault();
                finish();
            }
        });
        begin();
        return box.promise;
    }
    async function reauthenticate(settings) {
        var owner = lifecycle.signal;
        var queries = [
            settings
                ? Promise.resolve(null)
                : BF.confirmations.call(
                      'getSecuritySettings',
                      'GetSecuritySettingsRequest',
                      {},
                      { auth: true, read: true, signal: owner }
                  ),
            BF.confirmations.call(
                'getAuthCapabilities',
                'GetAuthCapabilitiesRequest',
                {},
                { read: true, signal: owner }
            )
        ];
        var results = await Promise.all(queries);
        settings = settings || results[0].toObject();
        var capabilities = results[1];
        ensureActive(owner);
        var box = modal(t('security.reauthenticate'));
        var password = field(box.body, t('common.password'), 'password');
        password.autocomplete = 'current-password';
        password.parentElement.hidden = settings.loginMode === 2;
        var factor = select(
            box.body,
            t('security.factor'),
            factors().filter(function (f) {
                return (
                    f[0] === 0 ||
                    (f[0] === 1 && settings.authenticatorEnabled) ||
                    (f[0] === 2 && settings.emailEnabled && capabilities.getEmailAvailable()) ||
                    (f[0] === 3 &&
                        settings.telegramEnabled &&
                        settings.telegramOtpEnabled &&
                        capabilities.getTelegramAvailable())
                );
            }),
            settings.preferredFactor || 0
        );
        factor.parentElement.hidden = settings.loginMode !== 3;
        var recovery = field(box.body, t('security.useRecoveryCode'), 'checkbox');
        recovery.parentElement.hidden = settings.loginMode === 1;
        var submit = button(box.actions, t('common.confirm'), async function () {
            submit.disabled = true;
            try {
                var response = await run(
                    'beginReauthentication',
                    'BeginReauthenticationRequest',
                    {
                        password: password.value,
                        factor: Number(factor.value),
                        useRecoveryCode: recovery.checked
                    },
                    { auth: true, switchFactor: settings.loginMode === 3 }
                );
                if (!box.signal.aborted) box.done(response.getSecurityProof());
            } catch (error) {
                box.error.textContent = errorText(error);
            } finally {
                submit.disabled = false;
            }
        });
        return box.promise;
    }
    async function recover(login) {
        var box = modal(t('security.resetPassword'));
        var user = field(box.body, t('auth.loginField'));
        user.value = login || '';
        user.autocomplete = 'username';
        var password = field(box.body, t('register.step.password'), 'password');
        password.autocomplete = 'new-password';
        password.minLength = 8;
        el('p', t('security.resetHint'), box.body).className = 'auth-hint';
        var submit = button(box.actions, t('common.confirm'), async function () {
            if (password.value.length < 8) {
                box.error.textContent = t('register.error.min8');
                return;
            }
            submit.disabled = true;
            try {
                var result = await run('beginPasswordRecovery', 'BeginPasswordRecoveryRequest', {
                    login: user.value.trim()
                });
                await BF.confirmations.call(
                    'setRecoveredPassword',
                    'SetRecoveredPasswordRequest',
                    {
                        securityProof: result.getSecurityProof(),
                        password: password.value
                    },
                    { signal: box.signal }
                );
                box.done();
            } catch (error) {
                box.error.textContent = errorText(error);
            } finally {
                submit.disabled = false;
            }
        });
        return box.promise;
    }
    async function setupFactor(factor) {
        var owner = lifecycle.signal;
        var proof = await reauthenticate();
        ensureActive(owner);
        var result = await BF.confirmations.call(
            'enableOtpVerification',
            'EnableOtpVerificationRequest',
            { otpType: factor, securityProof: proof },
            { auth: true, signal: owner }
        );
        ensureActive(owner);
        var box = modal(t('settings.twofa'));
        if (result.getOtpQr()) {
            var image = el('img', '', box.body);
            image.src = 'data:image/png;base64,' + result.getOtpQr();
            image.alt = t('register.twofa.qrAlt');
            image.width = image.height = 180;
        }
        if (result.getOtpCode()) el('pre', result.getOtpCode(), box.body);
        var code = field(box.body, t('twofa.confirmationCode'));
        code.autocomplete = 'one-time-code';
        var submit = button(box.actions, t('common.confirm'), async function () {
            submit.disabled = true;
            try {
                var response = await BF.confirmations.call(
                    'confirmOtpVerification',
                    'ConfirmOtpVerificationRequest',
                    { otpCode: code.value.trim(), securityProof: proof },
                    { auth: true, signal: box.signal }
                );
                await codes(response.getRecoveryCodesList());
                box.done();
            } catch (error) {
                box.error.textContent = errorText(error);
            } finally {
                submit.disabled = false;
            }
        });
        return box.promise;
    }
    function renderSecurity(container) {
        var controller = new AbortController(),
            origin = BF.node.origin(),
            options = { auth: true, signal: controller.signal, origin: origin },
            renderGeneration = 0,
            root,
            data,
            capabilities,
            draft,
            busy = false,
            syncCurrent;
        var cleanup = function () {
            controller.abort();
            active.delete(cleanup);
        };
        active.add(cleanup);

        function telegramConfigured() {
            return capabilities.getTelegramAvailable();
        }
        function emailConfigured() {
            return capabilities.getEmailAvailable();
        }
        function hasVerifiedEmail() {
            return Boolean(data.verifiedEmail && data.verifiedEmail.trim());
        }
        function canReceiveEmailAlerts() {
            return emailConfigured() && hasVerifiedEmail();
        }
        function telegramLoginAvailable(value) {
            return Boolean(data.telegramLinked && value.telegramEnabled && telegramConfigured());
        }
        function availableFactors(value, withoutTelegram) {
            var available = [];
            if (data.authenticatorEnabled) available.push(1);
            if (data.emailEnabled && emailConfigured() && hasVerifiedEmail()) available.push(2);
            if (
                !withoutTelegram &&
                data.telegramLinked &&
                value.telegramEnabled &&
                value.telegramOtpEnabled &&
                telegramConfigured()
            )
                available.push(3);
            return available;
        }
        function modeIsValid(value, withoutTelegram) {
            if (value.loginMode === 1) return true;
            if (value.loginMode === 2) return !withoutTelegram && telegramLoginAvailable(value);
            var choices = availableFactors(value, withoutTelegram);
            return value.loginMode === 3 && choices.includes(Number(value.preferredFactor));
        }
        function changed() {
            return (
                draft.loginMode !== data.loginMode ||
                draft.preferredFactor !== data.preferredFactor ||
                draft.telegramEnabled !== data.telegramEnabled ||
                draft.telegramOtpEnabled !== data.telegramOtpEnabled ||
                draft.fastAuthTelegramEnabled !== data.fastAuthTelegramEnabled
            );
        }
        function unlinkReason() {
            if (!canReceiveEmailAlerts()) return t('security.prerequisite.emailForTelegram');
            if (!modeIsValid(draft, true)) {
                if (draft.loginMode === 2) return t('security.prerequisite.unlinkMode');
                return t('security.prerequisite.unlinkFactor');
            }
            return '';
        }
        function modePrerequisite() {
            if (draft.loginMode === 3) {
                var available = availableFactors(draft, false);
                if (!available.length) return t('security.prerequisite.secondFactor');
                if (!available.includes(Number(draft.preferredFactor)))
                    return t('security.prerequisite.chooseFactor');
            }
            if (!draft.telegramEnabled && !canReceiveEmailAlerts())
                return t('security.prerequisite.emailForTelegram');
            return '';
        }
        function factorRemovalReason(factor) {
            if (data.loginMode === 3) {
                var persistedFactors = availableFactors(
                    {
                        telegramEnabled: data.telegramEnabled,
                        telegramOtpEnabled: data.telegramOtpEnabled
                    },
                    false
                );
                if (persistedFactors.includes(factor) && persistedFactors.length === 1)
                    return 'security.prerequisite.onlyFactorDisable';
                if (Number(data.preferredFactor) === factor)
                    return 'security.prerequisite.switchPreferredBeforeDisable';
            }
            if (draft.loginMode === 3 && Number(draft.preferredFactor) === factor)
                return 'security.prerequisite.switchPreferredBeforeDisable';
            return '';
        }
        function createForm() {
            container.replaceChildren();
            root = el('div', '', container);
            root.className = 'auth-security';
            root.setAttribute('aria-busy', 'false');
            var error = el('p', '', root);
            error.setAttribute('role', 'alert');
            error.setAttribute('aria-live', 'assertive');
            var controls = [];
            var buttons = [];
            function register(element, enabled) {
                controls.push({ element: element, enabled: enabled });
                return element;
            }
            function group(key) {
                var section = el('section', '', root);
                section.className = 'auth-security-section';
                el('h3', t(key), section);
                return section;
            }
            function rowLabel(parent, title, description) {
                var label = el('label', '', parent);
                label.className = 'auth-security-toggle';
                var input = el('input', '', label);
                input.type = 'checkbox';
                var text = el('span', '', label);
                el('span', title, text).className = 'auth-security-label';
                if (description) el('small', description, text);
                return { label: label, input: input };
            }
            function actionButton(parent, label, enabled, work, preserveDraft) {
                var value = button(parent, t(label), function () {
                    perform(work, preserveDraft);
                });
                buttons.push({ element: value, enabled: enabled });
                return value;
            }
            function addModeCard(parent, value, labelKey, descriptionKey) {
                var card = el('label', '', parent);
                card.className = 'auth-mode-card';
                var radio = el('input', '', card);
                radio.type = 'radio';
                radio.name = 'auth-security-mode-' + renderGeneration;
                radio.value = String(value);
                radio.checked = draft.loginMode === value;
                var text = el('span', '', card);
                el('strong', t(labelKey), text);
                el('small', t(descriptionKey), text);
                radio.addEventListener('change', function () {
                    if (radio.checked) draft.loginMode = value;
                    sync();
                });
                register(radio, function () {
                    return value !== 2 || telegramLoginAvailable(draft);
                });
                return radio;
            }

            var modeSection = group('security.mode');
            var modePassword = addModeCard(
                modeSection,
                1,
                'security.mode.password',
                'security.mode.description.password'
            );
            var modeTelegram = addModeCard(
                modeSection,
                2,
                'security.mode.telegram',
                'security.mode.description.telegram'
            );
            var modeTelegramStatus = el('p', '', modeSection);
            modeTelegramStatus.className = 'auth-hint';
            modeTelegramStatus.setAttribute('role', 'status');
            modeTelegramStatus.setAttribute('aria-live', 'polite');
            var modeFactor = addModeCard(
                modeSection,
                3,
                'security.mode.factor',
                'security.mode.description.factor'
            );

            var preferredLabel = el('label', t('security.preferred'), modeSection);
            preferredLabel.className = 'auth-security-preferred';
            var preferred = el('select', '', preferredLabel);
            register(preferred, function () {
                return draft.loginMode === 3 && availableFactors(draft, false).length > 0;
            });
            preferred.addEventListener('change', function () {
                if (preferred.value) draft.preferredFactor = Number(preferred.value);
                sync();
            });
            var modeStatus = el('p', '', modeSection);
            modeStatus.className = 'auth-hint';
            modeStatus.setAttribute('role', 'status');
            modeStatus.setAttribute('aria-live', 'polite');
            var setupFactorAction = actionButton(modeSection, 'security.setupAuthenticator', function () {
                return !data.authenticatorEnabled;
            }, async function () {
                await setupFactor(1);
            });

            var telegramSection = group('security.telegramSection');
            var telegramStatus = el('p', '', telegramSection);
            telegramStatus.className = 'auth-security-info';
            telegramStatus.setAttribute('role', 'status');
            telegramStatus.setAttribute('aria-live', 'polite');
            var telegramLinkAction = null;
            if (data.telegramLinked) {
                var master = rowLabel(telegramSection, t('security.telegramEnabled'), t('security.telegramEnabledHint'));
                master.input.checked = draft.telegramEnabled;
                master.input.addEventListener('change', function () {
                    draft.telegramEnabled = master.input.checked;
                    sync();
                });
                register(master.input, function () {
                    return data.telegramLinked && (draft.telegramEnabled || telegramConfigured());
                });

                var otp = rowLabel(telegramSection, t('security.telegramFactor'), t('security.telegramFactorHint'));
                otp.input.checked = draft.telegramOtpEnabled;
                otp.input.addEventListener('change', function () {
                    draft.telegramOtpEnabled = otp.input.checked;
                    sync();
                });
                register(otp.input, function () {
                    return data.telegramLinked && draft.telegramEnabled && telegramConfigured();
                });

                var fast = rowLabel(telegramSection, t('security.fastAuthTelegram'), t('security.fastAuthTelegramHint'));
                fast.input.checked = draft.fastAuthTelegramEnabled;
                fast.input.addEventListener('change', function () {
                    draft.fastAuthTelegramEnabled = fast.input.checked;
                    sync();
                });
                register(fast.input, function () {
                    return data.telegramLinked && draft.telegramEnabled && telegramConfigured();
                });
                actionButton(telegramSection, 'security.unlinkTelegram', function () {
                    return data.telegramLinked && !unlinkReason();
                }, async function () {
                    await save(true);
                }, false);
                el('p', t('security.unlinkImpact'), telegramSection).className = 'auth-hint';
                var unlinkStatus = el('p', '', telegramSection);
                unlinkStatus.className = 'auth-security-status';
                unlinkStatus.setAttribute('role', 'status');
                unlinkStatus.setAttribute('aria-live', 'polite');
                telegramLinkAction = { unlinkStatus: unlinkStatus };
            } else if (telegramConfigured()) {
                actionButton(telegramSection, 'security.linkTelegram', function () {
                    return !data.telegramLinked;
                }, async function () {
                    var proof = await reauthenticate(data);
                    await run(
                        'beginTelegramBinding',
                        'SecurityProofRequest',
                        { securityProof: proof },
                        { auth: true }
                    );
                });
            } else {
                el('p', t('security.prerequisite.telegramUnavailable'), telegramSection).className = 'auth-hint';
            }
            el('p', t('security.telegramDisabledNote'), telegramSection).className = 'auth-hint';

            var emailSection = group('security.emailSection');
            var emailStatus = el('p', '', emailSection);
            emailStatus.className = 'auth-security-info';
            emailStatus.setAttribute('role', 'status');
            emailStatus.setAttribute('aria-live', 'polite');
            var emailRow = el('div', '', emailSection);
            emailRow.className = 'auth-security-email';
            var email = field(emailRow, t('security.emailAddress'), 'email');
            email.value = data.verifiedEmail || '';
            email.addEventListener('input', sync);
            register(email, function () {
                return emailConfigured();
            });
            actionButton(emailSection, 'security.verifyEmail', function () {
                return emailConfigured() && Boolean(email.value.trim());
            }, async function () {
                var proof = await reauthenticate(data);
                await run(
                    'beginEmailBinding',
                    'BeginEmailBindingRequest',
                    { securityProof: proof, email: email.value.trim() },
                    { auth: true }
                );
            });
            if (!emailConfigured())
                el('p', t('security.prerequisite.emailUnavailable'), emailSection).className = 'auth-hint';

            var factorsSection = group('security.factorSection');
            function factorRow(factor, titleKey, enabled, canEnable) {
                var row = el('div', '', factorsSection);
                row.className = 'auth-security-factor';
                var description = el('div', '', row);
                description.className = 'auth-security-factor-copy';
                el('strong', t(titleKey), description);
                var state = el('span', t(enabled ? 'twofa.enabled' : 'twofa.disabled'), description);
                state.className = 'auth-security-factor-state';
                var help = el('small', '', description);
                help.className = 'auth-hint';
                var toggle = actionButton(row, enabled ? 'common.disable' : 'common.enable', function () {
                    if (busy) return false;
                    if (enabled) return !factorRemovalReason(factor);
                    return canEnable();
                }, async function () {
                    if (!enabled) {
                        await setupFactor(factor);
                        return;
                    }
                    var proof = await reauthenticate(data);
                    await BF.confirmations.call(
                        'disableOtpVerification',
                        'DisableOtpVerificationRequest',
                        { otpType: factor, securityProof: proof },
                        options
                    );
                });
                return {
                    enabled: enabled,
                    help: help,
                    setHelp: function (key) {
                        help.textContent = key ? t(key) : '';
                        toggle.disabled = busy || !(enabled ? !factorRemovalReason(factor) : canEnable());
                    }
                };
            }
            var authenticator = factorRow(1, 'security.factor.app', data.authenticatorEnabled, function () {
                return true;
            });
            var emailFactor = factorRow(2, 'twofa.email', data.emailEnabled, function () {
                return emailConfigured() && hasVerifiedEmail();
            });
            var emailFactorHelp = el('p', '', factorsSection);
            emailFactorHelp.className = 'auth-hint';
            emailFactorHelp.setAttribute('role', 'status');
            emailFactorHelp.setAttribute('aria-live', 'polite');

            var recoverySection = group('security.recoveryCodes');
            var recoveryStatus = el('p', '', recoverySection);
            recoveryStatus.className = 'auth-security-info';
            actionButton(recoverySection, 'security.regenerateCodes', function () {
                return true;
            }, async function () {
                var proof = await reauthenticate(data);
                var value = await BF.confirmations.call(
                    'generateRecoveryCodes',
                    'SecurityProofRequest',
                    { securityProof: proof },
                    options
                );
                await codes(value.getCodesList());
            });

            var saveButton = actionButton(root, 'common.save', function () {
                return changed() && modeIsValid(draft, false) &&
                    (draft.telegramEnabled || canReceiveEmailAlerts());
            }, async function () {
                await save(false);
            }, false);

            function refreshPreferred() {
                var choices = availableFactors(draft, false);
                preferred.replaceChildren();
                var placeholder = el('option', t('security.chooseFactor'), preferred);
                placeholder.value = '';
                placeholder.disabled = true;
                choices.forEach(function (factor) {
                    var factorInfo = factors().find(function (item) {
                        return item[0] === factor;
                    });
                    var option = el('option', factorInfo ? factorInfo[1] : '', preferred);
                    option.value = String(factor);
                });
                preferred.value = choices.includes(Number(draft.preferredFactor))
                    ? String(draft.preferredFactor)
                    : '';
            }
            function sync() {
                if (controller.signal.aborted) return;
                modePassword.checked = draft.loginMode === 1;
                modeTelegram.checked = draft.loginMode === 2;
                modeFactor.checked = draft.loginMode === 3;
                var telegramMessage = telegramLoginAvailable(draft)
                    ? ''
                    : t('security.prerequisite.telegramLogin');
                modeTelegramStatus.textContent = telegramMessage;
                modeTelegramStatus.hidden = !telegramMessage;
                preferredLabel.hidden = draft.loginMode !== 3;
                setupFactorAction.hidden =
                    draft.loginMode !== 3 || availableFactors(draft, false).length > 0 || data.authenticatorEnabled;
                refreshPreferred();
                telegramStatus.textContent = data.telegramLinked
                    ? t('security.telegramAccount', {
                          account: data.telegramUsername ? '@' + data.telegramUsername : t('security.linked')
                      })
                    : t('security.notLinked');
                emailStatus.textContent = hasVerifiedEmail()
                    ? t('security.emailVerified', { email: data.verifiedEmail })
                    : t('security.emailNotVerified');
                var message = modePrerequisite();
                modeStatus.textContent = message;
                modeStatus.hidden = !message;
                if (!draft.telegramEnabled && (draft.telegramOtpEnabled || draft.fastAuthTelegramEnabled)) {
                    telegramStatus.textContent += ' ' + t('security.telegramDisabledPending');
                }
                if (telegramLinkAction) {
                    var reason = unlinkReason();
                    telegramLinkAction.unlinkStatus.textContent = reason;
                    telegramLinkAction.unlinkStatus.hidden = !reason;
                }
                recoveryStatus.textContent = t('security.recoveryRemaining', {
                    count: data.remainingRecoveryCodes
                });
                authenticator.setHelp(data.authenticatorEnabled ? factorRemovalReason(1) : '');
                emailFactor.setHelp(data.emailEnabled ? factorRemovalReason(2) : '');
                emailFactorHelp.textContent = !emailConfigured()
                    ? t('security.prerequisite.emailUnavailable')
                    : !hasVerifiedEmail()
                      ? t('security.prerequisite.emailBindForFactor')
                      : '';
                emailFactorHelp.hidden = !emailFactorHelp.textContent;
                controls.forEach(function (control) {
                    control.element.disabled = busy || !control.enabled();
                });
                buttons.forEach(function (item) {
                    item.element.disabled = busy || !item.enabled();
                });
                saveButton.disabled = busy || !changed() || !modeIsValid(draft, false) ||
                    (!draft.telegramEnabled && !canReceiveEmailAlerts());
                root.setAttribute('aria-busy', busy ? 'true' : 'false');
            }
            syncCurrent = sync;
            async function perform(work, preserveDraft) {
                if (busy || controller.signal.aborted) return;
                var draftOverlay = preserveDraft === false ? null : {};
                if (draftOverlay) {
                    ['loginMode', 'preferredFactor', 'telegramEnabled', 'telegramOtpEnabled', 'fastAuthTelegramEnabled']
                        .forEach(function (key) {
                            if (draft[key] !== data[key]) draftOverlay[key] = draft[key];
                        });
                }
                busy = true;
                error.textContent = '';
                sync();
                try {
                    await work();
                    if (!controller.signal.aborted) await load(draftOverlay);
                } catch (err) {
                    if (!controller.signal.aborted && err?.name !== 'AbortError')
                        error.textContent = errorText(err);
                } finally {
                    if (!controller.signal.aborted) {
                        busy = false;
                        if (syncCurrent) syncCurrent();
                    }
                }
            }
            async function save(unlink) {
                var snapshot = {
                    loginMode: draft.loginMode,
                    preferredFactor: draft.preferredFactor,
                    telegramEnabled: unlink ? false : draft.telegramEnabled,
                    telegramOtpEnabled: unlink ? false : draft.telegramOtpEnabled,
                    fastAuthTelegramEnabled: unlink ? false : draft.fastAuthTelegramEnabled
                };
                if (!modeIsValid(snapshot, unlink) || (!snapshot.telegramEnabled && !canReceiveEmailAlerts()))
                    return;
                var proof = await reauthenticate(data);
                if (controller.signal.aborted) return;
                var saved = await BF.confirmations.call(
                    unlink ? 'unlinkTelegram' : 'updateSecuritySettings',
                    'UpdateSecuritySettingsRequest',
                    {
                        securityProof: proof,
                        loginMode: snapshot.loginMode,
                        preferredFactor: snapshot.preferredFactor,
                        telegramEnabled: snapshot.telegramEnabled,
                        telegramOtpEnabled: snapshot.telegramOtpEnabled,
                        fastAuthTelegramEnabled: snapshot.fastAuthTelegramEnabled
                    },
                    options
                );
                try {
                    await codes(saved.getRecoveryCodesList());
                } catch (err) {
                    if (err?.name !== 'AbortError') throw err;
                }
            }
            sync();
        }

        async function load(draftOverlay) {
            var generation = ++renderGeneration;
            try {
                var results = await Promise.all([
                    BF.confirmations.call(
                        'getSecuritySettings',
                        'GetSecuritySettingsRequest',
                        {},
                        Object.assign({ read: true }, options)
                    ),
                    BF.confirmations.call(
                        'getAuthCapabilities',
                        'GetAuthCapabilitiesRequest',
                        {},
                        Object.assign({ read: true }, options)
                    )
                ]);
                if (controller.signal.aborted || generation !== renderGeneration) return;
                data = results[0].toObject();
                capabilities = results[1];
                draft = {
                    loginMode: data.loginMode,
                    preferredFactor: data.preferredFactor,
                    telegramEnabled: data.telegramEnabled,
                    telegramOtpEnabled: data.telegramOtpEnabled,
                    fastAuthTelegramEnabled: data.fastAuthTelegramEnabled
                };
                if (draftOverlay) Object.assign(draft, draftOverlay);
                createForm();
            } catch (err) {
                if (!controller.signal.aborted && generation === renderGeneration) {
                    container.replaceChildren();
                    var error = el('p', errorText(err), container);
                    error.setAttribute('role', 'alert');
                }
            }
        }
        load();
        return cleanup;
    }
    function cancelAll() {
        lifecycle.abort();
        lifecycle = new AbortController();
        Array.from(active).forEach(function (cancel) {
            cancel();
        });
    }
    window.addEventListener('pagehide', cancelAll);
    BF.authUI = {
        run: run,
        reauthenticate: reauthenticate,
        recover: recover,
        codes: codes,
        setupFactor: setupFactor,
        renderSecurity: renderSecurity,
        cancelAll: cancelAll,
        modes: modes,
        factors: factors,
        errorText: errorText
    };
})();
