/**
 * E2E приватных чатов: Argon2id(passphrase, kdf_salt) → AES-256-GCM (WebCrypto).
 * Зеркалит Android PrivateChatCrypto.kt / PrivateChatRepository.kt:
 *  - Argon2id: t=3, m=64MiB, p=4, ключ 32 байта (hash-wasm, WASM);
 *  - verifier: HMAC-SHA256(key, "BARKFLUFF_PRIVATE_CHAT_VERIFIER");
 *  - AES-256-GCM: nonce 12 байт, tag 128 бит, AAD = "barkfluff:private:{chatId}".
 * Ключи (не passphrase) хранятся в localStorage (base64) + in-memory кэш.
 * Requires: hash-wasm (window.hashwasm)
 * Exposes: BF.privateChat
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var ARGON2_ITERATIONS = 3;
    var ARGON2_MEMORY_KIB = 64 * 1024;
    var ARGON2_PARALLELISM = 4;
    var KEY_BYTES = 32;
    var NONCE_BYTES = 12;
    var GCM_TAG_BITS = 128;
    var VERIFIER_CONSTANT = 'BARKFLUFF_PRIVATE_CHAT_VERIFIER';
    var STORAGE_KEY = 'bf_private_chat_keys';

    var enc = new TextEncoder();
    var dec = new TextDecoder();

    // In-memory кэши: сырой ключ (для «не запоминать») и импортированный CryptoKey
    var keyCache = new Map();        // chatId -> Uint8Array
    var cryptoKeyCache = new Map();  // chatId -> Promise<CryptoKey>

    // --- base64 helpers ---

    function toBase64(bytes) {
        var s = '';
        for (var i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
        return btoa(s);
    }

    function fromBase64(str) {
        var bin = atob(str);
        var out = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
        return out;
    }

    // --- Хранение ключей ---

    function readStore() {
        try { return JSON.parse(localStorage.getItem(STORAGE_KEY)) || {}; }
        catch (e) { return {}; }
    }

    function writeStore(store) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(store)); } catch (e) {}
    }

    /** Сохранить ключ чата. remember=true → localStorage, иначе только память (до перезагрузки). */
    function saveKey(chatId, key, remember) {
        keyCache.set(chatId, key);
        cryptoKeyCache.delete(chatId);
        if (remember) {
            var store = readStore();
            store[chatId] = toBase64(key);
            writeStore(store);
        }
    }

    /** Загрузить ключ чата (память → localStorage). null, если пароля ещё не вводили. */
    function loadKey(chatId) {
        if (keyCache.has(chatId)) return keyCache.get(chatId);
        var encoded = readStore()[chatId];
        if (!encoded) return null;
        var key = fromBase64(encoded);
        keyCache.set(chatId, key);
        return key;
    }

    function hasKey(chatId) {
        return keyCache.has(chatId) || !!readStore()[chatId];
    }

    function forgetKey(chatId) {
        keyCache.delete(chatId);
        cryptoKeyCache.delete(chatId);
        var store = readStore();
        if (store[chatId]) { delete store[chatId]; writeStore(store); }
    }

    // --- KDF + verifier ---

    /** Криптостойкий salt для Argon2id (создание чата). */
    function generateSalt() {
        return crypto.getRandomValues(new Uint8Array(32));
    }

    /** Argon2id(passphrase, salt) → Uint8Array(32). Тяжёлая операция (~1с). */
    function deriveKey(passphrase, salt) {
        return window.hashwasm.argon2id({
            password: passphrase,
            salt: salt,
            iterations: ARGON2_ITERATIONS,
            memorySize: ARGON2_MEMORY_KIB,
            parallelism: ARGON2_PARALLELISM,
            hashLength: KEY_BYTES,
            outputType: 'binary'
        });
    }

    function computeVerifier(key) {
        return crypto.subtle.importKey('raw', key, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign'])
            .then(function (hmacKey) {
                return crypto.subtle.sign('HMAC', hmacKey, enc.encode(VERIFIER_CONSTANT));
            })
            .then(function (sig) { return new Uint8Array(sig); });
    }

    /** Проверить passphrase-ключ против verifier'а чата (constant-time). */
    function validateVerifier(key, expectedVerifier) {
        return computeVerifier(key).then(function (actual) {
            if (actual.length !== expectedVerifier.length) return false;
            var diff = 0;
            for (var i = 0; i < actual.length; i++) diff |= actual[i] ^ expectedVerifier[i];
            return diff === 0;
        });
    }

    // --- AES-256-GCM ---

    function aad(chatId) {
        return enc.encode('barkfluff:private:' + chatId);
    }

    function importAesKey(chatId, key) {
        var cached = cryptoKeyCache.get(chatId);
        if (cached) return cached;
        var p = crypto.subtle.importKey('raw', key, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
        cryptoKeyCache.set(chatId, p);
        return p;
    }

    /** Шифрование текста. Ключ чата должен быть сохранён. → { ciphertext, nonce, associatedData } */
    function encryptText(chatId, text) {
        var key = loadKey(chatId);
        if (!key) return Promise.reject(new Error('no_key'));
        var nonce = crypto.getRandomValues(new Uint8Array(NONCE_BYTES));
        var ad = aad(chatId);
        return importAesKey(chatId, key).then(function (aesKey) {
            return crypto.subtle.encrypt(
                { name: 'AES-GCM', iv: nonce, additionalData: ad, tagLength: GCM_TAG_BITS },
                aesKey,
                enc.encode(text)
            );
        }).then(function (ct) {
            return { ciphertext: new Uint8Array(ct), nonce: nonce, associatedData: ad };
        });
    }

    /**
     * Расшифровать EncryptedMessage (mapped из BF.api). → Promise<string|null>:
     * null — нет ключа, сообщение удалено или расшифровка не удалась.
     */
    function decryptMessage(chatId, msg) {
        if (msg.isDeleted) return Promise.resolve(null);
        var key = loadKey(chatId);
        if (!key) return Promise.resolve(null);
        var ad = (msg.associatedData && msg.associatedData.length > 0) ? msg.associatedData : aad(chatId);
        return importAesKey(chatId, key).then(function (aesKey) {
            return crypto.subtle.decrypt(
                { name: 'AES-GCM', iv: msg.nonce, additionalData: ad, tagLength: GCM_TAG_BITS },
                aesKey,
                msg.ciphertext
            );
        }).then(function (pt) {
            return dec.decode(pt);
        }).catch(function () {
            console.warn('[privateChat] failed to decrypt message', msg.id, 'in', chatId);
            return null;
        });
    }

    window.BF.privateChat = {
        generateSalt: generateSalt,
        deriveKey: deriveKey,
        computeVerifier: computeVerifier,
        validateVerifier: validateVerifier,
        saveKey: saveKey,
        loadKey: loadKey,
        hasKey: hasKey,
        forgetKey: forgetKey,
        encryptText: encryptText,
        decryptMessage: decryptMessage
    };
})();
