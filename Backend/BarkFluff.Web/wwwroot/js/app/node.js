/**
 * Node (server) selection — какой ноде BarkFluff принадлежит эта сессия браузера.
 *
 * Загружается ДО clients.js / register.js: gRPC-Web клиенты создаются синхронно
 * при загрузке скрипта и захватывают origin, поэтому origin ноды обязан
 * резолвиться синхронно, без сети.
 *
 * Режим хоста приходит из /node-config.js (window.BF_NODE_CONFIG):
 *   pinned: true  — страницу отдала сама нода, работаем только с ней;
 *   pinned: false — глобальный шелл, ноду выбирает пользователь.
 *   proxied: true — прокси-зеркало ноды: gRPC и медиа ходят через этот же
 *                  хост, файловые ссылки оборачиваются в /media/-relay.
 *
 * Exposes: BF.node
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var ORIGIN_KEY = 'bf_node_origin';
    var META_KEY = 'bf_node_meta';
    var LIST_KEY = 'bf_node_list';
    var MIGRATED_KEY = 'bf_node_migrated';

    // Ключи, которые до появления мультиноды лежали без префикса.
    var LEGACY_KEYS = ['barkfluff_auth', 'barkfluff_temp', 'bf_private_chat_keys', 'bf_web_push_enabled'];
    var LEGACY_PREFIXES = ['bf_chat_drafts_'];

    var config = window.BF_NODE_CONFIG || {};
    var pinned = config.pinned !== false;
    var proxied = config.proxied === true;

    /**
     * Приводит пользовательский ввод к origin ('https://gw.example').
     * @returns {string|null} null, если адрес не разбирается
     */
    function normalize(value) {
        if (!value) return null;
        var raw = String(value).trim();
        if (!raw) return null;
        if (!/^https?:\/\//i.test(raw)) raw = 'https://' + raw;
        try {
            var url = new URL(raw);
            if (url.protocol !== 'http:' && url.protocol !== 'https:') return null;
            return url.origin;
        } catch (e) {
            return null;
        }
    }

    // Нода, зафиксированная хостом, авторитетна: страница ноды не должна
    // говорить с чужой нодой, даже если в localStorage что-то осталось.
    var current = pinned ? window.location.origin : normalize(localStorage.getItem(ORIGIN_KEY));

    function origin() {
        return current;
    }

    function id() {
        return current || '';
    }

    /** Ключ хранилища, привязанный к текущей ноде. */
    function key(name) {
        return name + '@' + id();
    }

    /**
     * Переносит домультинодовые ключи под неймспейс ноды, которая их и записала
     * (это всегда origin текущей страницы). Благодаря этому обновление ноды,
     * оставшейся на своём домене, не разлогинивает пользователей.
     *
     * Смену роли домена это не покрывает: если адрес был нодой, а стал шеллом,
     * старый ключ достаётся шеллу, а нода переезжает на другой origin — сопоставить
     * их нечем, и на таком домене вход потребуется заново.
     */
    function migrateLegacy() {
        if (localStorage.getItem(MIGRATED_KEY) === '1') return;
        var suffix = '@' + window.location.origin;

        LEGACY_KEYS.forEach(function (name) {
            [localStorage, sessionStorage].forEach(function (store) {
                var value = store.getItem(name);
                if (value === null) return;
                if (store.getItem(name + suffix) === null) store.setItem(name + suffix, value);
                store.removeItem(name);
            });
        });

        Object.keys(localStorage).forEach(function (name) {
            var isLegacy = LEGACY_PREFIXES.some(function (prefix) {
                return name.indexOf(prefix) === 0 && name.indexOf('@') === -1;
            });
            if (!isLegacy) return;
            var value = localStorage.getItem(name);
            if (localStorage.getItem(name + suffix) === null) localStorage.setItem(name + suffix, value);
            localStorage.removeItem(name);
        });

        localStorage.setItem(MIGRATED_KEY, '1');
    }

    function meta() {
        if (!current) return null;
        try {
            var all = JSON.parse(localStorage.getItem(META_KEY) || '{}');
            return all[current] || null;
        } catch (e) {
            return null;
        }
    }

    function setMeta(data) {
        if (!current) return;
        var all;
        try {
            all = JSON.parse(localStorage.getItem(META_KEY) || '{}');
        } catch (e) {
            all = {};
        }
        all[current] = data || {};
        localStorage.setItem(META_KEY, JSON.stringify(all));
        rememberInList(current, data);
    }

    /** История нод, к которым уже подключались — общая, не привязана к ноде. */
    function list() {
        try {
            var parsed = JSON.parse(localStorage.getItem(LIST_KEY) || '[]');
            return Array.isArray(parsed) ? parsed : [];
        } catch (e) {
            return [];
        }
    }

    function rememberInList(nodeOrigin, data) {
        var items = list().filter(function (item) { return item && item.origin !== nodeOrigin; });
        items.unshift({
            origin: nodeOrigin,
            name: (data && data.name) || '',
            description: (data && data.description) || '',
            lastUsedAt: Date.now()
        });
        localStorage.setItem(LIST_KEY, JSON.stringify(items.slice(0, 10)));
    }

    function forget(nodeOrigin) {
        var items = list().filter(function (item) { return item && item.origin !== nodeOrigin; });
        localStorage.setItem(LIST_KEY, JSON.stringify(items));
    }

    /**
     * Выбрать ноду. Метаданные (имя, цвета, livekit) кладутся позже, после
     * успешного Beacon.GetServerInfo.
     * @returns {string|null} нормализованный origin или null при неразбираемом адресе
     */
    function set(value, data) {
        var next = normalize(value);
        if (!next) return null;
        current = next;
        localStorage.setItem(ORIGIN_KEY, next);
        if (data) setMeta(data);
        else rememberInList(next, meta());
        return next;
    }

    function clear() {
        if (pinned) return;
        current = null;
        localStorage.removeItem(ORIGIN_KEY);
    }

    /** Клиент Beacon выбранной ноды. null, пока proto-бандл его не содержит. */
    function beaconClient() {
        var bf = window.barkfluff;
        if (!current || !bf || !bf.BeaconApiClient) return null;
        return new bf.BeaconApiClient(current);
    }

    /**
     * Обновляет метаданные текущей ноды из её Beacon. На самой ноде (pinned) экрана
     * выбора не было, поэтому meta пустая — а из неё берётся, в частности, отдельный
     * файловый адрес. Недоступный Beacon просто оставляет прежние метаданные.
     */
    function refreshMeta() {
        var client = beaconClient();
        if (!client || !window.proto) return Promise.resolve(null);

        return new Promise(function (resolve) {
            var req = new window.proto.barkfluff.beacon.GetServerInfoRequest();
            var metadata = (window.BF.metadata && window.BF.metadata.build()) || {};
            client.getServerInfo(req, metadata, function (err, resp) {
                if (err || !resp) { resolve(null); return; }
                var color = resp.getColor();
                var data = {
                    name: resp.getPublicName() || resp.getName(),
                    description: resp.getDescription(),
                    location: resp.getLocation(),
                    livekitUrl: resp.getLivekitUrl(),
                    serverName: resp.getServerName(),
                    filesMediaEndpoint: resp.getFilesMediaEndpoint(),
                    color: color ? color.getMainHex() : ''
                };
                setMeta(data);
                resolve(data);
            });
        });
    }

    /**
     * Клиент Navigator — всегда same-origin: каталог нод проксирует тот хост,
     * который отдал страницу (шелл), а не выбранная нода.
     */
    function navigatorClient() {
        var bf = window.barkfluff;
        if (!bf || !bf.NavigatorApiClient) return null;
        return new bf.NavigatorApiClient(window.location.origin);
    }

    migrateLegacy();

    window.BF.node = {
        origin: origin,
        id: id,
        key: key,
        pinned: function () { return pinned; },
        proxied: function () { return proxied; },
        normalize: normalize,
        set: set,
        clear: clear,
        forget: forget,
        meta: meta,
        setMeta: setMeta,
        refreshMeta: refreshMeta,
        list: list,
        beaconClient: beaconClient,
        navigatorClient: navigatorClient
    };
})();
