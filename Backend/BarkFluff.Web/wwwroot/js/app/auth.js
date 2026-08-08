/**
 * Login / 2FA / refresh flows for the login page (index.html).
 * Requires: barkfluff.bundle.js, BF.metadata, BF.tokens
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
     * Perform login.
     * @param {Object} opts — { login: string, password: string, otpCode?: string }
     * @returns {Promise<{ success: boolean, needOtp?: boolean, error?: string, data?: Object }>}
     */
    function login(opts) {
        var proto = window.proto.barkfluff.identity;
        var req = new proto.AuthRequest();

        // oneof login: email if contains @, otherwise username
        if (opts.login.indexOf('@') >= 0) req.setEmail(opts.login);
        else req.setUsername(opts.login);

        req.setPassword(opts.password);
        if (opts.otpCode) req.setOtpCode(opts.otpCode);

        var meta = BF.metadata.build(); // no token

        return new Promise(function (resolve) {
            identityClient.auth(req, meta, function (err, resp) {
                if (err) {
                    var errorCode = err.metadata && err.metadata['x-error-code'];

                    if (errorCode === ERROR_CODES.OTP_REQUIRED) {
                        return resolve({ success: false, needOtp: true });
                    }
                    if (errorCode === ERROR_CODES.INVALID_OTP) {
                        return resolve({ success: false, error: 'invalid_otp' });
                    }
                    if (errorCode === ERROR_CODES.INVALID_CREDENTIALS) {
                        return resolve({ success: false, error: 'invalid_credentials' });
                    }
                    return resolve({ success: false, error: 'server_error' });
                }

                var at = resp.getAccessToken();
                var rt = resp.getRefreshToken();
                if (!at || !rt) {
                    return resolve({ success: false, error: 'server_error' });
                }

                var data = {
                    accessToken: at.getValue(),
                    accessTokenExpiration: at.getExpirationDate().toDate().getTime(),
                    refreshToken: rt.getValue(),
                    refreshTokenExpiration: rt.getExpirationDate().toDate().getTime()
                };

                resolve({ success: true, data: data });
            });
        });
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
        return new Promise(function (resolve) {
            identityClient.createToken(req, meta, function (err, resp) {
                if (err || !resp) {
                    if (isInvalidRefreshTokenError(err)) BF.tokens.clear();
                    return resolve(null);
                }
                var at = resp.getAccessToken();
                if (!at) return resolve(null);

                var stored = BF.tokens.get() || {};
                stored.accessToken = at.getValue();
                stored.accessTokenExpiration = at.getExpirationDate().toDate().getTime();
                BF.tokens.save(stored);
                resolve(at.getValue());
            });
        });
    }

    function getValidAccessToken() {
        if (!BF.tokens.isAccessExpired()) {
            return Promise.resolve(BF.tokens.getAccessToken());
        }
        return refreshToken();
    }

    window.BF.auth = {
        login: login,
        refreshToken: refreshToken,
        getValidAccessToken: getValidAccessToken,
        ERROR_CODES: ERROR_CODES
    };
})();
