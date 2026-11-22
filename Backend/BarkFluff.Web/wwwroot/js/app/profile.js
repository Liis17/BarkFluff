/**
 * User profile panel (#profileOverlay): profile card, badges, media tabs of the open chat, "write a message",
 * chat background button and copy-to-clipboard for the ids.
 * Requires: BF.api, BF.files, BF.i18n, BF.utils, BF.userBadges, BF.chatMedia, BF.chatBackground, BF.sound
 * Exposes: BF.profile
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var profileOverlay;
    var profilePoster;
    var profileAvatar;
    var profileName;
    var profileUsername;
    var profileStatus;
    var profileBio;
    var profileBadges;
    var profileBadgesCard;
    var profilePrimaryBadge = null;
    var profileRegDate;
    var profileMediaPanels;
    var currentProfileUserId = null;

    function $(selector) {
        return document.querySelector(selector);
    }

    function loadMedia(type) {
        BF.chatMedia.setTabActive('#profileOverlay .profile-media-tab', profileMediaPanels, type);
        BF.chatMedia.render(type, profileMediaPanels);
    }

    function open(userId) {
        if (!userId) return;
        currentProfileUserId = userId;
        if (profilePoster) BF.files.loadResilientBackground(profilePoster, null, false);

        BF.api.getUser(userId).then(function (d) {
            if (currentProfileUserId !== userId) return;
            if (!d || !d.user) return;
            var user = d.user;

            var restricted = BF.utils.isRestrictedUser(user);
            var displayName = restricted
                ? BF.utils.restrictedUserName(user)
                : [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username;
            if (profilePoster) {
                BF.files.loadResilientBackground(profilePoster, restricted ? null : user.profilePosterFileId, false);
            }

            var initial = (displayName || '?')[0].toUpperCase();
            if (!restricted && user.profilePicture) {
                var avImg = document.createElement('img');
                avImg.src = user.profilePicture;
                avImg.alt = '';
                profileAvatar.replaceChildren(avImg);
            } else {
                profileAvatar.textContent = initial;
            }
            profileName.textContent = displayName;
            if (user.isBot && !restricted) profileName.insertAdjacentHTML('beforeend', deps.botBadgeMarkup());
            profileUsername.textContent = !restricted && user.username ? '@' + user.username : '';
            profileBio.textContent = restricted ? '' : user.bio || '';
            profileBio.style.display = !restricted && user.bio ? 'block' : 'none';

            var online = deps.isUserOnline(userId);
            var entry = deps.getOnlineEntry(userId);
            profileStatus.textContent = restricted
                ? ''
                : online
                  ? BF.i18n.t('status.online')
                  : BF.utils.formatLastSeen(entry ? entry.lastSeen : null);
            profileStatus.className = 'profile-status-line' + (!restricted && online ? ' online' : '');
            profileStatus.hidden = restricted || !!user.isBot;
            deps.setCallButtonsVisible(!restricted && !user.isBot);

            var _profileUserId = $('#profileUserId');
            var _profileChatId = $('#profileChatId');
            if (_profileUserId) _profileUserId.textContent = user.id;
            if (_profileChatId) _profileChatId.textContent = deps.getCurrentChatId() || '—';

            if (!restricted && user.registrationDate) {
                profileRegDate.textContent = new Date(user.registrationDate).toLocaleDateString('ru-RU', {
                    day: 'numeric',
                    month: 'long',
                    year: 'numeric'
                });
            } else {
                profileRegDate.textContent = '—';
            }

            renderBadges(restricted ? [] : BF.userBadges.cached(userId) || []);
            if (!restricted) {
                // Как на Android: сначала кеш, затем свежий список с сервера.
                BF.userBadges.load(userId, true).then(function (badges) {
                    if (currentProfileUserId === userId) renderBadges(badges);
                });
            }

            loadMedia('media');
            BF.utils.openOverlay(profileOverlay);
        });
    }

    function badgeButton(badge, className) {
        var button = document.createElement('button');
        button.type = 'button';
        button.className = className;
        button.title = badge.name || '';
        button.setAttribute('aria-label', badge.name || '');
        button.appendChild(BF.userBadges.miniImage(badge));
        button.addEventListener('click', function () {
            BF.userBadges.showViewer(badge);
        });
        return button;
    }

    // Основной бейдж — рядом с именем, остальные — в карточке «Бейджи»; без остальных карточка скрыта.
    function renderBadges(badges) {
        var primary = BF.userBadges.primaryOf(badges);
        if (profilePrimaryBadge && profilePrimaryBadge.parentNode)
            profilePrimaryBadge.parentNode.removeChild(profilePrimaryBadge);
        profilePrimaryBadge = null;
        if (primary) {
            profilePrimaryBadge = badgeButton(primary, 'profile-primary-badge');
            profileName.appendChild(profilePrimaryBadge);
        }
        var others = badges.filter(function (b) {
            return !b.isPrimary;
        });
        profileBadges.replaceChildren.apply(
            profileBadges,
            others.map(function (b) {
                return badgeButton(b, 'profile-badge');
            })
        );
        if (profileBadgesCard) profileBadgesCard.hidden = others.length === 0;
    }

    function copyText(text) {
        if (!text || !navigator.clipboard) return;
        navigator.clipboard
            .writeText(String(text))
            .then(function () {
                BF.sound.play('success');
                deps.showToast(BF.i18n.t('common.copied'), false);
            })
            .catch(function () {
                deps.showToast(BF.i18n.t('error.copy'), true);
            });
    }

    function init(options) {
        deps = options;
        profileOverlay = $('#profileOverlay');
        profilePoster = $('#profilePoster');
        profileAvatar = $('#profileAvatar');
        profileName = $('#profileName');
        profileUsername = $('#profileUsername');
        profileStatus = $('#profileStatus');
        profileBio = $('#profileBio');
        profileBadges = $('#profileBadges');
        profileBadgesCard = $('#profileBadgesCard');
        profileRegDate = $('#profileRegDate');
        profileMediaPanels = BF.chatMedia.createPanels($('#profileMediaContent'));

        document.querySelectorAll('#profileOverlay .profile-media-tab').forEach(function (tab) {
            tab.addEventListener('click', function () {
                loadMedia(tab.dataset.type);
            });
        });

        $('#profileClose').addEventListener('click', function () {
            BF.utils.closeOverlay(profileOverlay);
        });
        profileOverlay.addEventListener('click', function (e) {
            if (e.target === profileOverlay) BF.utils.closeOverlay(profileOverlay);
        });

        var msgBtn = $('#profileMsgBtn');
        if (msgBtn)
            msgBtn.addEventListener('click', function () {
                BF.utils.closeOverlay(profileOverlay);
            });
        var backgroundBtn = $('#profileBackgroundButton');
        if (backgroundBtn)
            backgroundBtn.addEventListener('click', function () {
                var info = deps.getCurrentChatInfo();
                BF.chatBackground.open(deps.getCurrentChatId(), info && info.title);
            });

        document.querySelectorAll('.profile-info-copy').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var target = document.getElementById(btn.dataset.copy);
                if (target) copyText(target.textContent);
            });
        });
    }

    window.BF.profile = { init: init, open: open };
})();
