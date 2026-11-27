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
    var badgeTooltip = null;
    var badgeTooltipImage;
    var badgeTooltipName;
    var badgeTooltipDesc;
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

    // Подсказка с полным изображением, названием и описанием бейджа: одна на модалку, позиционируется над бейджем.
    function ensureBadgeTooltip() {
        if (badgeTooltip) return badgeTooltip;
        badgeTooltip = document.createElement('div');
        badgeTooltip.className = 'profile-badge-tooltip';
        badgeTooltip.id = 'profileBadgeTooltip';
        badgeTooltip.setAttribute('role', 'tooltip');
        badgeTooltip.hidden = true;
        // Размер задан в CSS, поэтому позиция верна ещё до загрузки изображения.
        badgeTooltipImage = document.createElement('img');
        badgeTooltipImage.className = 'profile-badge-tooltip-image';
        badgeTooltipImage.alt = '';
        badgeTooltipImage.decoding = 'async';
        badgeTooltipName = document.createElement('div');
        badgeTooltipName.className = 'profile-badge-tooltip-name';
        badgeTooltipDesc = document.createElement('div');
        badgeTooltipDesc.className = 'profile-badge-tooltip-desc';
        badgeTooltip.appendChild(badgeTooltipImage);
        badgeTooltip.appendChild(badgeTooltipName);
        badgeTooltip.appendChild(badgeTooltipDesc);
        document.body.appendChild(badgeTooltip);
        return badgeTooltip;
    }

    function showBadgeTooltip(button, badge) {
        var imageUrl = badge.imageUrl || badge.miniImageUrl || '';
        if (!imageUrl && !badge.name && !badge.description) return;
        var tip = ensureBadgeTooltip();
        badgeTooltipImage.src = imageUrl;
        badgeTooltipImage.hidden = !imageUrl;
        badgeTooltipName.textContent = badge.name || '';
        badgeTooltipDesc.textContent = badge.description || '';
        tip.classList.remove('visible');
        tip.hidden = false;
        var r = button.getBoundingClientRect();
        var t = tip.getBoundingClientRect();
        var margin = 8;
        var left = Math.min(Math.max(r.left + r.width / 2 - t.width / 2, margin), window.innerWidth - t.width - margin);
        var top = r.top - t.height - margin;
        if (top < margin) top = r.bottom + margin;
        tip.style.left = left + 'px';
        tip.style.top = top + 'px';
        requestAnimationFrame(function () {
            if (!tip.hidden) tip.classList.add('visible');
        });
    }

    function hideBadgeTooltip() {
        if (!badgeTooltip) return;
        badgeTooltip.classList.remove('visible');
        badgeTooltip.hidden = true;
    }

    function badgeButton(badge, className) {
        var button = document.createElement('button');
        button.type = 'button';
        button.className = className;
        button.setAttribute('aria-label', badge.name || '');
        button.setAttribute('aria-describedby', 'profileBadgeTooltip');
        var image = BF.userBadges.miniImage(badge);
        image.removeAttribute('title');
        button.appendChild(image);
        button.addEventListener('mouseenter', function () {
            showBadgeTooltip(button, badge);
        });
        button.addEventListener('focus', function () {
            showBadgeTooltip(button, badge);
        });
        button.addEventListener('mouseleave', hideBadgeTooltip);
        button.addEventListener('blur', hideBadgeTooltip);
        button.addEventListener('click', function () {
            hideBadgeTooltip();
            BF.userBadges.showViewer(badge);
        });
        return button;
    }

    // Основной бейдж — рядом с именем, остальные — в карточке «Бейджи»; без остальных карточка скрыта.
    function renderBadges(badges) {
        hideBadgeTooltip();
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
            hideBadgeTooltip();
            BF.utils.closeOverlay(profileOverlay);
        });
        profileOverlay.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') hideBadgeTooltip();
        });
        var profilePanel = $('#profileOverlay .profile-panel');
        if (profilePanel) profilePanel.addEventListener('scroll', hideBadgeTooltip, { passive: true });
        profileOverlay.addEventListener('click', function (e) {
            if (e.target === profileOverlay) {
                hideBadgeTooltip();
                BF.utils.closeOverlay(profileOverlay);
            }
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
