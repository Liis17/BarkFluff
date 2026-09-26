/* Shared confirmation UI for login, registration and security settings. */
(function () {
    'use strict';
    var active = new Set();
    var lifecycle = new AbortController();
    function ensureActive(signal) {
        if (signal.aborted) throw new DOMException('Cancelled', 'AbortError');
    }
    var t = function (key) {
        return BF.i18n.t(key);
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
        var confirm = button(box.actions, t('common.confirm'), finish);
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
            origin = BF.node.origin();
        var options = { auth: true, signal: controller.signal, origin: origin };
        var cleanup = function () {
            controller.abort();
            active.delete(cleanup);
        };
        active.add(cleanup);
        async function render() {
            try {
                var response = await BF.confirmations.call(
                    'getSecuritySettings',
                    'GetSecuritySettingsRequest',
                    {},
                    Object.assign({ read: true }, options)
                );
                var capabilities = await BF.confirmations.call(
                    'getAuthCapabilities',
                    'GetAuthCapabilitiesRequest',
                    {},
                    Object.assign({ read: true }, options)
                );
                if (controller.signal.aborted) return;
                var data = response.toObject();
                container.replaceChildren();
                var root = el('div', '', container);
                root.className = 'auth-security';
                var error = el('p', '', root);
                error.setAttribute('role', 'alert');
                var mode = select(root, t('security.mode'), modes(), data.loginMode);
                Array.from(mode.options).forEach(function (option) {
                    if (option.value === '2')
                        option.disabled = !data.telegramLinked || !capabilities.getTelegramAvailable();
                    if (option.value === '3')
                        option.disabled =
                            !data.authenticatorEnabled &&
                            !(data.emailEnabled && capabilities.getEmailAvailable()) &&
                            !(
                                data.telegramLinked &&
                                data.telegramEnabled &&
                                data.telegramOtpEnabled &&
                                capabilities.getTelegramAvailable()
                            );
                });
                var preferred = select(
                    root,
                    t('security.factor'),
                    factors()
                        .slice(1)
                        .filter(function (f) {
                            return (
                                (f[0] === 1 && data.authenticatorEnabled) ||
                                (f[0] === 2 && data.emailEnabled && capabilities.getEmailAvailable()) ||
                                (f[0] === 3 &&
                                    data.telegramLinked &&
                                    data.telegramEnabled &&
                                    data.telegramOtpEnabled &&
                                    capabilities.getTelegramAvailable())
                            );
                        }),
                    data.preferredFactor
                );
                el(
                    'p',
                    data.telegramLinked
                        ? 'Telegram: ' + (data.telegramUsername ? '@' + data.telegramUsername : t('security.linked'))
                        : t('security.notLinked'),
                    root
                );
                var telegram = field(root, t('security.telegramEnabled'), 'checkbox');
                telegram.checked = data.telegramEnabled;
                telegram.disabled = !data.telegramLinked;
                var otp = field(root, t('security.telegramFactor'), 'checkbox');
                otp.checked = data.telegramOtpEnabled;
                var fast = field(root, t('security.fastAuthTelegram'), 'checkbox');
                fast.checked = data.fastAuthTelegramEnabled;
                otp.disabled = fast.disabled = !data.telegramLinked;
                async function action(work) {
                    error.textContent = '';
                    root.querySelectorAll('button').forEach(function (btn) {
                        btn.disabled = true;
                    });
                    try {
                        await work();
                        if (!controller.signal.aborted) await render();
                    } catch (err) {
                        if (!controller.signal.aborted) {
                            error.textContent = errorText(err);
                            root.querySelectorAll('button').forEach(function (btn) {
                                btn.disabled = false;
                            });
                        }
                    }
                }
                async function save(unlink) {
                    var proof = await reauthenticate(data);
                    var saved = await BF.confirmations.call(
                        unlink ? 'unlinkTelegram' : 'updateSecuritySettings',
                        'UpdateSecuritySettingsRequest',
                        {
                            securityProof: proof,
                            loginMode: Number(mode.value),
                            preferredFactor: Number(preferred.value),
                            telegramEnabled: !unlink && telegram.checked,
                            telegramOtpEnabled: !unlink && otp.checked,
                            fastAuthTelegramEnabled: !unlink && fast.checked
                        },
                        options
                    );
                    await codes(saved.getRecoveryCodesList());
                }
                button(root, t('common.save'), function () {
                    action(function () {
                        return save(false);
                    });
                });
                if (data.telegramLinked)
                    button(root, t('security.unlinkTelegram'), function () {
                        action(function () {
                            return save(true);
                        });
                    });
                else if (capabilities.getTelegramAvailable())
                    button(root, t('security.linkTelegram'), function () {
                        action(async function () {
                            var proof = await reauthenticate(data);
                            await run(
                                'beginTelegramBinding',
                                'SecurityProofRequest',
                                { securityProof: proof },
                                { auth: true }
                            );
                        });
                    });
                el('p', t('security.disableHint'), root).className = 'auth-hint';
                var email = field(root, t('twofa.email'), 'email');
                email.value = data.verifiedEmail || '';
                if (capabilities.getEmailAvailable())
                    button(root, t('security.verifyEmail'), function () {
                        action(async function () {
                            var proof = await reauthenticate(data);
                            await run(
                                'beginEmailBinding',
                                'BeginEmailBindingRequest',
                                {
                                    securityProof: proof,
                                    email: email.value.trim()
                                },
                                { auth: true }
                            );
                        });
                    });
                [
                    [1, t('security.factor.app'), data.authenticatorEnabled],
                    [2, t('twofa.email'), data.emailEnabled]
                ].forEach(function (item) {
                    var row = el('div', '', root);
                    row.className = 'auth-factor';
                    el('span', item[1] + ' · ' + t(item[2] ? 'twofa.enabled' : 'twofa.disabled'), row);
                    button(row, t(item[2] ? 'common.disable' : 'common.enable'), function () {
                        action(async function () {
                            if (!item[2]) {
                                await setupFactor(item[0]);
                                return;
                            }
                            var proof = await reauthenticate(data);
                            await BF.confirmations.call(
                                'disableOtpVerification',
                                'DisableOtpVerificationRequest',
                                { otpType: item[0], securityProof: proof },
                                options
                            );
                        });
                    });
                });
                el('p', t('security.recoveryCodes') + ': ' + data.remainingRecoveryCodes, root);
                button(root, t('security.regenerateCodes'), function () {
                    action(async function () {
                        var proof = await reauthenticate(data);
                        var value = await BF.confirmations.call(
                            'generateRecoveryCodes',
                            'SecurityProofRequest',
                            { securityProof: proof },
                            options
                        );
                        await codes(value.getCodesList());
                    });
                });
            } catch (error) {
                if (!controller.signal.aborted) container.textContent = errorText(error);
            }
        }
        render();
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
