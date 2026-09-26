/**
 * Login / 2FA / refresh flows for the login page (index.html).
 * Requires: barkfluff.bundle.js, BF.metadata, BF.tokens, BF.network
 * Exposes: BF.auth
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var bf = window.barkfluff;

    // Use a dedicated client without auth interceptor — login is unauthenticated.
    // Создаётся лениво: на шелле нода выбирается на этой же странице, до выбора
    // адреса ещё нет.
    var cache = { origin: null, client: null };
    function client() {
        var origin = BF.node.origin();
        if (cache.origin !== origin) cache = { origin: origin, client: new bf.IdentityApiClient(origin) };
        return cache.client;
    }
    var identityClient = {
        auth: function () { var c = client(); return c.auth.apply(c, arguments); },
        createToken: function () { var c = client(); return c.createToken.apply(c, arguments); }
    };

    var ERROR_CODES = {
        OTP_REQUIRED: 'C1576884-12D8-4722-A7EE-9F9789AD1265',
        INVALID_OTP: '803B632C-4457-4B05-9435-9C3DD0F41E00',
        INVALID_CREDENTIALS: '21BFB9B5-C377-45D1-9B15-6B7F3432B397',
        INVALID_REFRESH_TOKEN: '7E6A31C5-3C4D-412E-87BC-0A387617A5D3'
    };

    function isInvalidRefreshTokenError(err) {
        var errorCode = err && err.metadata && err.metadata['x-error-code'];
        return errorCode === ERROR_CODES.INVALID_REFRESH_TOKEN;
    }

    /**
     * Refresh access token using stored refresh token.
     * @returns {Promise<string|null>} — new access token or null
     */
    function refreshToken() {
        var rt = BF.tokens.getRefreshToken();
        if (!rt) return Promise.resolve(null);

        var proto = window.proto.barkfluff.identity;
        var req = new proto.CreateTokenRequest();
        req.setRefreshToken(rt);

        var meta = BF.metadata.build();
        return BF.network.unary(
            identityClient.createToken.bind(identityClient),
            req,
            meta,
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
                if (isInvalidRefreshTokenError(err)) BF.tokens.clear();
                return null;
            });
    }

    function getValidAccessToken() {
        if (!BF.tokens.isAccessExpired()) {
            return Promise.resolve(BF.tokens.getAccessToken());
        }
        return refreshToken();
    }

    window.BF.auth = {
        refreshToken: refreshToken,
        getValidAccessToken: getValidAccessToken,
        ERROR_CODES: ERROR_CODES
    };
})();
