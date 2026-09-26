/* Shared Identity transport and cancellable confirmation attempts. No tokens are saved here. */
(function () {
    'use strict';
    window.BF = window.BF || {};
    function aborted() {
        return new DOMException('Confirmation cancelled', 'AbortError');
    }
    function request(type, fields) {
        var value = new window.proto.barkfluff.identity[type]();
        Object.keys(fields || {}).forEach(function (key) {
            value['set' + key[0].toUpperCase() + key.slice(1)](fields[key]);
        });
        return value;
    }
    async function call(method, type, fields, options) {
        options = options || {};
        var origin = options.origin || BF.node.origin();
        if (!origin) throw aborted();
        var client = new window.barkfluff.IdentityApiClient(origin);
        var policy = options.read ? BF.network.POLICIES.READ : BF.network.POLICIES.MUTATION;
        if (options.signal) policy = BF.network.withSignal(policy, options.signal);
        var token = options.auth
            ? BF.clients
                ? await BF.clients.getValidToken()
                : await BF.auth.getValidAccessToken()
            : null;
        if (options.signal?.aborted || BF.node.origin() !== origin) throw aborted();
        var result = await BF.network.unary(
            client[method].bind(client),
            typeof type === 'string' ? request(type, fields) : type,
            BF.metadata.build(token),
            policy
        );
        if (options.signal?.aborted || BF.node.origin() !== origin) throw aborted();
        return result;
    }
    function create(options) {
        options = options || {};
        var origin = BF.node.origin(),
            generation = 0,
            controller = null,
            timer = null,
            reference = null;
        function valid(version) {
            return version === generation && controller && !controller.signal.aborted && BF.node.origin() === origin;
        }
        function cancel() {
            generation++;
            clearTimeout(timer);
            if (controller) controller.abort();
            if (reference) {
                // Cancellation is public and uses the old node's browser secret, never a new node's token.
                var client = new window.barkfluff.IdentityApiClient(origin);
                BF.network
                    .unary(
                        client.cancelAuthChallenge.bind(client),
                        reference,
                        BF.metadata.build(),
                        BF.network.POLICIES.MUTATION
                    )
                    .catch(function () {});
            }
            reference = null;
        }
        async function begin(method, type, fields) {
            cancel();
            controller = new AbortController();
            var version = generation;
            var response = await call(method, type, fields, {
                origin: origin,
                auth: options.auth,
                signal: controller.signal
            });
            if (!valid(version)) throw aborted();
            reference = response.getChallenge();
            return response;
        }
        async function invoke(method, type, fields, read) {
            var version = generation;
            if (!valid(version) || !reference) throw aborted();
            var response = await call(method, type || reference, fields, {
                origin: origin,
                auth: options.auth,
                signal: controller.signal,
                read: read
            });
            if (!valid(version)) throw aborted();
            return response;
        }
        function poll(onState, onError) {
            var version = generation;
            clearTimeout(timer);
            timer = setTimeout(async function tick() {
                if (!valid(version)) return;
                try {
                    var response = await invoke('getAuthChallenge', null, null, true);
                    if (!valid(version)) return;
                    onState(response);
                    if (response.getState() === 1 && valid(version)) timer = setTimeout(tick, 2000);
                } catch (error) {
                    if (valid(version)) {
                        onError(error);
                        timer = setTimeout(tick, 2000);
                    }
                }
            }, 2000);
        }
        return {
            begin: begin,
            cancel: cancel,
            poll: poll,
            origin: origin,
            valid: function () {
                return valid(generation);
            },
            complete: function (code, recovery) {
                return invoke('completeAuthChallenge', 'CompleteAuthChallengeRequest', {
                    challenge: reference,
                    code: code || '',
                    useRecoveryCode: !!recovery
                });
            },
            resend: function () {
                return invoke('resendAuthChallenge');
            }
        };
    }
    function session(response) {
        var at = response.getAccessToken(),
            rt = response.getRefreshToken();
        return {
            accessToken: at.getValue(),
            accessTokenExpiration: at.getExpirationDate().toDate().getTime(),
            refreshToken: rt.getValue(),
            refreshTokenExpiration: rt.getExpirationDate().toDate().getTime()
        };
    }
    BF.confirmations = { call: call, create: create, session: session };
})();
