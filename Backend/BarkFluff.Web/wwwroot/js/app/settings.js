/**
 * Settings dialog — multi-view panel: dashboard (profile header, stats, theme, sections), profile, password, 2FA, sessions.
 * Requires: BF.api, BF.files, BF.tokens, BF.realtime, BF.device, BF.utils, BF.icons, BF.confirmations
 * Exposes: BF.settings
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var overlay, backBtn, titleEl, body, confirmOverlay;
    var myUserId = null;
    var currentUser = null;
    var viewStack = [];

    // OtpTypeId enum values (mirrors proto)

    // ProfileFieldVisibility enum values (mirrors proto)
    var VIS_ALL = 0;
    var VIS_FRIENDS = 1;
    var VIS_NONE = 2;
    var VIS_OPTIONS = [
        { value: VIS_ALL, labelKey: 'privacy.visibility.all' },
        { value: VIS_FRIENDS, labelKey: 'privacy.visibility.friends' },
        { value: VIS_NONE, labelKey: 'privacy.visibility.none' }
    ];

    // UploadFileType enum values used here
    var FT_MESSAGE_ATTACHMENT_IMAGE = 2;
    var FT_USER_PROFILE_POSTER = 10;
    var LOGIN_NOTIFICATION_EMAIL = 1;
    var LOGIN_NOTIFICATION_TELEGRAM = 2;

    // Web app version (shown in About)
    var WEB_VERSION = '1.0';

    // Error code for wrong old password
    var ERR_WRONG_OLD_PASSWORD = 'A7E3F1B2-9C4D-4E8A-B5F6-2D1A3C7E9F04';

    var ICONS = {
        settings: BF.icons.html('general')
    };
    function init(opts) {
        myUserId = opts.myUserId;
        overlay = document.querySelector('#settingsOverlay');
        backBtn = document.querySelector('#sdBack');
        titleEl = document.querySelector('#sdTitle');
        body = document.querySelector('#sdBody');
        confirmOverlay = document.querySelector('#confirmOverlay');

        // Кнопка видна только на мобильном: закрывает раздел и возвращает к списку чатов.
        document.querySelector('#sdClose').addEventListener('click', closeFromButton);
        backBtn.addEventListener('click', goBack);
        body.addEventListener('animationend', function (e) {
            if (e.target === body) clearSlide();
        });
        BF.pages.register('settings', overlay, close);

        document.querySelector('#confirmCancel').addEventListener('click', function () {
            BF.utils.closeOverlay(confirmOverlay);
        });
        document.querySelector('#confirmOk').addEventListener('click', function () {
            var finishLogout = function () {
                BF.realtime.stopAll();
                BF.tokens.clear();
                BF.privateChat.clearAll();
                BF.personalization.clearAll();
                window.location.href = '/';
            };
            if (BF.push && BF.push.clearOnLogout) BF.push.clearOnLogout().finally(finishLogout);
            else finishLogout();
        });

        window.addEventListener('bf-pwa-install-available', function () {
            if (overlay.classList.contains('visible') && viewStack.length === 0) renderMain();
        });

        // Смена языка — перерисовать открытый экран настроек
        BF.i18n.onChange(function () {
            if (!overlay.classList.contains('visible')) return;
            // Перерисовка «Подключить устройство» отклонила бы отсканированный, но ещё не решённый вход.
            if (body.dataset.view === 'linkDevice') return;
            showView(viewStack.length ? viewStack[viewStack.length - 1] : 'main');
        });
    }

    function open(view) {
        var target = view || 'main';
        viewStack = target === 'main' ? [] : ['main'];
        currentUser = null;
        // Первое открытие без перехода: остаток класса от прерванного перехода (оверлей закрыт) не должен сработать.
        clearSlide();
        showView(target);
        BF.pages.show('settings');
    }

    function close() {
        runViewCleanup();
        BF.authUI.cancelAll();
        BF.pages.hide('settings');
        viewStack = [];
    }

    // Направление перехода содержимого #sdBody: класс перезапускает keyframes (reflow), снимается по animationend.
    var SLIDE_FORWARD = 'sd-slide-forward';
    var SLIDE_BACK = 'sd-slide-back';
    function clearSlide() {
        body.classList.remove(SLIDE_FORWARD, SLIDE_BACK);
    }
    function playSlide(cls) {
        clearSlide();
        void body.offsetWidth;
        body.classList.add(cls);
    }

    function goBack() {
        viewStack.pop();
        playSlide(SLIDE_BACK);
        if (viewStack.length === 0) {
            showView('main');
        } else {
            showView(viewStack[viewStack.length - 1]);
        }
    }

    function navigate(name) {
        viewStack.push(name);
        playSlide(SLIDE_FORWARD);
        showView(name);
    }

    // Освобождение ресурсов экрана (камера «Подключить устройство») при уходе с него или закрытии настроек.
    var viewCleanup = null;
    function runViewCleanup() {
        if (viewCleanup) { viewCleanup(); viewCleanup = null; }
    }

    function showView(name) {
        if (securityCleanup) { securityCleanup(); securityCleanup = null; BF.authUI.cancelAll(); }
        runViewCleanup();
        backBtn.classList.toggle('visible', viewStack.length > 0);
        body.dataset.view = name;
        switch (name) {
            case 'main':            renderMain(); break;
            case 'profile':         renderProfile(); break;
            case 'account':         renderAccount(); break;
            case 'notifications':   renderNotifications(); break;
            case 'password':        renderPassword(); break;
            case 'twofa':           renderTwoFA(); break;
            case 'sessions':        renderSessions(); break;
            case 'privacy':         renderPrivacy(); break;
            case 'personalization': renderPersonalization(); break;
            case 'language':        renderLanguage(); break;
            case 'about':           renderAbout(); break;
            case 'badges':          renderBadges(); break;
            case 'storage':         renderStorage(); break;
            case 'linkDevice':      renderLinkDevice(); break;
        }
        if (name !== 'main') decorateTitle(name);
    }

    // ========== HELPERS ==========

    function makeField(label, inputEl) {
        var wrap = document.createElement('div');
        wrap.className = 'sd-field';
        var lbl = document.createElement('div');
        lbl.className = 'sd-label';
        lbl.textContent = label;
        wrap.appendChild(lbl);
        wrap.appendChild(inputEl);
        return wrap;
    }

    function makeInput(type, placeholder, value) {
        var el = document.createElement('input');
        el.type = type || 'text';
        el.className = 'sd-input';
        el.placeholder = placeholder || '';
        if (value !== undefined && value !== null) el.value = value;
        return el;
    }

    function makeHint(text, isError) {
        var el = document.createElement('div');
        el.className = 'sd-hint' + (isError ? ' error' : '');
        el.textContent = text;
        return el;
    }

    function makeSaveBtn(label) {
        var btn = document.createElement('button');
        btn.className = 'sd-btn sd-btn-primary';
        btn.textContent = label || BF.i18n.t('common.save');
        return btn;
    }

    function extractErrorCode(err) {
        if (!err) return null;
        var msg = (err.message || err.toString()) + (err.metadata ? JSON.stringify(err.metadata) : '');
        var m = msg.match(/[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}/i);
        return m ? m[0].toUpperCase() : null;
    }

    function loadCurrentUser() {
        if (currentUser) return Promise.resolve(currentUser);
        return BF.api.getUser(myUserId).then(function (d) {
            if (d && d.user) currentUser = d.user;
            return currentUser;
        });
    }

    // Заглушки экранов на время загрузки: строки повторяют форму реальных; текст загрузки виден только скринридеру.
    var SKELETON_BADGE_ROW =
        '<div class="sd-badge-item sd-badge-skeleton" aria-hidden="true"><span class="skeleton sd-badge-skeleton-icon"></span>' +
        '<span class="sd-badge-info"><span class="skeleton sd-badge-skeleton-name"></span>' +
        '<span class="skeleton sd-badge-skeleton-desc"></span></span></div>';
    var SKELETON_SESSION_ROW =
        '<div class="session-item" aria-hidden="true"><span class="session-info">' +
        '<span class="skeleton sd-skeleton-line sd-skeleton-line-title"></span>' +
        '<span class="skeleton sd-skeleton-line sd-skeleton-line-meta"></span></span></div>';
    var SKELETON_TOGGLE_ROW =
        '<div class="sd-toggle-row" aria-hidden="true"><span class="sd-toggle-info">' +
        '<span class="skeleton sd-skeleton-line sd-skeleton-line-title"></span>' +
        '<span class="skeleton sd-skeleton-line sd-skeleton-line-meta"></span></span>' +
        '<span class="skeleton sd-skeleton-switch"></span></div>';

    function skeletonRows(count, rowHtml) {
        var html = '<span class="sr-only">' + BF.utils.escapeHtml(BF.i18n.t('common.loading')) + '</span>';
        for (var i = 0; i < count; i++) html += rowHtml;
        return html;
    }

    function makeToggleRow(title, desc, initial, onChange) {
        var row = document.createElement('div');
        row.className = 'sd-toggle-row';
        var info = document.createElement('div');
        info.className = 'sd-toggle-info';
        var t = document.createElement('div');
        t.className = 'sd-toggle-title';
        t.textContent = title;
        info.appendChild(t);
        if (desc) {
            var d = document.createElement('div');
            d.className = 'sd-toggle-desc';
            d.textContent = desc;
            info.appendChild(d);
        }
        var sw = document.createElement('button');
        sw.type = 'button';
        sw.className = 'sd-switch' + (initial ? ' on' : '');
        sw.setAttribute('aria-pressed', initial ? 'true' : 'false');
        sw.addEventListener('click', function () {
            var next = !sw.classList.contains('on');
            sw.classList.toggle('on', next);
            sw.setAttribute('aria-pressed', next ? 'true' : 'false');
            BF.sound.play('toggle');
            if (onChange) onChange(next, sw);
        });
        row.appendChild(info);
        row.appendChild(sw);
        return { row: row, getValue: function () { return sw.classList.contains('on'); }, setValue: function (v) {
            sw.classList.toggle('on', !!v);
            sw.setAttribute('aria-pressed', v ? 'true' : 'false');
        }, setDisabled: function (v) { sw.disabled = !!v; } };
    }

    function makeSegmented(options, initial, onChange) {
        var wrap = document.createElement('div');
        wrap.className = 'sd-segmented';
        var current = initial;
        var btns = [];
        options.forEach(function (opt) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'sd-segmented-option' + (opt.value === initial ? ' active' : '');
            b.textContent = opt.labelKey ? BF.i18n.t(opt.labelKey) : opt.label;
            b.addEventListener('click', function () {
                if (current === opt.value) return;
                current = opt.value;
                btns.forEach(function (x) {
                    x.classList.toggle('active', x.dataset.val === String(opt.value));
                });
                if (onChange) onChange(opt.value);
            });
            b.dataset.val = String(opt.value);
            btns.push(b);
            wrap.appendChild(b);
        });
        return { el: wrap, getValue: function () { return current; } };
    }

    function renderAvatarEl(user, className) {
        var av = document.createElement('div');
        av.className = className || 'sd-avatar';
        if (user && user.profilePicture) {
            var img = document.createElement('img');
            img.src = user.profilePicture;
            img.alt = '';
            av.appendChild(img);
        } else {
            var letter = (user && (user.firstName || user.username)) || '?';
            av.textContent = letter[0].toUpperCase();
        }
        return av;
    }

    // ========== VIEWS ==========

    // Тайл экрана: иконка, цвет и подзаголовок — в строке главного экрана и в шапке подстраницы.
    var VIEW_META = {
        account: { label: 'settings.profile', icon: 'identity', color: '#d9542c', desc: 'settings.desc.account' },
        profile: { label: 'settings.profilePhoto', icon: 'image', color: '#d9542c', desc: 'settings.profilePhoto.hint' },
        badges: { label: 'settings.badges', icon: 'badge', color: '#d9542c', desc: 'settings.desc.badges' },
        storage: { label: 'settings.storage', icon: 'storage-data', color: '#ca8a04', desc: 'settings.desc.storage' },
        privacy: { label: 'settings.privacy', icon: 'privacy', color: '#0e9f6e', desc: 'settings.desc.privacy' },
        password: { label: 'common.password', icon: 'password', color: '#2563eb', desc: 'settings.desc.password' },
        twofa: { label: 'settings.twofa', icon: 'two-factor', color: '#2563eb', desc: 'settings.desc.twofa' },
        sessions: { label: 'settings.sessions', icon: 'active-sessions', color: '#7c3aed', desc: 'settings.desc.sessions' },
        linkDevice: { label: 'fastAuthLink.title', icon: 'qr-code', color: '#7c3aed' },
        personalization: { label: 'settings.personalization', icon: 'personalization', color: '#c026d3', desc: 'settings.desc.personalization' },
        language: { label: 'settings.language.item', icon: 'language', color: '#0891b2', desc: 'settings.desc.language' },
        notifications: { label: 'settings.section.notifications', icon: 'notifications', color: '#e11d48', desc: 'settings.desc.notifications' },
        about: { label: 'settings.about', icon: 'about-app', color: '#78716c', desc: 'settings.desc.about' }
    };

    function tileHtml(icon, color) {
        return '<span class="sd-tile" style="--sd-tile:' + color + '">' + BF.icons.html(icon) + '</span>';
    }

    // Шапка подстраницы: тайл, заголовок (его ставит экран) и подзаголовок.
    function decorateTitle(name) {
        var meta = VIEW_META[name];
        if (!meta) return;
        var text = titleEl.textContent;
        titleEl.innerHTML =
            tileHtml(meta.icon, meta.color) +
            '<span class="sd-title-text"><span>' + BF.utils.escapeHtml(text) + '</span>' +
            (meta.desc ? '<span class="sd-title-desc">' + BF.utils.escapeHtml(BF.i18n.t(meta.desc)) + '</span>' : '') +
            '</span>';
    }

    // Мини-превью темы: фон и два пузыря в цветах темы.
    var THEMES = [
        { id: 'light', key: 'theme.light', bg: '#efebe5', inBg: '#e2dcd5', outBg: '#c49a80' },
        { id: 'dark', key: 'theme.dark', bg: '#141210', inBg: '#272320', outBg: '#4a2e1e' },
        { id: 'midnight', key: 'theme.midnight', bg: '#0d0f1a', inBg: '#191c30', outBg: '#2e295e' }
    ];

    function buildThemePicker() {
        var cur = window.__getTheme ? window.__getTheme() : 'light';
        var row = document.createElement('div');
        row.className = 'sd-themes';
        row.setAttribute('role', 'radiogroup');
        row.setAttribute('aria-label', BF.i18n.t('theme.title'));
        var buttons = [];
        THEMES.forEach(function (theme) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'sd-theme' + (cur === theme.id ? ' active' : '');
            btn.setAttribute('role', 'radio');
            btn.setAttribute('aria-checked', cur === theme.id ? 'true' : 'false');
            btn.innerHTML =
                '<span class="sd-theme-preview" style="background:' + theme.bg + '">' +
                '<i style="background:' + theme.inBg + '"></i><i style="background:' + theme.outBg + '"></i>' +
                '<i style="background:' + theme.inBg + '"></i></span>' +
                '<span class="sd-theme-name"><span>' + BF.utils.escapeHtml(BF.i18n.t(theme.key)) + '</span>' +
                '<span class="sd-theme-radio"></span></span>';
            btn.addEventListener('click', function () {
                if (window.__setTheme) window.__setTheme(theme.id);
                buttons.forEach(function (b) {
                    var active = b === btn;
                    b.classList.toggle('active', active);
                    b.setAttribute('aria-checked', active ? 'true' : 'false');
                });
            });
            buttons.push(btn);
            row.appendChild(btn);
        });
        return row;
    }

    function makeStat(view, label) {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'sd-stat';
        var value = document.createElement('span');
        value.className = 'sd-stat-value';
        value.textContent = '…';
        var caption = document.createElement('span');
        caption.className = 'sd-stat-label';
        caption.textContent = label;
        btn.appendChild(value);
        btn.appendChild(caption);
        btn.addEventListener('click', function () { navigate(view); });
        return { el: btn, value: value, caption: caption };
    }

    function renderMain() {
        titleEl.innerHTML = ICONS.settings + '<span>' + BF.utils.escapeHtml(BF.i18n.t('settings.title')) + '</span>';
        body.innerHTML = '';
        var isMain = function () { return body.dataset.view === 'main'; };

        // Шапка: постер, аватар (→ фото), имя и кнопка «Изменить» (→ профиль)
        var hero = document.createElement('div');
        hero.className = 'sd-hero';
        var poster = document.createElement('div');
        poster.className = 'sd-hero-poster';
        var heroTop = document.createElement('div');
        heroTop.className = 'sd-hero-top';
        heroTop.innerHTML = '<span class="sd-hero-title">' + ICONS.settings + '<span>' +
            BF.utils.escapeHtml(BF.i18n.t('settings.title')) + '</span></span>';
        var heroClose = document.createElement('button');
        heroClose.type = 'button';
        heroClose.className = 'sd-close';
        heroClose.setAttribute('aria-label', BF.i18n.t('common.close'));
        heroClose.innerHTML = BF.icons.html('close');
        heroClose.addEventListener('click', closeFromButton);
        heroTop.appendChild(heroClose);

        var heroBody = document.createElement('div');
        heroBody.className = 'sd-hero-body';
        var avatarBtn = document.createElement('button');
        avatarBtn.type = 'button';
        avatarBtn.className = 'sd-hero-avatar';
        avatarBtn.setAttribute('aria-label', BF.i18n.t('settings.profilePhoto'));
        var avatarEl = renderAvatarEl(null, 'sd-avatar');
        avatarEl.textContent = '…';
        var camera = document.createElement('span');
        camera.className = 'sd-hero-camera';
        camera.innerHTML = BF.icons.html('image');
        avatarBtn.appendChild(avatarEl);
        avatarBtn.appendChild(camera);
        avatarBtn.addEventListener('click', function () { navigate('profile'); });

        var userInfo = document.createElement('div');
        userInfo.className = 'sd-hero-who';
        var nameEl = document.createElement('div');
        nameEl.className = 'sd-user-name';
        nameEl.textContent = '…';
        var unameEl = document.createElement('div');
        unameEl.className = 'sd-user-username';
        userInfo.appendChild(nameEl);
        userInfo.appendChild(unameEl);

        var editBtn = document.createElement('button');
        editBtn.type = 'button';
        editBtn.className = 'sd-btn sd-hero-edit';
        editBtn.setAttribute('aria-label', BF.i18n.t('settings.profile'));
        editBtn.innerHTML = BF.icons.html('edit') + '<span>' + BF.utils.escapeHtml(BF.i18n.t('settings.edit')) + '</span>';
        editBtn.addEventListener('click', function () { navigate('account'); });

        heroBody.appendChild(avatarBtn);
        heroBody.appendChild(userInfo);
        heroBody.appendChild(editBtn);
        hero.appendChild(poster);
        hero.appendChild(heroTop);
        hero.appendChild(heroBody);
        body.appendChild(hero);

        // Счётчики: бейджи, сессии, хранилище
        var stats = document.createElement('div');
        stats.className = 'sd-stats';
        var statBadges = makeStat('badges', BF.i18n.tp('settings.stats.badges', 0));
        var statSessions = makeStat('sessions', BF.i18n.tp('settings.stats.sessions', 0));
        var statStorage = makeStat('storage', BF.i18n.t('settings.stats.storage'));
        stats.appendChild(statBadges.el);
        stats.appendChild(statSessions.el);
        stats.appendChild(statStorage.el);
        body.appendChild(stats);

        // Предупреждение: вход только по паролю
        var alertEl = document.createElement('div');
        alertEl.className = 'sd-alert';
        alertEl.hidden = true;
        alertEl.innerHTML = tileHtml('two-factor', 'var(--sd-warn)') +
            '<div class="sd-alert-text"><div class="sd-alert-title">' + BF.utils.escapeHtml(BF.i18n.t('settings.twofa.alert.title')) +
            '</div><div class="sd-alert-desc">' + BF.utils.escapeHtml(BF.i18n.t('settings.twofa.alert.text')) + '</div></div>';
        var alertBtn = document.createElement('button');
        alertBtn.type = 'button';
        alertBtn.className = 'sd-btn sd-btn-primary';
        alertBtn.textContent = BF.i18n.t('settings.twofa.alert.action');
        alertBtn.addEventListener('click', function () { navigate('twofa'); });
        alertEl.appendChild(alertBtn);
        body.appendChild(alertEl);

        var themeSection = document.createElement('div');
        themeSection.className = 'sd-section';
        var themeTitle = document.createElement('div');
        themeTitle.className = 'sd-section-title';
        themeTitle.textContent = BF.i18n.t('theme.title');
        themeSection.appendChild(themeTitle);
        themeSection.appendChild(buildThemePicker());
        body.appendChild(themeSection);

        var rows = {};
        var addSection = function (titleKey, views) {
            var section = makeSection(BF.i18n.t(titleKey), views);
            body.appendChild(section.el);
            Object.assign(rows, section.values);
            return section;
        };
        addSection('settings.section.account', ['account', 'badges', 'storage']);
        addSection('settings.section.confidentiality', ['privacy']);
        addSection('settings.section.security', ['password', 'twofa', 'sessions']);
        addSection('settings.section.appearance', ['personalization', 'language']);
        addSection('settings.section.notifications', ['notifications']);
        var about = addSection('settings.section.about', ['about']);

        // Смена ноды: на самой ноде (pinned) переключаться некуда — строки нет.
        if (!BF.node.pinned()) {
            var nodeMeta = BF.node.meta();
            var nodeRow = makeRow('about-server', VIEW_META.about.color, BF.i18n.t('settings.server.change'),
                (nodeMeta && nodeMeta.name) || BF.node.origin() || '');
            makeActivatable(nodeRow.el, switchNode);
            about.group.appendChild(nodeRow.el);
        }
        if (BF.push && BF.push.canInstall && BF.push.canInstall()) {
            var installRow = makeRow('download', VIEW_META.about.color, BF.i18n.t('settings.installApp'));
            makeActivatable(installRow.el, function () { BF.push.install().then(function () { renderMain(); }); });
            about.group.appendChild(installRow.el);
        }

        var logoutGroup = document.createElement('div');
        logoutGroup.className = 'sd-section';
        var logoutInner = document.createElement('div');
        logoutInner.className = 'sd-group';
        var logoutRow = makeRow('logout', 'var(--error)', BF.i18n.t('settings.logout'));
        logoutRow.el.classList.add('sd-item-danger');
        logoutRow.el.id = 'settingsLogoutBtn';
        makeActivatable(logoutRow.el, function () { BF.utils.openOverlay(confirmOverlay); });
        logoutInner.appendChild(logoutRow.el);
        logoutGroup.appendChild(logoutInner);
        body.appendChild(logoutGroup);

        var lang = (BF.i18n.langs ? BF.i18n.langs() : []).filter(function (l) {
            return l.code === BF.i18n.current();
        })[0];
        if (lang) rows.language.textContent = lang.name;

        loadCurrentUser().then(function (user) {
            if (!user || !isMain()) return;
            avatarBtn.replaceChild(renderAvatarEl(user, 'sd-avatar'), avatarEl);
            nameEl.textContent = [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username || '';
            nameEl.appendChild(BF.userBadges.createSlot(myUserId));
            unameEl.textContent = user.username ? '@' + user.username : '';
            rows.account.textContent = unameEl.textContent;
            if (user.profilePosterFileId) {
                BF.files.getFileUrls([user.profilePosterFileId]).then(function (urls) {
                    var u = urls && urls[0];
                    var url = u && (u.url || u.previewUrl);
                    if (url && isMain()) {
                        poster.style.backgroundImage = 'url("' + url + '")';
                        poster.classList.add('has-image');
                    }
                }).catch(function () {});
            }
        });

        // Сводка для счётчиков и значений строк; ошибка оставляет прочерк.
        var failed = function (stat) {
            return function () { if (isMain()) stat.value.textContent = '—'; };
        };
        BF.api.getUserBadges(myUserId).then(function (data) {
            var badges = (data && data.badges) || [];
            BF.userBadges.set(myUserId, badges);
            if (!isMain()) return;
            statBadges.value.textContent = String(badges.length);
            statBadges.caption.textContent = BF.i18n.tp('settings.stats.badges', badges.length);
            var primary = badges.filter(function (b) { return b.isPrimary; })[0];
            rows.badges.textContent = primary ? primary.name : '';
        }).catch(failed(statBadges));
        BF.api.getActiveSessions().then(function (data) {
            if (!isMain()) return;
            var count = ((data && data.sessions) || []).length;
            statSessions.value.textContent = String(count);
            statSessions.caption.textContent = BF.i18n.tp('settings.stats.sessions', count);
            rows.sessions.textContent = BF.i18n.tp('settings.sessions.count', count);
        }).catch(failed(statSessions));
        BF.api.getUserStorageInfo().then(function (info) {
            if (!isMain()) return;
            var size = BF.utils.formatFileSize;
            if (info.limit > 0) {
                var percent = Math.min(100, Math.round((info.totalUsed / info.limit) * 100));
                statStorage.value.textContent = percent + '%';
                var bar = document.createElement('span');
                bar.className = 'sd-stat-bar';
                var fill = document.createElement('span');
                fill.style.width = percent + '%';
                bar.appendChild(fill);
                statStorage.el.appendChild(bar);
                rows.storage.textContent = size(info.totalUsed) + ' / ' + size(info.limit);
            } else {
                statStorage.value.textContent = size(info.totalUsed);
                rows.storage.textContent = size(info.totalUsed);
            }
        }).catch(failed(statStorage));
        loadSecuritySettings().then(function (security) {
            if (!isMain() || !security) return;
            // 1 — вход только по паролю, 3 — пароль и подтверждение (LoginMode)
            if (security.loginMode === 1) {
                alertEl.hidden = false;
                rows.twofa.innerHTML = '<span class="sd-tag sd-tag-warn">' + BF.utils.escapeHtml(BF.i18n.t('settings.twofa.off')) + '</span>';
            } else if (security.loginMode === 3) {
                rows.twofa.innerHTML = '<span class="sd-tag sd-tag-ok">' + BF.utils.escapeHtml(BF.i18n.t('settings.twofa.on')) + '</span>';
            }
        }).catch(function () {});
    }

    function loadSecuritySettings() {
        return BF.confirmations
            .call('getSecuritySettings', 'GetSecuritySettingsRequest', {}, { auth: true, read: true, origin: BF.node.origin() })
            .then(function (res) { return res && res.toObject(); });
    }

    function closeFromButton() {
        close();
        if (window.__mobileShowList) window.__mobileShowList();
    }

    // --- Notifications: браузерные уведомления и канал уведомлений о входах ---
    function renderNotifications() {
        titleEl.textContent = BF.i18n.t('settings.section.notifications');
        body.innerHTML = '';
        var secNotifications = document.createElement('div');
        secNotifications.className = 'sd-section';
        var pushSupported = !!(BF.push && BF.push.isSupported && BF.push.isSupported());
        var pushStatus = BF.push && BF.push.status ? BF.push.status() : 'unsupported';
        var pushDescription = pushStatus === 'denied'
            ? BF.i18n.t('settings.push.denied')
            : pushSupported
                ? BF.i18n.t('settings.push.description')
                : BF.i18n.t('settings.push.unsupported');
        var pushToggle = makeToggleRow(
            BF.i18n.t('settings.push.title'),
            pushDescription,
            pushStatus === 'enabled',
            function (next) {
                if (!pushSupported || pushStatus === 'denied') { pushToggle.setValue(false); return; }
                pushToggle.setDisabled(true);
                (next ? BF.push.enable() : BF.push.disable()).then(function (success) {
                    pushToggle.setValue(next && !!success);
                    if (next && !success && BF.push.status && BF.push.status() === 'denied') renderNotifications();
                }).finally(function () { pushToggle.setDisabled(false); });
            });
        if (!pushSupported || pushStatus === 'denied') pushToggle.setDisabled(true);
        secNotifications.appendChild(pushToggle.row);
        var loginAlerts = document.createElement('div');
        loginAlerts.className = 'sd-notification-channel';
        var loginAlertsInfo = document.createElement('div');
        loginAlertsInfo.className = 'sd-toggle-info';
        var loginAlertsTitle = document.createElement('div');
        loginAlertsTitle.className = 'sd-toggle-title';
        loginAlertsTitle.textContent = BF.i18n.t('settings.loginAlerts.title');
        var loginAlertsDescription = document.createElement('div');
        loginAlertsDescription.className = 'sd-toggle-desc';
        loginAlertsDescription.textContent = BF.i18n.t('settings.loginAlerts.loading');
        loginAlertsInfo.appendChild(loginAlertsTitle);
        loginAlertsInfo.appendChild(loginAlertsDescription);
        var loginAlertsChannel = document.createElement('select');
        loginAlertsChannel.className = 'sd-input sd-notification-channel-select';
        loginAlertsChannel.setAttribute('aria-label', BF.i18n.t('settings.loginAlerts.title'));
        loginAlerts.appendChild(loginAlertsInfo);
        loginAlerts.appendChild(loginAlertsChannel);
        secNotifications.appendChild(loginAlerts);
        BF.api.getLoginNotificationSettings().then(function (data) {
            if (!loginAlerts.isConnected) return;
            var options = [];
            if (data.emailAvailable) options.push({ value: LOGIN_NOTIFICATION_EMAIL, label: BF.i18n.t('settings.loginAlerts.email') });
            if (data.telegramAvailable) options.push({ value: LOGIN_NOTIFICATION_TELEGRAM, label: BF.i18n.t('settings.loginAlerts.telegram') });
            loginAlertsChannel.replaceChildren();
            options.forEach(function (option) {
                var element = document.createElement('option');
                element.value = String(option.value);
                element.textContent = option.label;
                loginAlertsChannel.appendChild(element);
            });
            if (!options.length) {
                loginAlertsChannel.disabled = true;
                loginAlertsDescription.textContent = BF.i18n.t('settings.loginAlerts.unavailable');
                return;
            }
            loginAlertsChannel.value = String(data.channel);
            loginAlertsChannel.disabled = options.length < 2;
            loginAlertsDescription.textContent = options.length > 1
                ? BF.i18n.t('settings.loginAlerts.description')
                : BF.i18n.t('settings.loginAlerts.required');
            loginAlertsChannel.addEventListener('change', function () {
                var previous = data.channel;
                loginAlertsChannel.disabled = true;
                BF.api.setLoginNotificationChannel(Number(loginAlertsChannel.value)).then(function (updated) {
                    data = updated;
                    loginAlertsChannel.value = String(data.channel);
                }).catch(function () {
                    loginAlertsChannel.value = String(previous);
                    loginAlertsDescription.textContent = BF.i18n.t('settings.loginAlerts.saveError');
                }).finally(function () {
                    if (!loginAlerts.isConnected) return;
                    loginAlertsChannel.disabled = options.length < 2;
                });
            });
        }).catch(function () {
            if (!loginAlerts.isConnected) return;
            loginAlertsChannel.disabled = true;
            loginAlertsDescription.textContent = BF.i18n.t('settings.loginAlerts.loadError');
        });
        body.appendChild(secNotifications);
    }

    /**
     * Уход на экран выбора ноды. Сессию текущей ноды не трогаем: токены лежат под
     * её неймспейсом, и при возврате вход не потребуется. Стримы гасим, чтобы не
     * оставить висящие соединения к покидаемой ноде.
     */
    function switchNode() {
        if (!confirm(BF.i18n.t('settings.server.confirm'))) return;
        BF.realtime.stopAll();
        BF.node.clear();
        window.location.href = '/';
    }

    // Строки-контейнеры (div с вложенной разметкой) делаем доступными с клавиатуры как кнопки.
    function makeActivatable(el, onActivate) {
        el.setAttribute('role', 'button');
        el.tabIndex = 0;
        el.addEventListener('click', onActivate);
        el.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter' && e.key !== ' ') return;
            e.preventDefault();
            onActivate(e);
        });
    }

    function makeRow(icon, color, label, value) {
        var row = document.createElement('div');
        row.className = 'sd-item';
        row.innerHTML = tileHtml(icon, color) + '<span class="sd-item-label">' + BF.utils.escapeHtml(label) + '</span>';
        var valueEl = document.createElement('span');
        valueEl.className = 'sd-item-value';
        valueEl.textContent = value || '';
        var arrow = document.createElement('span');
        arrow.className = 'sd-item-arrow';
        arrow.innerHTML = BF.icons.html('chevron-right');
        row.appendChild(valueEl);
        row.appendChild(arrow);
        return { el: row, value: valueEl };
    }

    // Раздел главного экрана: заголовок и карточка строк-переходов; values — элементы значений по имени экрана.
    function makeSection(title, views) {
        var sec = document.createElement('div');
        sec.className = 'sd-section';
        var titleEl2 = document.createElement('div');
        titleEl2.className = 'sd-section-title';
        titleEl2.textContent = title;
        sec.appendChild(titleEl2);
        var group = document.createElement('div');
        group.className = 'sd-group';
        sec.appendChild(group);
        var values = {};
        views.forEach(function (view) {
            var meta = VIEW_META[view];
            var row = makeRow(meta.icon, meta.color, BF.i18n.t(meta.label));
            makeActivatable(row.el, function () { navigate(view); });
            group.appendChild(row.el);
            values[view] = row.value;
        });
        return { el: sec, group: group, values: values };
    }

    // --- Badges: выбор основного бейджа (SetPrimaryBadge) ---
    function renderBadges() {
        titleEl.textContent = BF.i18n.t('settings.badges');
        body.innerHTML = '';
        var hint = makeHint(BF.i18n.t('settings.badges.hint'));
        var list = document.createElement('div');
        list.className = 'sd-badge-list';
        var status = makeHint('');
        body.appendChild(hint);
        body.appendChild(list);
        body.appendChild(status);
        list.innerHTML = skeletonRows(3, SKELETON_BADGE_ROW);

        var badges = [];
        var saving = false;

        function draw() {
            list.innerHTML = '';
            if (!badges.length) {
                list.textContent = BF.i18n.t('settings.badges.empty');
                return;
            }
            badges.forEach(function (b) {
                var row = document.createElement('button');
                row.type = 'button';
                row.className = 'sd-badge-item' + (b.isPrimary ? ' primary' : '');
                row.setAttribute('aria-pressed', b.isPrimary ? 'true' : 'false');
                row.disabled = saving;
                if (b.imageUrl) {
                    var img = document.createElement('img');
                    img.src = b.imageUrl;
                    img.alt = '';
                    row.appendChild(img);
                }
                var info = document.createElement('span');
                info.className = 'sd-badge-info';
                var name = document.createElement('span');
                name.className = 'sd-badge-name';
                name.textContent = b.name;
                info.appendChild(name);
                if (b.description) {
                    var desc = document.createElement('span');
                    desc.className = 'sd-badge-desc';
                    desc.textContent = b.description;
                    info.appendChild(desc);
                }
                row.appendChild(info);
                if (b.isPrimary) {
                    var mark = document.createElement('span');
                    mark.className = 'sd-badge-mark';
                    mark.textContent = BF.i18n.t('settings.badges.primary');
                    row.appendChild(mark);
                }
                // Повторный клик по основному снимает выбор (badge_id = 0).
                row.addEventListener('click', function () {
                    choose(b.isPrimary ? 0 : b.id);
                });
                list.appendChild(row);
            });
        }

        function choose(badgeId) {
            if (saving) return;
            saving = true;
            status.textContent = '';
            draw();
            BF.api
                .setPrimaryBadge(badgeId)
                .then(function (data) {
                    var primaryId = data.primaryBadge ? data.primaryBadge.id : 0;
                    badges.forEach(function (b) {
                        b.isPrimary = b.id === primaryId;
                    });
                    // Значок рядом с именем в списках и шапках обновляется сразу.
                    BF.userBadges.set(myUserId, badges);
                })
                .catch(function () {
                    status.textContent = BF.i18n.t('common.error');
                    status.className = 'sd-hint error';
                })
                .then(function () {
                    saving = false;
                    if (body.dataset.view === 'badges') draw();
                });
        }

        BF.api
            .getUserBadges(myUserId)
            .then(function (data) {
                BF.userBadges.set(myUserId, data.badges);
                if (body.dataset.view !== 'badges') return;
                badges = data.badges.map(function (b) {
                    return Object.assign({}, b);
                });
                draw();
            })
            .catch(function () {
                if (body.dataset.view !== 'badges') return;
                list.textContent = BF.i18n.t('common.loadError');
            });
    }

    // --- Storage: занятое место по типам файлов (GetUserStorageInfo) ---
    var STORAGE_TYPE_KEYS = {
        1: 'settings.storage.avatar',
        2: 'attachment.photo',
        3: 'attachment.video',
        4: 'settings.storage.gif',
        5: 'attachment.document',
        6: 'settings.storage.chatPicture',
        7: 'attachment.audio',
        8: 'attachment.voice',
        9: 'attachment.sticker',
        10: 'settings.storage.poster'
    };

    function renderStorage() {
        titleEl.textContent = BF.i18n.t('settings.storage');
        body.innerHTML = '';
        var summary = makeHint(BF.i18n.t('common.loadingShort'));
        body.appendChild(summary);

        BF.api
            .getUserStorageInfo()
            .then(function (info) {
                if (body.dataset.view !== 'storage') return;
                var size = BF.utils.formatFileSize;
                summary.textContent = info.limit > 0
                    ? BF.i18n.t('settings.storage.used', { used: size(info.totalUsed), limit: size(info.limit) })
                    : size(info.totalUsed);
                if (info.limit > 0) {
                    var bar = document.createElement('div');
                    bar.className = 'sd-storage-bar';
                    var fill = document.createElement('div');
                    fill.style.width = Math.min(100, (info.totalUsed / info.limit) * 100) + '%';
                    bar.appendChild(fill);
                    body.appendChild(bar);
                }
                info.byType
                    .filter(function (t) {
                        return t.used > 0;
                    })
                    .sort(function (a, b) {
                        return b.used - a.used;
                    })
                    .forEach(function (t) {
                        var row = document.createElement('div');
                        row.className = 'sd-about-row';
                        var l = document.createElement('div');
                        l.className = 'sd-about-label';
                        l.textContent = BF.i18n.t(STORAGE_TYPE_KEYS[t.fileType] || 'attachment.generic');
                        var v = document.createElement('div');
                        v.className = 'sd-about-value';
                        v.textContent = size(t.used);
                        row.appendChild(l);
                        row.appendChild(v);
                        body.appendChild(row);
                    });
            })
            .catch(function () {
                if (body.dataset.view !== 'storage') return;
                summary.textContent = BF.i18n.t('common.loadError');
                summary.className = 'sd-hint error';
            });
    }

    // --- Profile (avatar upload) ---
    function renderProfile() {
        titleEl.textContent = BF.i18n.t('settings.profilePhoto');
        body.innerHTML = '';

        var avatarUpload = document.createElement('div');
        avatarUpload.className = 'sd-avatar-upload';

        var avatarLarge = document.createElement('div');
        avatarLarge.className = 'sd-avatar-large';
        avatarLarge.textContent = '…';

        var hint = document.createElement('div');
        hint.className = 'sd-avatar-hint';
        hint.textContent = BF.i18n.t('settings.profilePhoto.hint');

        var fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.accept = 'image/*';
        fileInput.style.display = 'none';

        var statusEl = document.createElement('div');
        statusEl.className = 'sd-hint';

        avatarUpload.appendChild(avatarLarge);
        avatarUpload.appendChild(hint);
        avatarUpload.appendChild(fileInput);
        avatarUpload.appendChild(statusEl);
        body.appendChild(avatarUpload);

        loadCurrentUser().then(function (user) {
            if (!user) return;
            // Replace placeholder with actual avatar
            var avEl = renderAvatarEl(user, 'sd-avatar-large');
            avatarLarge.parentNode.replaceChild(avEl, avatarLarge);
            avatarLarge = avEl;
            avatarLarge.style.cursor = 'pointer';
            avatarLarge.addEventListener('click', function () { fileInput.click(); });
        });

        avatarLarge.style.cursor = 'pointer';
        avatarLarge.addEventListener('click', function () { fileInput.click(); });

        fileInput.addEventListener('change', function () {
            var file = fileInput.files[0];
            if (!file) return;
            statusEl.textContent = BF.i18n.t('common.loadingShort');
            statusEl.className = 'sd-hint';
            // USER_AVATAR = 1
            BF.files.uploadFile(file, 1).then(function (fileId) {
                return BF.api.setProfilePicture(fileId).then(function () {
                    statusEl.textContent = BF.i18n.t('settings.photoUpdated');
                    // Update cached user picture
                    if (currentUser) {
                        var url = URL.createObjectURL(file);
                        currentUser.profilePicture = url;
                        // Update avatar shown
                        var img = document.createElement('img');
                        img.src = url;
                        img.alt = '';
                        avatarLarge.innerHTML = '';
                        avatarLarge.appendChild(img);
                    }
                });
            }).catch(function () {
                statusEl.textContent = BF.i18n.t('common.loadError');
                statusEl.className = 'sd-hint error';
            });
        });
    }

    // --- Profile: имя, фамилия, юзернейм и биография одной формой ---
    var ERR_USERNAME_FORMAT = 'E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47';

    function renderAccount() {
        titleEl.textContent = BF.i18n.t('settings.profile');
        body.innerHTML = '';

        var form = document.createElement('div');
        form.className = 'sd-form';

        var fnInput = makeInput('text', BF.i18n.t('profile.firstName'), '');
        var lnInput = makeInput('text', BF.i18n.t('profile.lastName'), '');
        var unInput = makeInput('text', BF.i18n.t('profile.username'), '');
        var bioInput = document.createElement('textarea');
        bioInput.className = 'sd-input';
        bioInput.rows = 4;
        bioInput.placeholder = BF.i18n.t('profile.bio.placeholder');
        bioInput.style.resize = 'vertical';
        var nameHint = makeHint('', true);
        var unHint = makeHint('');
        var bioHint = makeHint('', true);
        var statusEl = makeHint('');
        var saveBtn = makeSaveBtn();

        var nameRow = document.createElement('div');
        nameRow.className = 'sd-field-row';
        nameRow.appendChild(makeField(BF.i18n.t('profile.firstName'), fnInput));
        nameRow.appendChild(makeField(BF.i18n.t('profile.lastName'), lnInput));
        form.appendChild(nameRow);
        form.appendChild(nameHint);

        var unWrap = document.createElement('div');
        unWrap.className = 'sd-input-prefixed';
        var at = document.createElement('span');
        at.textContent = '@';
        unWrap.appendChild(at);
        unWrap.appendChild(unInput);
        var unField = makeField(BF.i18n.t('profile.username'), unWrap);
        unField.appendChild(unHint);
        form.appendChild(unField);

        var bioField = makeField(BF.i18n.t('settings.bio'), bioInput);
        bioField.appendChild(bioHint);
        form.appendChild(bioField);
        form.appendChild(saveBtn);
        form.appendChild(statusEl);
        body.appendChild(form);

        var orig = { firstName: '', lastName: '', username: '', bio: '' };
        function fill(user) {
            orig = {
                firstName: user.firstName || '',
                lastName: user.lastName || '',
                username: user.username || '',
                bio: user.bio || ''
            };
            fnInput.value = orig.firstName;
            lnInput.value = orig.lastName;
            unInput.value = orig.username;
            bioInput.value = orig.bio;
        }
        loadCurrentUser().then(function (user) {
            if (user && body.dataset.view === 'account') fill(user);
        });

        function setHint(el, text, isError) {
            el.textContent = text;
            el.className = 'sd-hint' + (isError ? ' error' : '');
        }

        // Debounce username check
        var unTimer = null;
        var unAvailable = true;
        unInput.addEventListener('input', function () {
            clearTimeout(unTimer);
            var val = unInput.value.trim();
            if (!val || val === orig.username) {
                setHint(unHint, '');
                unAvailable = true;
                return;
            }
            if (val.length < 3 || val.length > 32 || !/^[a-zA-Z0-9_]+$/.test(val)) {
                setHint(unHint, BF.i18n.t('profile.username.rules'), true);
                unAvailable = false;
                return;
            }
            unTimer = setTimeout(function () {
                BF.api.checkExistUsername(val).then(function (r) {
                    if (unInput.value.trim() !== val) return;
                    if (r.exist) {
                        setHint(unHint, BF.i18n.t('profile.username.taken'), true);
                        unAvailable = false;
                        BF.sound.play('droplet');
                    } else {
                        setHint(unHint, BF.i18n.t('profile.username.available'));
                        unHint.classList.add('ok');
                        unAvailable = true;
                    }
                });
            }, 500);
        });

        // Имя, юзернейм и биография сохраняются разными вызовами: ошибка одного не отменяет остальные
        // и показывается у своего поля.
        saveBtn.addEventListener('click', function () {
            if (!unAvailable) return;
            var fn = fnInput.value.trim();
            var ln = lnInput.value.trim();
            var un = unInput.value.trim();
            var bio = bioInput.value.trim();
            var steps = [];
            if (fn !== orig.firstName || ln !== orig.lastName) {
                steps.push({ hint: nameHint, run: function () { return BF.api.changeName(fn, ln); } });
            }
            var usernameChanged = un && un !== orig.username;
            if (usernameChanged) {
                steps.push({ hint: unHint, username: true, run: function () { return BF.api.changeUsername(un); } });
            }
            if (bio !== orig.bio) {
                steps.push({ hint: bioHint, run: function () { return BF.api.changeBio(bio); } });
            }
            if (!steps.length) return;

            saveBtn.disabled = true;
            setHint(nameHint, '', true);
            setHint(bioHint, '', true);
            setHint(statusEl, '');
            var failures = [];
            var chain = Promise.resolve();
            steps.forEach(function (step) {
                chain = chain.then(function () {
                    return step.run().catch(function (err) { failures.push({ step: step, err: err }); });
                });
            });
            chain.then(function () {
                return BF.api.getUser(myUserId);
            }).then(function (data) {
                var savedUser = data && data.user;
                if (!savedUser) throw new Error('profile_not_loaded');
                currentUser = savedUser;
                var usernameFailed = failures.some(function (f) { return f.step.username; });
                if (usernameChanged && !usernameFailed && savedUser.username !== un) {
                    failures.push({ step: { hint: unHint, username: true }, err: new Error('username_not_changed') });
                }
                if (body.dataset.view !== 'account') return;
                // Поля с ошибкой сохраняют введённое значение, остальные берут сохранённое с сервера.
                var keep = { fn: fn, ln: ln, un: un, bio: bio };
                fill(savedUser);
                failures.forEach(function (f) {
                    if (f.step.hint === nameHint) { fnInput.value = keep.fn; lnInput.value = keep.ln; }
                    if (f.step.hint === unHint) unInput.value = keep.un;
                    if (f.step.hint === bioHint) bioInput.value = keep.bio;
                    setHint(f.step.hint, failureText(f), true);
                });
                if (failures.length) {
                    BF.sound.play('droplet');
                } else {
                    setHint(statusEl, BF.i18n.t('common.saved'));
                    setTimeout(function () { if (statusEl.textContent === BF.i18n.t('common.saved')) setHint(statusEl, ''); }, 2000);
                }
            }).catch(function () {
                setHint(statusEl, BF.i18n.t('common.error'), true);
            }).then(function () {
                saveBtn.disabled = false;
            });
        });

        function failureText(f) {
            if (f.step.username) {
                if (extractErrorCode(f.err) === ERR_USERNAME_FORMAT) return BF.i18n.t('profile.username.invalidFormat');
                if (f.err && f.err.message === 'username_not_changed') return BF.i18n.t('profile.username.notConfirmed');
            }
            return BF.i18n.t('settings.profile.saveFailed');
        }
    }

    // --- Password ---
    function renderPassword() {
        titleEl.textContent = BF.i18n.t('common.password');
        body.innerHTML = '';

        var form = document.createElement('div');
        form.className = 'sd-form';

        var oldInput = makeInput('password', BF.i18n.t('password.current'), '');
        var newInput = makeInput('password', BF.i18n.t('password.new'), '');
        var repInput = makeInput('password', BF.i18n.t('password.repeat'), '');
        var errEl = makeHint('', true);
        errEl.style.display = 'none';
        var saveBtn = makeSaveBtn(BF.i18n.t('password.change'));

        form.appendChild(makeField(BF.i18n.t('password.current'), oldInput));
        form.appendChild(makeField(BF.i18n.t('password.new'), newInput));
        form.appendChild(makeField(BF.i18n.t('password.repeatNew'), repInput));
        form.appendChild(errEl);
        form.appendChild(saveBtn);
        body.appendChild(form);

        saveBtn.addEventListener('click', function () {
            errEl.style.display = 'none';
            var op = oldInput.value;
            var np = newInput.value;
            var rp = repInput.value;
            if (!np) { showErr(BF.i18n.t('password.error.empty')); return; }
            if (np !== rp) { showErr(BF.i18n.t('password.error.mismatch')); return; }

            saveBtn.disabled = true;
            BF.api.setPassword(np, op || undefined).then(function () {
                saveBtn.disabled = false;
                oldInput.value = '';
                newInput.value = '';
                repInput.value = '';
                var ok = document.createElement('div');
                ok.className = 'sd-hint';
                ok.textContent = BF.i18n.t('password.changed');
                form.appendChild(ok);
                setTimeout(function () { if (ok.parentNode) ok.parentNode.removeChild(ok); }, 2000);
            }).catch(function (err) {
                saveBtn.disabled = false;
                var code = extractErrorCode(err);
                if (code === ERR_WRONG_OLD_PASSWORD) {
                    showErr(BF.i18n.t('password.error.wrongCurrent'));
                } else {
                    showErr(BF.i18n.t('password.error.changeFailed'));
                }
            });

            function showErr(msg) {
                errEl.textContent = msg;
                errEl.style.display = '';
                BF.sound.play('droplet');
            }
        });
    }

    // --- Login mode, factors and verified contacts ---
    var securityCleanup = null;
    function renderTwoFA() {
        titleEl.textContent = BF.i18n.t('security.settingsTitle');
        body.replaceChildren();
        securityCleanup = BF.authUI.renderSecurity(body);
    }

    // --- Sessions ---
    // Подтверждение входа другого устройства по его QR (FastAuth).
    function renderLinkDevice() {
        titleEl.textContent = BF.i18n.t('fastAuthLink.title');
        body.innerHTML = '';
        BF.fastAuthLink.render(body);
        viewCleanup = BF.fastAuthLink.stop;
    }

    function linkDeviceButton() {
        var btn = makeSaveBtn(BF.i18n.t('fastAuthLink.title'));
        btn.type = 'button';
        btn.addEventListener('click', function () { navigate('linkDevice'); });
        return btn;
    }

    function renderSessions() {
        titleEl.textContent = BF.i18n.t('settings.sessions');
        body.innerHTML = skeletonRows(3, SKELETON_SESSION_ROW);

        var currentDeviceId = BF.device ? BF.device.getDeviceId() : null;

        BF.api.getActiveSessions().then(function (data) {
            if (body.dataset.view !== 'sessions') return;
            body.innerHTML = '';
            body.appendChild(linkDeviceButton());
            var sessions = data.sessions || [];
            if (sessions.length === 0) {
                body.innerHTML = '<div class="sd-hint" style="padding:20px">' + BF.i18n.t('sessions.empty') + '</div>';
                return;
            }

            var otherSessions = sessions.filter(function (s) {
                return !(s.deviceId && currentDeviceId && s.deviceId === currentDeviceId);
            });

            if (otherSessions.length > 0) {
                var termAllBtn = document.createElement('button');
                termAllBtn.className = 'sessions-terminate-all';
                termAllBtn.textContent = BF.i18n.t('sessions.terminateOthers', { count: otherSessions.length });
                termAllBtn.addEventListener('click', function () {
                    if (!window.confirm(BF.i18n.t('sessions.terminateOthers.confirm', { count: otherSessions.length }))) return;
                    termAllBtn.disabled = true;
                    var p = Promise.resolve();
                    otherSessions.forEach(function (s) {
                        p = p.then(function () { return BF.api.removeActiveSession(s.deviceId).catch(function () {}); });
                    });
                    p.then(function () { renderSessions(); });
                });
                body.appendChild(termAllBtn);
            }

            sessions.forEach(function (s) { body.appendChild(buildSessionItem(s, currentDeviceId)); });
        }).catch(function () {
            body.innerHTML = '<div class="sd-hint error" style="padding:20px">' + BF.i18n.t('common.loadError') + '</div>';
        });
    }

    function buildSessionItem(s, currentDeviceId) {
        var isCurrent = s.deviceId && currentDeviceId && s.deviceId === currentDeviceId;
        var item = document.createElement('div');
        item.className = 'session-item' + (isCurrent ? ' current' : '');

        var info = document.createElement('div');
        info.className = 'session-info';

        var name = document.createElement('div');
        name.className = 'session-name';
        name.textContent = s.customName || s.originalName || s.appName || BF.i18n.t('sessions.device');

        var meta = document.createElement('div');
        meta.className = 'session-meta';
        var parts = [];
        if (s.operationSystem) parts.push(s.operationSystem);
        if (s.location) parts.push(s.location);
        if (s.createdAt) parts.push(BF.i18n.t('sessions.since', { date: new Date(s.createdAt).toLocaleDateString(BF.i18n.current()) }));
        meta.textContent = parts.join(' · ');

        info.appendChild(name);
        info.appendChild(meta);
        item.appendChild(info);

        if (isCurrent) {
            var badge = document.createElement('span');
            badge.style.cssText = 'font-size:11px;color:var(--primary);font-weight:600;flex-shrink:0;';
            badge.textContent = BF.i18n.t('sessions.thisDevice');
            item.appendChild(badge);
            return item;
        }

        var actions = document.createElement('div');
        actions.className = 'session-actions';

        var renameBtn = document.createElement('button');
        renameBtn.className = 'session-rename';
        renameBtn.textContent = BF.i18n.t('group.rename');
        renameBtn.addEventListener('click', function () {
            if (item.querySelector('.session-rename-input')) return;
            var box = document.createElement('div');
            box.className = 'session-rename-input';
            var input = document.createElement('input');
            input.type = 'text';
            input.maxLength = 64;
            input.value = s.customName || s.originalName || '';
            input.placeholder = BF.i18n.t('sessions.deviceName');
            var ok = document.createElement('button');
            ok.className = 'session-rename';
            ok.textContent = 'OK';
            ok.addEventListener('click', function () {
                var newName = input.value.trim();
                ok.disabled = true;
                BF.api.renameDevice(s.deviceId, newName).then(function () {
                    s.customName = newName;
                    name.textContent = newName || s.originalName || s.appName || BF.i18n.t('sessions.device');
                    info.removeChild(box);
                }).catch(function () { ok.disabled = false; });
            });
            box.appendChild(input);
            box.appendChild(ok);
            info.appendChild(box);
            input.focus();
            input.select();
        });

        var termBtn = document.createElement('button');
        termBtn.className = 'session-terminate';
        termBtn.textContent = BF.i18n.t('sessions.terminate');
        termBtn.addEventListener('click', function () {
            termBtn.disabled = true;
            BF.api.removeActiveSession(s.deviceId).then(function () {
                item.parentNode && item.parentNode.removeChild(item);
            }).catch(function () { termBtn.disabled = false; });
        });

        actions.appendChild(renameBtn);
        actions.appendChild(termBtn);
        item.appendChild(actions);
        return item;
    }

    // --- Privacy ---
    function renderPrivacy() {
        titleEl.textContent = BF.i18n.t('settings.privacy');
        body.innerHTML = skeletonRows(4, SKELETON_TOGGLE_ROW);

        BF.api.getPrivacySettings().then(function (data) {
            var s = data.settings || {
                profileVisibleOnSite: true, avatarVisibility: VIS_ALL,
                bioVisibility: VIS_ALL, emailVisibility: VIS_NONE,
                searchVisible: true, onlineVisibility: VIS_ALL
            };
            body.innerHTML = '';

            var togglesSec = document.createElement('div');
            togglesSec.className = 'sd-section';
            var profileToggle = makeToggleRow(
                BF.i18n.t('privacy.profileVisible'),
                BF.i18n.t('privacy.profileVisible.description', { url: 'barkfluff.com/' + (currentUser && currentUser.username ? currentUser.username : 'username') }),
                s.profileVisibleOnSite, null
            );
            var searchToggle = makeToggleRow(
                BF.i18n.t('privacy.searchable'),
                BF.i18n.t('privacy.searchable.description'),
                s.searchVisible, null
            );
            togglesSec.appendChild(profileToggle.row);
            togglesSec.appendChild(searchToggle.row);
            body.appendChild(togglesSec);

            var segments = [
                { key: 'avatarVisibility', label: BF.i18n.t('privacy.avatarVisibility') },
                { key: 'bioVisibility',    label: BF.i18n.t('privacy.bioVisibility') },
                { key: 'emailVisibility',  label: BF.i18n.t('privacy.emailVisibility') },
                { key: 'onlineVisibility', label: BF.i18n.t('privacy.onlineVisibility') }
            ];
            var segCtrls = {};
            segments.forEach(function (seg) {
                var field = document.createElement('div');
                field.className = 'sd-field';
                field.style.padding = '12px 20px 0';
                var lbl = document.createElement('div');
                lbl.className = 'sd-label';
                lbl.textContent = seg.label;
                field.appendChild(lbl);
                var ctrl = makeSegmented(VIS_OPTIONS, s[seg.key], null);
                segCtrls[seg.key] = ctrl;
                field.appendChild(ctrl.el);
                body.appendChild(field);
            });

            var hint = document.createElement('div');
            hint.className = 'sd-hint';
            hint.style.padding = '12px 20px 0';
            hint.textContent = BF.i18n.t('privacy.friendsHint');
            body.appendChild(hint);

            var btnWrap = document.createElement('div');
            btnWrap.style.padding = '16px 20px 20px';
            var saveBtn = makeSaveBtn();
            btnWrap.appendChild(saveBtn);
            body.appendChild(btnWrap);

            saveBtn.addEventListener('click', function () {
                saveBtn.disabled = true;
                BF.api.updatePrivacySettings({
                    profileVisibleOnSite: profileToggle.getValue(),
                    searchVisible: searchToggle.getValue(),
                    avatarVisibility: segCtrls.avatarVisibility.getValue(),
                    bioVisibility: segCtrls.bioVisibility.getValue(),
                    emailVisibility: segCtrls.emailVisibility.getValue(),
                    onlineVisibility: segCtrls.onlineVisibility.getValue()
                }).then(function () {
                    saveBtn.disabled = false;
                    var ok = document.createElement('div');
                    ok.className = 'sd-hint';
                    ok.style.padding = '6px 20px 0';
                    ok.textContent = BF.i18n.t('common.saved');
                    btnWrap.appendChild(ok);
                    setTimeout(function () { if (ok.parentNode) ok.parentNode.removeChild(ok); }, 2000);
                }).catch(function () {
                    saveBtn.disabled = false;
                    var err = document.createElement('div');
                    err.className = 'sd-hint error';
                    err.style.padding = '6px 20px 0';
                    err.textContent = BF.i18n.t('common.saveError');
                    btnWrap.appendChild(err);
                    setTimeout(function () { if (err.parentNode) err.parentNode.removeChild(err); }, 3000);
                });
            });
        }).catch(function () {
            body.innerHTML = '<div class="sd-hint error" style="padding:20px">' + BF.i18n.t('common.loadError') + '</div>';
        });
    }

    // --- Personalization (poster + chat backgrounds) ---
    function renderPersonalization() {
        titleEl.textContent = BF.i18n.t('settings.section.personalization');
        body.innerHTML =
            '<div class="sd-profile-preview" aria-hidden="true"><span class="skeleton sd-skeleton-poster"></span></div>' +
            skeletonRows(3, SKELETON_TOGGLE_ROW);

        var p = Promise.all([loadCurrentUser(), BF.api.getPersonalization()]);
        p.then(function (results) {
            var user = results[0] || {};
            var data = results[1] || {};
            var pers = (data && data.personalization) || { profilePosterFileId: '', chatBackgroundFileIds: [] };
            // Server is source of truth for poster
            if (user && pers.profilePosterFileId !== undefined) {
                user.profilePosterFileId = pers.profilePosterFileId || '';
            }
            body.innerHTML = '';
            renderPersonalizationContent(user, pers);
        }).catch(function () {
            body.innerHTML = '<div class="sd-hint error" style="padding:20px">' + BF.i18n.t('common.loadError') + '</div>';
        });
    }

    function renderPersonalizationContent(user, pers) {
        // ===== Profile preview =====
        var preview = document.createElement('div');
        preview.className = 'sd-profile-preview';

        var poster = document.createElement('div');
        poster.className = 'sd-profile-preview-poster';
        preview.appendChild(poster);

        var pbody = document.createElement('div');
        pbody.className = 'sd-profile-preview-body';

        var pav = document.createElement('div');
        pav.className = 'sd-profile-preview-avatar';
        if (user && user.profilePicture) {
            var img = document.createElement('img'); img.src = user.profilePicture; img.alt = '';
            pav.appendChild(img);
        } else {
            pav.textContent = ((user && (user.firstName || user.username)) || '?')[0].toUpperCase();
        }
        pbody.appendChild(pav);

        var pname = document.createElement('div');
        pname.className = 'sd-profile-preview-name';
        pname.textContent = (user && ([user.firstName, user.lastName].filter(Boolean).join(' ') || user.username)) || '';
        pbody.appendChild(pname);

        var punm = document.createElement('div');
        punm.className = 'sd-profile-preview-username';
        punm.textContent = user && user.username ? '@' + user.username : '';
        pbody.appendChild(punm);

        preview.appendChild(pbody);

        // Poster actions
        var actions = document.createElement('div');
        actions.className = 'sd-poster-actions';
        var setBtn = document.createElement('button');
        setBtn.className = 'sd-btn sd-btn-primary';
        setBtn.textContent = BF.i18n.t('personalization.poster.set');
        var rmBtn = document.createElement('button');
        rmBtn.className = 'sd-btn-danger';
        rmBtn.textContent = BF.i18n.t('common.delete');
        actions.appendChild(setBtn);
        actions.appendChild(rmBtn);

        var posterStatus = document.createElement('div');
        posterStatus.className = 'sd-hint';
        posterStatus.style.padding = '6px 20px 0';

        body.appendChild(preview);
        body.appendChild(actions);
        body.appendChild(posterStatus);

        function paintPoster(fileId) {
            if (fileId) {
                BF.files.getFileUrls([fileId]).then(function (urls) {
                    var u = urls && urls[0];
                    var url = u && (u.url || u.previewUrl);
                    if (url) {
                        poster.classList.add('has-image');
                        poster.style.setProperty('--preview-poster-image', 'url("' + url + '")');
                    }
                });
                rmBtn.style.display = '';
            } else {
                poster.classList.remove('has-image');
                poster.style.removeProperty('--preview-poster-image');
                rmBtn.style.display = 'none';
            }
        }
        paintPoster(pers.profilePosterFileId || '');

        setBtn.addEventListener('click', function () {
            openPosterFilePicker(function (blob) {
                if (!blob) return;
                posterStatus.textContent = BF.i18n.t('common.loadingShort');
                posterStatus.className = 'sd-hint';
                posterStatus.style.padding = '6px 20px 0';
                BF.files.uploadFile(blob, FT_USER_PROFILE_POSTER).then(function (fileId) {
                    return BF.api.setProfilePoster(fileId).then(function () {
                        pers.profilePosterFileId = fileId;
                        paintPoster(fileId);
                        if (currentUser) currentUser.profilePosterFileId = fileId;
                        posterStatus.textContent = BF.i18n.t('personalization.poster.updated');
                        setTimeout(function () { posterStatus.textContent = ''; }, 2000);
                    });
                }).catch(function () {
                    posterStatus.textContent = BF.i18n.t('common.loadError');
                    posterStatus.className = 'sd-hint error';
                    posterStatus.style.padding = '6px 20px 0';
                });
            });
        });

        rmBtn.addEventListener('click', function () {
            rmBtn.disabled = true;
            BF.api.setProfilePoster('').then(function () {
                pers.profilePosterFileId = '';
                if (currentUser) currentUser.profilePosterFileId = '';
                paintPoster('');
                rmBtn.disabled = false;
            }).catch(function () { rmBtn.disabled = false; });
        });

        // ===== Appearance section =====
        var apHead = document.createElement('div');
        apHead.className = 'sd-section-heading';
        apHead.textContent = BF.i18n.t('personalization.appearance');
        body.appendChild(apHead);

        // Chat preview
        var chatPreview = document.createElement('div');
        chatPreview.className = 'sd-chat-preview';
        var bgEl = document.createElement('div');
        bgEl.className = 'sd-chat-preview-bg';
        chatPreview.appendChild(bgEl);
        var dimEl = document.createElement('div');
        dimEl.className = 'sd-chat-preview-dim';
        chatPreview.appendChild(dimEl);

        var msgs = document.createElement('div');
        msgs.className = 'sd-chat-preview-msgs';
        var mockMsgs = [
            { side: 'incoming', textKey: 'personalization.preview.1' },
            { side: 'outgoing', textKey: 'personalization.preview.2' },
            { side: 'incoming', textKey: 'personalization.preview.3' },
            { side: 'outgoing', textKey: 'personalization.preview.4' },
            { side: 'incoming', textKey: 'personalization.preview.5' }
        ];
        mockMsgs.forEach(function (m) {
            var b = document.createElement('div');
            b.className = 'sd-preview-msg ' + m.side;
            b.textContent = BF.i18n.t(m.textKey);
            msgs.appendChild(b);
        });
        chatPreview.appendChild(msgs);
        body.appendChild(chatPreview);

        // Slider helpers
        function buildSlider(label, min, max, step, initial, unit, onChange) {
            var row = document.createElement('div');
            row.className = 'sd-slider-row';
            var head = document.createElement('div');
            head.className = 'sd-slider-row-header';
            var l = document.createElement('span');
            l.textContent = label;
            var v = document.createElement('span');
            v.className = 'sd-slider-value';
            v.textContent = initial + (unit || '');
            head.appendChild(l);
            head.appendChild(v);
            row.appendChild(head);
            var sl = document.createElement('input');
            sl.type = 'range';
            sl.className = 'sd-slider';
            sl.min = String(min); sl.max = String(max); sl.step = String(step || 1);
            sl.value = String(initial);
            sl.addEventListener('input', function () {
                var n = parseInt(sl.value, 10);
                v.textContent = n + (unit || '');
                onChange(n);
            });
            row.appendChild(sl);
            return { row: row, slider: sl, valueEl: v };
        }

        function applyPreview() {
            var r = BF.personalization.getRadius();
            var blurOn = BF.personalization.getBlurEnabled();
            var blurR = BF.personalization.getBlurRadius();
            var dim = BF.personalization.getDim();
            chatPreview.style.setProperty('--preview-radius', r + 'px');
            chatPreview.style.setProperty('--preview-bg-blur', (blurOn ? blurR : 0) + 'px');
            chatPreview.style.setProperty('--preview-bg-dim', (dim / 100).toFixed(3));
            var bgUrl = BF.personalization.getResolvedBackgroundUrl();
            chatPreview.style.setProperty('--preview-bg-image', bgUrl ? ('url("' + bgUrl + '")') : 'none');
        }

        var radiusCtl = buildSlider(BF.i18n.t('personalization.bubbleRadius'), 0, 20, 1,
            BF.personalization.getRadius(), 'px',
            function (n) { BF.personalization.setRadius(n); applyPreview(); });
        body.appendChild(radiusCtl.row);

        var stickerSizeCtl = buildSlider(BF.i18n.t('personalization.stickerSize'), 96, 240, 1,
            BF.personalization.getStickerSize(), 'px',
            function (n) { BF.personalization.setStickerSize(n); });
        body.appendChild(stickerSizeCtl.row);

        var stickerTabsField = document.createElement('div');
        stickerTabsField.className = 'sd-field';
        stickerTabsField.style.padding = '12px 20px 0';
        var stickerTabsLabel = document.createElement('div');
        stickerTabsLabel.className = 'sd-label';
        stickerTabsLabel.textContent = BF.i18n.t('personalization.stickerTabs');
        stickerTabsField.appendChild(stickerTabsLabel);
        stickerTabsField.appendChild(makeSegmented([
            { value: 'bottom', labelKey: 'personalization.stickerTabs.bottom' },
            { value: 'side', labelKey: 'personalization.stickerTabs.side' }
        ], BF.personalization.getStickerTabsLayout(), function (v) {
            BF.personalization.setStickerTabsLayout(v);
        }).el);
        body.appendChild(stickerTabsField);

        var lastSeenField = document.createElement('div');
        lastSeenField.className = 'sd-field';
        lastSeenField.style.padding = '12px 20px 0';
        var lastSeenLabel = document.createElement('div');
        lastSeenLabel.className = 'sd-label';
        lastSeenLabel.textContent = BF.i18n.t('personalization.lastSeenFormat');
        lastSeenField.appendChild(lastSeenLabel);
        lastSeenField.appendChild(makeSegmented([
            { value: 'relative', labelKey: 'personalization.lastSeenFormat.relative' },
            { value: 'exact', labelKey: 'personalization.lastSeenFormat.exact' }
        ], BF.personalization.getLastSeenFormat(), function (v) {
            BF.personalization.setLastSeenFormat(v);
        }).el);
        body.appendChild(lastSeenField);

        // Blur toggle + slider
        var blurInitial = BF.personalization.getBlurEnabled();
        var blurToggleSec = document.createElement('div');
        blurToggleSec.className = 'sd-section';
        blurToggleSec.style.marginTop = '8px';
        var blurToggle = makeToggleRow(
            BF.i18n.t('personalization.backgroundBlur'),
            BF.i18n.t('personalization.backgroundBlur.description'),
            blurInitial,
            function (next) {
                BF.personalization.setBlurEnabled(next);
                blurSliderCtl.slider.disabled = !next;
                applyPreview();
            }
        );
        blurToggleSec.appendChild(blurToggle.row);
        body.appendChild(blurToggleSec);

        var blurSliderCtl = buildSlider(BF.i18n.t('personalization.blurRadius'), 1, 25, 1,
            BF.personalization.getBlurRadius(), '',
            function (n) { BF.personalization.setBlurRadius(n); applyPreview(); });
        blurSliderCtl.slider.disabled = !blurInitial;
        body.appendChild(blurSliderCtl.row);

        var dimCtl = buildSlider(BF.i18n.t('personalization.backgroundDim'), 0, 100, 1,
            BF.personalization.getDim(), '%',
            function (n) { BF.personalization.setDim(n); applyPreview(); });
        body.appendChild(dimCtl.row);

        // ===== Backgrounds section =====
        var bgHead = document.createElement('div');
        bgHead.className = 'sd-section-heading';
        bgHead.textContent = BF.i18n.t('personalization.globalBackground');
        body.appendChild(bgHead);

        var bgWrap = document.createElement('div');
        bgWrap.style.padding = '8px 20px 24px';
        var grid = document.createElement('div');
        grid.className = 'sd-bg-grid';
        bgWrap.appendChild(grid);

        var bgStatus = document.createElement('div');
        bgStatus.className = 'sd-hint';
        bgStatus.style.marginTop = '8px';
        bgWrap.appendChild(bgStatus);

        var bgInput = document.createElement('input');
        bgInput.type = 'file';
        bgInput.accept = 'image/*';
        bgInput.style.display = 'none';
        bgWrap.appendChild(bgInput);
        body.appendChild(bgWrap);

        function rerenderGrid() {
            grid.innerHTML = '';
            var ids = pers.chatBackgroundFileIds || [];
            var activeId = BF.personalization.getBackgroundFileId();

            // "None" tile
            var none = document.createElement('div');
            none.className = 'sd-bg-card none-card' + (!activeId ? ' active' : '');
            none.textContent = BF.i18n.t('personalization.noBackground');
            none.addEventListener('click', function () {
                BF.personalization.setBackgroundFileId('').then(function () {
                    applyPreview();
                    rerenderGrid();
                });
            });
            grid.appendChild(none);

            ids.forEach(function (fid) {
                var card = document.createElement('div');
                card.className = 'sd-bg-card' + (fid === activeId ? ' active' : '');
                var im = document.createElement('img');
                im.alt = '';
                card.appendChild(im);
                BF.files.getFileUrls([fid]).then(function (urls) {
                    var u = urls && urls[0];
                    if (u) im.src = u.previewUrl || u.url;
                });
                card.addEventListener('click', function (e) {
                    if (e.target.classList.contains('sd-bg-card-remove')) return;
                    BF.personalization.setBackgroundFileId(fid).then(function () {
                        applyPreview();
                        rerenderGrid();
                    });
                });

                var rm = document.createElement('button');
                rm.type = 'button';
                rm.className = 'sd-bg-card-remove';
                rm.innerHTML = BF.icons.html('close');
                rm.title = BF.i18n.t('personalization.removeFromCollection');
                rm.addEventListener('click', function (e) {
                    e.stopPropagation();
                    rm.disabled = true;
                    var nextIds = ids.filter(function (x) { return x !== fid; });
                    BF.api.updatePersonalization({
                        profilePosterFileId: pers.profilePosterFileId || '',
                        chatBackgroundFileIds: nextIds
                    }).then(function () {
                        pers.chatBackgroundFileIds = nextIds;
                        return BF.personalization.reloadSettings();
                    }).then(function () {
                        applyPreview();
                        rerenderGrid();
                    }).catch(function () { rm.disabled = false; });
                });
                card.appendChild(rm);
                grid.appendChild(card);
            });

            var add = document.createElement('div');
            add.className = 'sd-bg-card sd-bg-card-add';
            add.innerHTML = BF.icons.html('add');
            add.title = BF.i18n.t('personalization.addBackground');
            add.addEventListener('click', function () { bgInput.click(); });
            grid.appendChild(add);
        }

        bgInput.addEventListener('change', function () {
            var f = bgInput.files[0];
            if (!f) return;
            bgStatus.textContent = BF.i18n.t('common.loadingShort');
            bgStatus.className = 'sd-hint';
            BF.files.uploadFile(f, FT_MESSAGE_ATTACHMENT_IMAGE).then(function (fileId) {
                var nextIds = (pers.chatBackgroundFileIds || []).concat([fileId]);
                return BF.api.updatePersonalization({
                    profilePosterFileId: pers.profilePosterFileId || '',
                    chatBackgroundFileIds: nextIds
                }).then(function () {
                    pers.chatBackgroundFileIds = nextIds;
                    // Auto-select newly added global background
                    BF.personalization.setBackgroundFileId(fileId).then(applyPreview);
                    rerenderGrid();
                    bgStatus.textContent = BF.i18n.t('personalization.backgroundAdded');
                    setTimeout(function () { bgStatus.textContent = ''; }, 2000);
                });
            }).catch(function () {
                bgStatus.textContent = BF.i18n.t('common.loadError');
                bgStatus.className = 'sd-hint error';
            });
            bgInput.value = '';
        });

        rerenderGrid();
        applyPreview();
    }

    // ===== Poster crop modal =====
    var cropState = null;
    function openPosterFilePicker(onResult) {
        var inp = document.createElement('input');
        inp.type = 'file';
        inp.accept = 'image/*';
        inp.style.display = 'none';
        inp.addEventListener('change', function () {
            var f = inp.files[0];
            if (!f) return;
            openPosterCrop(f, onResult);
        });
        document.body.appendChild(inp);
        inp.click();
        setTimeout(function () { if (inp.parentNode) inp.parentNode.removeChild(inp); }, 100);
    }

    function openPosterCrop(file, onResult) {
        var overlay = document.getElementById('posterCropOverlay');
        var stage = document.getElementById('posterCropStage');
        var imgEl = document.getElementById('posterCropImage');
        var frame = document.getElementById('posterCropFrame');
        var btnCancel = document.getElementById('posterCropCancel');
        var btnSave = document.getElementById('posterCropSave');
        if (!overlay || !stage || !imgEl || !frame) return;

        var url = URL.createObjectURL(file);
        imgEl.onload = function () {
            BF.utils.openOverlay(overlay);
            initCrop();
        };
        imgEl.src = url;

        function cleanup() {
            BF.utils.closeOverlay(overlay);
            URL.revokeObjectURL(url);
            imgEl.src = '';
            btnCancel.onclick = null;
            btnSave.onclick = null;
            if (cropState && cropState.detach) cropState.detach();
            cropState = null;
        }

        function initCrop() {
            // Compute image rect inside stage (object-fit: contain by max-width/max-height)
            var imgRect = imgEl.getBoundingClientRect();
            var stageRect = stage.getBoundingClientRect();
            var ix = imgRect.left - stageRect.left;
            var iy = imgRect.top - stageRect.top;
            var iw = imgRect.width;
            var ih = imgRect.height;

            // Init frame: max width that fits aspect 3:1 inside image area
            var fw = iw;
            var fh = fw / 3;
            if (fh > ih) { fh = ih; fw = fh * 3; }
            var fx = ix + (iw - fw) / 2;
            var fy = iy + (ih - fh) / 2;

            frame.style.left = fx + 'px';
            frame.style.top = fy + 'px';
            frame.style.width = fw + 'px';
            frame.style.height = fh + 'px';

            cropState = {
                bounds: { x: ix, y: iy, w: iw, h: ih },
                naturalW: imgEl.naturalWidth,
                naturalH: imgEl.naturalHeight,
                detach: bindCropDrag(frame, stage)
            };
        }

        btnCancel.onclick = function () { cleanup(); onResult(null); };
        btnSave.onclick = function () {
            if (!cropState) { cleanup(); return; }
            var b = cropState.bounds;
            var fx = parseFloat(frame.style.left) - b.x;
            var fy = parseFloat(frame.style.top) - b.y;
            var fw = parseFloat(frame.style.width);
            var fh = parseFloat(frame.style.height);
            var scale = cropState.naturalW / b.w;
            var sx = Math.max(0, fx * scale);
            var sy = Math.max(0, fy * scale);
            var sw = Math.min(cropState.naturalW - sx, fw * scale);
            var sh = Math.min(cropState.naturalH - sy, fh * scale);

            // Cap to 2400x800 (3:1) like Android
            var outW = Math.min(2400, sw);
            var outH = outW / 3;
            var canvas = document.createElement('canvas');
            canvas.width = Math.round(outW);
            canvas.height = Math.round(outH);
            var ctx = canvas.getContext('2d');
            ctx.drawImage(imgEl, sx, sy, sw, sh, 0, 0, canvas.width, canvas.height);
            canvas.toBlob(function (blob) {
                cleanup();
                if (blob) {
                    var jpeg = new File([blob], 'poster.jpg', { type: 'image/jpeg' });
                    onResult(jpeg);
                } else {
                    onResult(null);
                }
            }, 'image/jpeg', 0.9);
        };
    }

    function bindCropDrag(frame, stage) {
        var dragging = null;

        function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }

        function onDown(e) {
            var target = e.target;
            var handle = target.classList && target.classList.contains('sd-crop-handle') ? target.dataset.handle : null;
            if (target !== frame && !handle) return;
            e.preventDefault();
            var pt = pointFromEvent(e);
            var rect = stage.getBoundingClientRect();
            var b = cropState.bounds;
            dragging = {
                mode: handle ? 'resize-' + handle : 'move',
                startX: pt.clientX,
                startY: pt.clientY,
                origLeft: parseFloat(frame.style.left),
                origTop: parseFloat(frame.style.top),
                origW: parseFloat(frame.style.width),
                origH: parseFloat(frame.style.height),
                stage: rect,
                bounds: b
            };
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
            document.addEventListener('touchmove', onMove, { passive: false });
            document.addEventListener('touchend', onUp);
        }

        function pointFromEvent(e) {
            if (e.touches && e.touches[0]) return { clientX: e.touches[0].clientX, clientY: e.touches[0].clientY };
            return { clientX: e.clientX, clientY: e.clientY };
        }

        function onMove(e) {
            if (!dragging) return;
            e.preventDefault();
            var pt = pointFromEvent(e);
            var dx = pt.clientX - dragging.startX;
            var dy = pt.clientY - dragging.startY;
            var b = dragging.bounds;
            var minSize = 60;

            if (dragging.mode === 'move') {
                var nl = clamp(dragging.origLeft + dx, b.x, b.x + b.w - dragging.origW);
                var nt = clamp(dragging.origTop + dy, b.y, b.y + b.h - dragging.origH);
                frame.style.left = nl + 'px';
                frame.style.top = nt + 'px';
                return;
            }

            // Resize maintaining 3:1 aspect
            var nl2 = dragging.origLeft, nt2 = dragging.origTop;
            var nw = dragging.origW, nh = dragging.origH;
            var m = dragging.mode;

            if (m === 'resize-br') {
                nw = clamp(dragging.origW + dx, minSize, b.x + b.w - dragging.origLeft);
                nh = nw / 3;
                if (nt2 + nh > b.y + b.h) { nh = b.y + b.h - nt2; nw = nh * 3; }
            } else if (m === 'resize-bl') {
                nw = clamp(dragging.origW - dx, minSize, dragging.origLeft + dragging.origW - b.x);
                nh = nw / 3;
                nl2 = dragging.origLeft + dragging.origW - nw;
                if (nt2 + nh > b.y + b.h) { nh = b.y + b.h - nt2; nw = nh * 3; nl2 = dragging.origLeft + dragging.origW - nw; }
            } else if (m === 'resize-tr') {
                nw = clamp(dragging.origW + dx, minSize, b.x + b.w - dragging.origLeft);
                nh = nw / 3;
                nt2 = dragging.origTop + dragging.origH - nh;
                if (nt2 < b.y) { nt2 = b.y; nh = dragging.origTop + dragging.origH - nt2; nw = nh * 3; }
            } else if (m === 'resize-tl') {
                nw = clamp(dragging.origW - dx, minSize, dragging.origLeft + dragging.origW - b.x);
                nh = nw / 3;
                nl2 = dragging.origLeft + dragging.origW - nw;
                nt2 = dragging.origTop + dragging.origH - nh;
                if (nt2 < b.y) { nt2 = b.y; nh = dragging.origTop + dragging.origH - nt2; nw = nh * 3; nl2 = dragging.origLeft + dragging.origW - nw; }
            }

            frame.style.left = nl2 + 'px';
            frame.style.top = nt2 + 'px';
            frame.style.width = nw + 'px';
            frame.style.height = nh + 'px';
        }

        function onUp() {
            dragging = null;
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            document.removeEventListener('touchmove', onMove);
            document.removeEventListener('touchend', onUp);
        }

        stage.addEventListener('mousedown', onDown);
        stage.addEventListener('touchstart', onDown, { passive: false });
        return function () {
            stage.removeEventListener('mousedown', onDown);
            stage.removeEventListener('touchstart', onDown);
        };
    }

    // --- Language ---
    function renderLanguage() {
        titleEl.textContent = BF.i18n.t('settings.language.title');
        body.innerHTML = '';

        var sec = document.createElement('div');
        sec.className = 'sd-section';
        var current = BF.i18n.current();

        BF.i18n.langs().forEach(function (lang) {
            var row = document.createElement('div');
            row.className = 'sd-item';

            var label = document.createElement('span');
            label.className = 'sd-item-label';
            label.textContent = lang.name;

            var check = document.createElement('span');
            check.className = 'sd-item-icon';
            if (lang.code === current) {
                check.innerHTML = BF.icons.html('select');
            }

            row.appendChild(label);
            row.appendChild(check);
            if (lang.code === current) row.setAttribute('aria-current', 'true');
            makeActivatable(row, function () { BF.i18n.setLang(lang.code); });
            sec.appendChild(row);
        });
        body.appendChild(sec);

        var hint = document.createElement('div');
        hint.className = 'sd-hint';
        hint.style.cssText = 'padding: 4px 20px 18px;';
        hint.textContent = BF.i18n.t('settings.language.hint');
        body.appendChild(hint);
    }

    // --- About ---
    function renderAbout() {
        titleEl.textContent = BF.i18n.t('settings.about');
        body.innerHTML = '';

        var dev = window.BF && BF.device ? BF.device : null;
        var rows = [
            { label: BF.i18n.t('about.webVersion'), value: WEB_VERSION },
            { label: BF.i18n.t('about.browser'), value: dev ? dev.getBrowserName() : '—' },
            { label: BF.i18n.t('about.os'), value: dev ? dev.getOsName() : '—' },
            { label: 'Device ID', value: dev ? dev.getDeviceId() : '—' },
            { label: BF.i18n.t('about.server'), value: BF.node.origin() }
        ];

        rows.forEach(function (r) {
            var row = document.createElement('div');
            row.className = 'sd-about-row';
            var l = document.createElement('div');
            l.className = 'sd-about-label';
            l.textContent = r.label;
            var v = document.createElement('div');
            v.className = 'sd-about-value';
            v.textContent = r.value || '—';
            row.appendChild(l);
            row.appendChild(v);
            body.appendChild(row);
        });

        var healthSection = document.createElement('div');
        healthSection.className = 'sd-health';

        var checkBtn = document.createElement('button');
        checkBtn.type = 'button';
        checkBtn.className = 'sd-btn sd-btn-primary';
        checkBtn.textContent = BF.i18n.t('about.checkAvailability');

        var healthResults = document.createElement('div');
        healthResults.className = 'sd-health-results';
        healthResults.setAttribute('aria-live', 'polite');

        healthSection.appendChild(checkBtn);
        healthSection.appendChild(healthResults);
        body.appendChild(healthSection);

        checkBtn.addEventListener('click', function () {
            checkBtn.disabled = true;
            checkBtn.textContent = BF.i18n.t('about.checkingAvailability');
            healthResults.textContent = '';

            if (!BF.health || !BF.health.check) {
                showHealthError(healthResults);
                checkBtn.disabled = false;
                checkBtn.textContent = BF.i18n.t('about.checkAvailability');
                return;
            }

            BF.health.check().then(function (results) {
                renderHealthResults(healthResults, results);
            }).catch(function () {
                showHealthError(healthResults);
            }).finally(function () {
                checkBtn.disabled = false;
                checkBtn.textContent = BF.i18n.t('about.checkAvailability');
            });
        });

        var link = document.createElement('a');
        link.href = 'https://barkfluff.com';
        link.target = '_blank';
        link.rel = 'noopener';
        link.textContent = 'barkfluff.com';
        link.style.cssText = 'display:block;text-align:center;padding:18px;color:var(--primary);font-size:14px;font-weight:600;text-decoration:none;';
        body.appendChild(link);
    }

    function renderHealthResults(container, results) {
        container.textContent = '';
        results.forEach(function (item) {
            var row = document.createElement('div');
            row.className = 'sd-health-row ' + (item.available ? 'available' : 'unavailable');

            var dot = document.createElement('span');
            dot.className = 'sd-health-dot';
            dot.setAttribute('aria-hidden', 'true');

            var name = document.createElement('span');
            name.className = 'sd-health-name';
            name.textContent = item.name;

            var status = document.createElement('span');
            status.className = 'sd-health-status';
            status.textContent = BF.i18n.t(item.available ? 'about.available' : 'about.unavailable');

            var time = document.createElement('span');
            time.className = 'sd-health-time';
            time.textContent = BF.i18n.t('about.requestTime', { value: item.elapsedMs });

            row.appendChild(dot);
            row.appendChild(name);
            row.appendChild(status);
            row.appendChild(time);
            container.appendChild(row);
        });
    }

    function showHealthError(container) {
        var error = document.createElement('div');
        error.className = 'sd-hint error';
        error.textContent = BF.i18n.t('about.checkError');
        container.textContent = '';
        container.appendChild(error);
    }

    window.BF.settings = {
        init: init,
        open: open,
        close: close
    };
})();
