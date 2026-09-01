/**
 * Экран выбора ноды (index.html) — каталог из Navigator, история и ручной ввод.
 *
 * Показывается, пока BF.node.origin() пуст, то есть на глобальном шелле до первого
 * выбора. На самой ноде (BF_NODE_CONFIG.pinned) не используется вовсе.
 *
 * Requires: barkfluff.bundle.js (NavigatorApiClient/BeaconApiClient), BF.node, BF.i18n, BF.network
 * Exposes: BF.nodePicker
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var $ = function (sel) { return document.querySelector(sel); };

    // Beacon отвечает быстро; долгое ожидание здесь читается как «сайт завис».
    var PROBE_TIMEOUT = 8000;

    var section, list, manualForm, manualInput, errorBox, connectBtn;
    var onSelected = null;
    var busy = false;

    function cacheNodes() {
        if (section) return;
        section = $('#nodeSection');
        list = $('#nodeList');
        manualForm = $('#nodeManualForm');
        manualInput = $('#nodeManualInput');
        errorBox = $('#nodeError');
        connectBtn = $('#nodeConnectBtn');
    }

    function showError(key) {
        errorBox.textContent = BF.i18n.t(key);
        errorBox.classList.add('visible');
        if (BF.sound) BF.sound.play('droplet');
    }

    function clearError() {
        errorBox.classList.remove('visible');
    }

    function setBusy(isBusy) {
        busy = isBusy;
        connectBtn.classList.toggle('loading', isBusy);
        connectBtn.disabled = isBusy;
        list.classList.toggle('busy', isBusy);
    }

    /** Каталог публичных нод. Недоступный Navigator не блокирует ручной ввод. */
    function loadCatalog() {
        var client = BF.node.navigatorClient();
        if (!client) return Promise.resolve([]);

        var req = new window.proto.barkfluff.navigator.ListServersRequest();
        return BF.network.unary(
            client.listServers.bind(client),
            req,
            BF.metadata.build(),
            BF.network.POLICIES.READ
        ).then(function (resp) {
                if (!resp) return [];
                return resp.getServersList().map(function (s) {
                    var color = s.getColor();
                    return {
                        origin: BF.node.normalize(s.getWebEndpoint()),
                        name: s.getServerPublicName() || s.getName(),
                        description: s.getDescription(),
                        location: s.getLocation(),
                        color: color ? color.getMainHex() : ''
                    };
                });
            }).catch(function () { return []; });
    }

    /**
     * Проверяет, что по адресу действительно живёт нода BarkFluff, и заодно
     * забирает её метаданные. Без этого опечатка в адресе всплыла бы только
     * на экране логина непонятной ошибкой.
     */
    function probe(origin) {
        var bf = window.barkfluff;
        if (!bf || !bf.BeaconApiClient) return Promise.resolve(null);

        var client = new bf.BeaconApiClient(origin);
        var req = new window.proto.barkfluff.beacon.GetServerInfoRequest();
        return BF.network.unary(
            client.getServerInfo.bind(client),
            req,
            BF.metadata.build(),
            {
                attemptTimeoutMs: PROBE_TIMEOUT,
                overallTimeoutMs: PROBE_TIMEOUT,
                maxAttempts: 1,
                retryCodes: [],
                retryTransport: false,
                outcomeUnknown: false
            }
        ).then(function (resp) {
                if (!resp) return null;
                var color = resp.getColor();
                return {
                    name: resp.getPublicName() || resp.getName(),
                    description: resp.getDescription(),
                    location: resp.getLocation(),
                    livekitUrl: resp.getLivekitUrl(),
                    serverName: resp.getServerName(),
                    filesMediaEndpoint: resp.getFilesMediaEndpoint(),
                    color: color ? color.getMainHex() : ''
                };
            }).catch(function () { return null; });
    }

    function connect(value) {
        if (busy) return;
        clearError();

        var origin = BF.node.normalize(value);
        if (!origin) { showError('node.error.address'); return; }

        setBusy(true);
        probe(origin).then(function (meta) {
            if (!meta) {
                setBusy(false);
                showError('node.error.unreachable');
                return;
            }
            BF.node.set(origin, meta);
            setBusy(false);
            if (onSelected) onSelected(origin, meta);
        });
    }

    function card(item, isKnown) {
        var el = document.createElement('button');
        el.type = 'button';
        el.className = 'node-card';
        el.disabled = !item.origin;

        var dot = document.createElement('span');
        dot.className = 'node-card-dot';
        dot.style.backgroundColor = item.color || 'var(--primary)';
        el.appendChild(dot);

        var body = document.createElement('span');
        body.className = 'node-card-body';

        var title = document.createElement('span');
        title.className = 'node-card-title';
        title.textContent = item.name || item.origin || '';
        body.appendChild(title);

        var sub = document.createElement('span');
        sub.className = 'node-card-sub';
        if (!item.origin) sub.textContent = BF.i18n.t('node.noWebSupport');
        else if (item.description) sub.textContent = item.description;
        else sub.textContent = item.origin;
        body.appendChild(sub);

        if (item.location || isKnown) {
            var tag = document.createElement('span');
            tag.className = 'node-card-tag';
            tag.textContent = isKnown ? BF.i18n.t('node.recent') : item.location;
            body.appendChild(tag);
        }

        el.appendChild(body);
        if (item.origin) el.addEventListener('click', function () { connect(item.origin); });
        return el;
    }

    function render(catalog) {
        list.textContent = '';

        var known = BF.node.list();
        var catalogOrigins = {};
        catalog.forEach(function (item) { if (item.origin) catalogOrigins[item.origin] = true; });

        known.filter(function (item) { return item.origin && !catalogOrigins[item.origin]; })
            .forEach(function (item) { list.appendChild(card(item, true)); });

        catalog.forEach(function (item) { list.appendChild(card(item, false)); });

        if (!list.children.length) {
            var empty = document.createElement('p');
            empty.className = 'node-empty';
            empty.textContent = BF.i18n.t('node.empty');
            list.appendChild(empty);
        }
    }

    function open(opts) {
        cacheNodes();
        onSelected = (opts && opts.onSelected) || null;
        section.classList.remove('hidden');
        document.body.classList.add('node-picking');

        render([]);
        loadCatalog().then(render);
    }

    function close() {
        cacheNodes();
        section.classList.add('hidden');
        document.body.classList.remove('node-picking');
    }

    function init() {
        cacheNodes();
        if (!manualForm) return;
        manualForm.addEventListener('submit', function (e) {
            e.preventDefault();
            connect(manualInput.value.trim());
        });
        manualInput.addEventListener('input', clearError);
    }

    window.BF.nodePicker = { init: init, open: open, close: close, connect: connect };
})();
