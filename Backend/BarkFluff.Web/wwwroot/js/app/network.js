/**
 * Bounded callback-style gRPC-Web unary transport.
 * Requires: none
 * Exposes: BF.network
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var POLICIES = {
        READ: {
            attemptTimeoutMs: 12000,
            overallTimeoutMs: 35000,
            maxAttempts: 3,
            retryCodes: [2, 4, 14],
            retryTransport: true,
            baseDelayMs: 250,
            maxDelayMs: 2000,
            outcomeUnknown: false
        },
        REFRESH: {
            attemptTimeoutMs: 10000,
            overallTimeoutMs: 22000,
            maxAttempts: 2,
            retryCodes: [14],
            retryTransport: true,
            baseDelayMs: 250,
            maxDelayMs: 1000,
            outcomeUnknown: false
        },
        DRAFT: {
            attemptTimeoutMs: 8000,
            overallTimeoutMs: 26000,
            maxAttempts: 3,
            retryCodes: [2, 4, 14],
            retryTransport: true,
            baseDelayMs: 250,
            maxDelayMs: 2000,
            outcomeUnknown: false
        },
        MUTATION: {
            attemptTimeoutMs: 15000,
            overallTimeoutMs: 15000,
            maxAttempts: 1,
            retryCodes: [],
            retryTransport: false,
            baseDelayMs: 0,
            maxDelayMs: 0,
            outcomeUnknown: true
        }
    };

    function copyPolicy(policy) {
        var source = policy || POLICIES.MUTATION;
        return {
            attemptTimeoutMs: source.attemptTimeoutMs || POLICIES.MUTATION.attemptTimeoutMs,
            overallTimeoutMs: source.overallTimeoutMs || source.attemptTimeoutMs || POLICIES.MUTATION.overallTimeoutMs,
            maxAttempts: Math.max(1, source.maxAttempts || 1),
            retryCodes: Array.isArray(source.retryCodes) ? source.retryCodes.slice() : [],
            retryTransport: source.retryTransport === true,
            baseDelayMs: Math.max(0, source.baseDelayMs || 0),
            maxDelayMs: Math.max(0, source.maxDelayMs || 0),
            outcomeUnknown: source.outcomeUnknown === true,
            signal: source.signal || null
        };
    }

    function networkError(kind, code, message, policy, original) {
        var error = new Error(message || kind);
        error.name = 'NetworkError';
        error.kind = kind;
        error.code = code == null ? null : code;
        error.retryable = isRetryable(original || error, policy);
        error.outcomeUnknown = policy.outcomeUnknown === true && kind !== 'cancelled';
        error.original = original || null;
        error.metadata = original && original.metadata ? original.metadata : null;
        error.errorCode = error.metadata && error.metadata['x-error-code'] ? error.metadata['x-error-code'] : null;
        return error;
    }

    function errorCode(error) {
        return error && typeof error.code === 'number' ? error.code : null;
    }

    function isRetryable(error, policy) {
        var code = errorCode(error);
        if (code != null) return policy.retryCodes.indexOf(code) !== -1;
        return policy.retryTransport === true;
    }

    function normalize(error, policy) {
        if (error && error.name === 'NetworkError') return error;
        var code = errorCode(error);
        var kind = code === 4 ? 'timeout' : code == null ? 'transport' : 'grpc';
        return networkError(kind, code, error && error.message, policy, error);
    }

    function withSignal(policy, signal) {
        var copy = {};
        Object.keys(policy || {}).forEach(function (key) {
            copy[key] = policy[key];
        });
        copy.signal = signal;
        return copy;
    }

    function unary(method, request, metadata, inputPolicy) {
        var policy = copyPolicy(inputPolicy);
        var startedAt = Date.now();
        var overallDeadline = startedAt + policy.overallTimeoutMs;
        var attempts = 0;
        var activeCall = null;
        var attemptTimer = null;
        var retryTimer = null;
        var settled = false;

        return new Promise(function (resolve, reject) {
            function cleanup() {
                if (attemptTimer) clearTimeout(attemptTimer);
                if (retryTimer) clearTimeout(retryTimer);
                attemptTimer = null;
                retryTimer = null;
                if (policy.signal) policy.signal.removeEventListener('abort', onAbort);
            }

            function settleResolve(response) {
                if (settled) return;
                settled = true;
                cleanup();
                activeCall = null;
                resolve(response);
            }

            function settleReject(error, cancelCall) {
                if (settled) return;
                settled = true;
                cleanup();
                var call = activeCall;
                activeCall = null;
                reject(error);
                if (cancelCall && call && typeof call.cancel === 'function') call.cancel();
            }

            function onAbort() {
                settleReject(networkError('cancelled', 1, 'cancelled', policy), true);
            }

            function scheduleRetry(error) {
                if (attempts >= policy.maxAttempts || !isRetryable(error, policy)) return false;
                var cap = Math.min(policy.maxDelayMs, policy.baseDelayMs * Math.pow(2, attempts - 1));
                var delay = cap > 0 ? Math.floor(Math.random() * (cap + 1)) : 0;
                if (Date.now() + delay >= overallDeadline) return false;
                retryTimer = setTimeout(runAttempt, delay);
                return true;
            }

            function failAttempt(error, call, cancelCall) {
                if (settled) return;
                if (attemptTimer) clearTimeout(attemptTimer);
                attemptTimer = null;
                if (activeCall === call) activeCall = null;
                var normalized = normalize(error, policy);
                if (scheduleRetry(normalized)) {
                    if (cancelCall && call && typeof call.cancel === 'function') call.cancel();
                    return;
                }
                activeCall = call;
                settleReject(normalized, cancelCall);
            }

            function runAttempt() {
                retryTimer = null;
                if (settled) return;
                if (policy.signal && policy.signal.aborted) {
                    onAbort();
                    return;
                }

                var now = Date.now();
                if (now >= overallDeadline) {
                    settleReject(networkError('timeout', 4, 'deadline exceeded', policy), false);
                    return;
                }

                attempts += 1;
                var attemptSettled = false;
                var attemptDeadline = Math.min(now + policy.attemptTimeoutMs, overallDeadline);
                var callMetadata = {};
                Object.keys(metadata || {}).forEach(function (key) {
                    callMetadata[key] = metadata[key];
                });
                callMetadata.deadline = String(attemptDeadline);

                var call = null;
                attemptTimer = setTimeout(
                    function () {
                        if (attemptSettled || settled) return;
                        attemptSettled = true;
                        failAttempt(networkError('timeout', 4, 'deadline exceeded', policy), call, true);
                    },
                    Math.max(1, attemptDeadline - Date.now())
                );

                try {
                    call = method(request, callMetadata, function (error, response) {
                        if (attemptSettled || settled) return;
                        attemptSettled = true;
                        if (error) failAttempt(error, call, false);
                        else {
                            if (attemptTimer) clearTimeout(attemptTimer);
                            attemptTimer = null;
                            settleResolve(response);
                        }
                    });
                    if (!attemptSettled) activeCall = call;
                } catch (error) {
                    if (attemptSettled || settled) return;
                    attemptSettled = true;
                    failAttempt(error, call, false);
                }
            }

            if (policy.signal) policy.signal.addEventListener('abort', onAbort, { once: true });
            runAttempt();
        });
    }

    window.BF.network = {
        unary: unary,
        withSignal: withSignal,
        POLICIES: POLICIES
    };
})();
