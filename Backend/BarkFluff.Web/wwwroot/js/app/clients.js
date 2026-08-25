/**
 * gRPC-Web client instances + authCall wrapper with auto token-refresh.
 * Requires: barkfluff.bundle.js loaded (window.barkfluff), BF.tokens, BF.metadata, BF.network
 * Exposes: BF.clients
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var bf = window.barkfluff;
    // Origin выбранной ноды. Клиенты создаются синхронно ниже, поэтому адрес
    // обязан быть известен уже сейчас — сети здесь быть не может.
    var origin = BF.node.origin();
    if (!origin) {
        window.location.href = '/';
        return;
    }

    // gRPC-Web callback-style clients (needed for server-streaming)
    var identityClient = new bf.IdentityApiClient(origin);
    var messagesClient = new bf.MessagesApiClient(origin);
    var usersClient = new bf.UsersApiClient(origin);
    var filesClient = new bf.FilesApiClient(origin);
    var updatesClient = new bf.UpdatesApiClient(origin);
    var onlinerClient = new bf.OnlinerApiClient(origin);
    var fastAuthClient = new bf.FastAuthApiClient(origin);
    var callsClient = new bf.CallsApiClient(origin);

    // Known error codes from x-error-code trailer
    var ERROR_CODES = {
        OTP_REQUIRED: 'C1576884-12D8-4722-A7EE-9F9789AD1265',
        INVALID_OTP: '803B632C-4457-4B05-9435-9C3DD0F41E00',
        INVALID_CREDENTIALS: '21BFB9B5-C377-45D1-9B15-6B7F3432B397',
        INVALID_REFRESH_TOKEN: '7E6A31C5-3C4D-412E-87BC-0A387617A5D3'
    };

    var refreshPromise = null;

    function isInvalidRefreshTokenError(err) {
        var errorCode = err && err.metadata && err.metadata['x-error-code'];
        return errorCode === ERROR_CODES.INVALID_REFRESH_TOKEN;
    }

    /**
     * Refresh the access token using the stored refresh token.
     * Returns new access token or null.
     */
    function refreshToken() {
        if (refreshPromise) return refreshPromise;

        var rt = BF.tokens.getRefreshToken();
        if (!rt) return Promise.resolve(null);

        var proto = window.proto.barkfluff.identity;
        var req = new proto.CreateTokenRequest();
        req.setRefreshToken(rt);

        var p = BF.network.unary(
            identityClient.createToken.bind(identityClient),
            req,
            BF.metadata.build(),
            BF.network.POLICIES.REFRESH
        ).then(function (resp) {
            if (!resp) return null;
            var at = resp.getAccessToken();
            if (!at) return null;

            var stored = BF.tokens.get() || {};
            stored.accessToken = at.getValue();
            stored.accessTokenExpiration = at.getExpirationDate().toDate().getTime();
            BF.tokens.save(stored);
            return at.getValue();
        }).catch(function (err) {
            // Сетевая ошибка не означает, что refresh token недействителен.
            // Очищаем сессию только по явному ответу Identity.
            if (isInvalidRefreshTokenError(err)) BF.tokens.clear();
            return null;
        });

        refreshPromise = p.finally(function () { refreshPromise = null; });
        return refreshPromise;
    }

    /**
     * Get a valid access token, refreshing if expired.
     */
    function getValidToken() {
        if (!BF.tokens.isAccessExpired()) {
            return Promise.resolve(BF.tokens.getAccessToken());
        }
        return refreshToken();
    }

    /**
     * Make an authorized unary gRPC-Web call with auto refresh + retry.
     * @param {Function} method — bound client method, e.g. messagesClient.listChats.bind(messagesClient)
     * @param {Object} request — protobuf request message
     * @returns {Promise<Object>} — protobuf response message
     */
    function authCall(method, request, policy) {
        return getValidToken().then(function (token) {
            if (!token) {
                if (!BF.tokens.getRefreshToken()) {
                    window.location.href = '/';
                    return Promise.reject(new Error('no_token'));
                }
                return Promise.reject(new Error('token_refresh_unavailable'));
            }
            return callWithToken(method, request, token, false, policy || BF.network.POLICIES.MUTATION);
        });
    }

    function callWithToken(method, request, token, isRetry, policy) {
        var meta = BF.metadata.build(token);
        return BF.network.unary(method, request, meta, policy).catch(function (err) {
            // UNAUTHENTICATED означает, что мутация не была авторизована: после refresh
            // её можно один раз безопасно отправить повторно.
            if (err.code === 16 && !isRetry) {
                return refreshToken().then(function (newToken) {
                    if (!newToken) {
                        if (!BF.tokens.getRefreshToken()) window.location.href = '/';
                        throw err;
                    }
                    return callWithToken(method, request, newToken, true, policy);
                });
            }
            if (!err.errorCode) {
                var errorCode = err.metadata && err.metadata['x-error-code'];
                err.errorCode = errorCode || null;
            }
            throw err;
        });
    }

    window.BF.clients = {
        identity: identityClient,
        messages: messagesClient,
        users: usersClient,
        files: filesClient,
        updates: updatesClient,
        onliner: onlinerClient,
        fastAuth: fastAuthClient,
        calls: callsClient,
        authCall: authCall,
        getValidToken: getValidToken,
        refreshToken: refreshToken,
        ERROR_CODES: ERROR_CODES
    };
})();
