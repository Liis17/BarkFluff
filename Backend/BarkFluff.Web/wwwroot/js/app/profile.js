/**
 * User profile panel (#profileOverlay): profile card, badges, media tabs of the open chat, "write a message",
 * chat background button and copy-to-clipboard for the ids.
 * Requires: BF.api, BF.files, BF.i18n, BF.utils, BF.chatMedia, BF.chatBackground, BF.sound
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

            if (profilePoster) {
                BF.files.loadResilientBackground(profilePoster, user.profilePosterFileId, false);
            }

            var initial = (user.firstName || user.username || '?')[0].toUpperCase();
            if (user.profilePicture) {
                var avImg = document.createElement('img');
                avImg.src = user.profilePicture;
                avImg.alt = '';
                profileAvatar.replaceChildren(avImg);
            } else {
                profileAvatar.textContent = initial;
            }
            profileName.textContent = [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username;
            if (user.isBot) profileName.insertAdjacentHTML('beforeend', deps.botBadgeMarkup());
            profileUsername.textContent = user.username ? '@' + user.username : '';
            profileBio.textContent = user.bio || '';
            profileBio.style.display = user.bio ? 'block' : 'none';

            var online = deps.isUserOnline(userId);
            var entry = deps.getOnlineEntry(userId);
            profileStatus.textContent = online
                ? BF.i18n.t('status.online')
                : BF.utils.formatLastSeen(entry ? entry.lastSeen : null);
            profileStatus.className = 'profile-status-line' + (online ? ' online' : '');
            profileStatus.hidden = !!user.isBot;
            deps.setCallButtonsVisible(!user.isBot);

            var _profileUserId = $('#profileUserId');
            var _profileChatId = $('#profileChatId');
            if (_profileUserId) _profileUserId.textContent = user.id;
            if (_profileChatId) _profileChatId.textContent = deps.getCurrentChatId() || '—';

            if (user.registrationDate) {
                profileRegDate.textContent = new Date(user.registrationDate).toLocaleDateString('ru-RU', {
                    day: 'numeric',
                    month: 'long',
                    year: 'numeric'
                });
            } else {
                profileRegDate.textContent = '—';
            }

            profileBadges.innerHTML = '';
            if (user.badges && user.badges.length > 0) {
                user.badges.forEach(function (b) {
                    var el = document.createElement('div');
                    el.className = 'profile-badge';
                    if (b.imageUrl) {
                        var bImg = document.createElement('img');
                        bImg.src = b.imageUrl;
                        bImg.alt = '';
                        el.appendChild(bImg);
                    }
                    el.appendChild(document.createTextNode(b.name || ''));
                    profileBadges.appendChild(el);
                });
            }

            loadMedia('media');
            BF.utils.openOverlay(profileOverlay);
        });
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
