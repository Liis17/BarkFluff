/**
 * gRPC-Web client instances + authCall wrapper with auto token-refresh.
 * Requires: barkfluff.bundle.js loaded (window.barkfluff), BF.tokens, BF.metadata
 * Exposes: BF.clients
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var bf = window.barkfluff;
    var origin = window.location.origin;

    // gRPC-Web callback-style clients (needed for server-streaming)
    var identityClient = new bf.IdentityApiClient(origin);
    var messagesClient = new bf.MessagesApiClient(origin);
    var usersClient = new bf.UsersApiClient(origin);
    var filesClient = new bf.FilesApiClient(origin);
    var updatesClient = new bf.UpdatesApiClient(origin);
    var onlinerClient = new bf.OnlinerApiClient(origin);
    var fastAuthClient = new bf.FastAuthApiClient(origin);

    // Known error codes from x-error-code trailer
    var ERROR_CODES = {
        OTP_REQUIRED: 'C1576884-12D8-4722-A7EE-9F9789AD1265',
        INVALID_OTP: '803B632C-4457-4B05-9435-9C3DD0F41E00',
        INVALID_CREDENTIALS: '21BFB9B5-C377-45D1-9B15-6B7F3432B397'
    };

    var refreshPromise = null;

    /**
     * Refresh the access token using the stored refresh token.
     * Returns new access token or null.
     */
    function refreshToken() {
        if (refreshPromise) return refreshPromise;

        var rt = BF.tokens.getRefreshToken();
        if (!rt) return Promise.resolve(null);

        var p = new Promise(function (resolve) {
            var proto = window.proto.barkfluff.identity;
            var req = new proto.CreateTokenRequest();
            req.setRefreshToken(rt);

            var meta = BF.metadata.build();
            identityClient.createToken(req, meta, function (err, resp) {
                if (err || !resp) {
                    BF.tokens.clear();
                    resolve(null);
                    return;
                }
                var at = resp.getAccessToken();
                if (!at) { resolve(null); return; }

                var stored = BF.tokens.get() || {};
                stored.accessToken = at.getValue();
                stored.accessTokenExpiration = at.getExpirationDate().toDate().getTime();
                BF.tokens.save(stored);
                resolve(at.getValue());
            });
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
    function authCall(method, request) {
        return getValidToken().then(function (token) {
            if (!token) {
                window.location.href = '/';
                return Promise.reject(new Error('no_token'));
            }
            return callWithToken(method, request, token, false);
        });
    }

    function callWithToken(method, request, token, isRetry) {
        var meta = BF.metadata.build(token);
        return new Promise(function (resolve, reject) {
            method(request, meta, function (err, resp) {
                if (err) {
                    // On UNAUTHENTICATED, try refresh once
                    if (err.code === 16 && !isRetry) {
                        refreshToken().then(function (newToken) {
                            if (!newToken) {
                                window.location.href = '/';
                                return reject(err);
                            }
                            callWithToken(method, request, newToken, true).then(resolve, reject);
                        });
                        return;
                    }
                    // Attach parsed error code
                    var errorCode = err.metadata && err.metadata['x-error-code'];
                    err.errorCode = errorCode || null;
                    return reject(err);
                }
                resolve(resp);
            });
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
        authCall: authCall,
        getValidToken: getValidToken,
        refreshToken: refreshToken,
        ERROR_CODES: ERROR_CODES
    };
})();
