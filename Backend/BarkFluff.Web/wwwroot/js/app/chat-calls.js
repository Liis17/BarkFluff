/**
 * Call buttons of the chat header and the profile panel: starting an audio/video call (1-on-1 with the peer,
 * or a group call for the chat) and showing/hiding the buttons (hidden for bot chats), plus the banner
 * for joining a group call that is already running in the open chat (GetActiveCalls → JoinCall).
 * Requires: BF.calls, BF.callsUI, BF.api, BF.i18n
 * Exposes: BF.chatCalls
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var isInitiatingCall = false;
    var banner = null;
    var bannerText = null;
    var bannerJoin = null;
    var bannerCall = null; // { callId, chatId, mediaType }
    var bannerToken = 0;

    function hideJoinBanner() {
        bannerToken++;
        bannerCall = null;
        if (banner) banner.hidden = true;
    }

    // Баннер только для открытого группового чата, пока пользователь сам не в звонке.
    function refreshJoinBanner() {
        var chatId = deps.getCurrentChatId();
        var chatInfo = deps.getCurrentChatInfo();
        hideJoinBanner();
        if (!banner || !chatId || !chatInfo || !chatInfo.isGroupChat || BF.calls.getCurrent()) return;
        var token = bannerToken;
        BF.api
            .getActiveCalls([chatId])
            .then(function (data) {
                if (token !== bannerToken || chatId !== deps.getCurrentChatId() || BF.calls.getCurrent()) return;
                var call = (data.calls || []).find(function (c) {
                    return String(c.chatId) === String(chatId);
                });
                if (!call) return;
                bannerCall = { callId: call.callId, chatId: chatId, mediaType: call.mediaType };
                bannerText.textContent = BF.i18n.tp('call.activeBanner', call.participantUserIds.length);
                bannerJoin.disabled = false;
                banner.hidden = false;
            })
            .catch(function () {
                // Баннер необязателен: без ответа Calls просто не показываем его.
            });
    }

    function joinBannerCall() {
        var call = bannerCall;
        if (!call || isInitiatingCall) return;
        isInitiatingCall = true;
        bannerJoin.disabled = true;
        BF.callsUI
            .ensureMediaPermissions(call.mediaType)
            .then(function () {
                return BF.calls.join(call.callId, call.chatId, call.mediaType);
            })
            .then(hideJoinBanner)
            .catch(function (e) {
                bannerJoin.disabled = false;
                if (!e || e.code !== 'media-permission-dismissed') {
                    console.error('call join failed:', e);
                    // Звонок мог закончиться, пока баннер был на экране.
                    refreshJoinBanner();
                }
            })
            .finally(function () {
                isInitiatingCall = false;
            });
    }

    function setButtonsVisible(ids, visible) {
        ids.forEach(function (id) {
            var button = document.getElementById(id);
            if (button) button.hidden = !visible;
        });
    }

    function setChatButtonsVisible(visible) {
        setButtonsVisible(['btnCallAudio', 'btnCallVideo'], visible);
    }

    function setProfileButtonsVisible(visible) {
        setButtonsVisible(['profileCallAudioBtn', 'profileCallVideoBtn'], visible);
    }

    function startCall(media) {
        var chatId = deps.getCurrentChatId();
        var chatInfo = deps.getCurrentChatInfo();
        if (isInitiatingCall || !chatId || !chatInfo) return;
        if (deps.getPeerIsRestricted && deps.getPeerIsRestricted()) {
            deps.showToast(BF.i18n.t('accountRestriction.userUnavailable'), true);
            return;
        }
        if (!chatInfo.isGroupChat && deps.getPeerIsBot()) return;
        var target;
        if (chatInfo.isGroupChat) {
            target = { chatId: chatId };
        } else {
            var myUserId = deps.getMyUserId();
            var peerId = (chatInfo.membersId || []).find(function (id) {
                return id !== myUserId;
            });
            if (!peerId) return;
            target = { userId: peerId };
        }
        isInitiatingCall = true;
        BF.callsUI
            .ensureMediaPermissions(media)
            .then(function () {
                return BF.calls.initiate(target, media);
            })
            .catch(function (e) {
                if (e && e.errorCode === '6BC0E7A1-5117-4910-9717-1784E6921F6F') {
                    deps.showToast(BF.i18n.t('accountRestriction.userUnavailable'), true);
                } else if (!e || e.code !== 'media-permission-dismissed') console.error('call start failed:', e);
            })
            .finally(function () {
                isInitiatingCall = false;
            });
    }

    function init(options) {
        deps = options;
        [
            ['btnCallAudio', 'AUDIO'],
            ['btnCallVideo', 'VIDEO'],
            ['profileCallAudioBtn', 'AUDIO'],
            ['profileCallVideoBtn', 'VIDEO']
        ].forEach(function (pair) {
            var button = document.getElementById(pair[0]);
            if (button)
                button.addEventListener('click', function () {
                    startCall(BF.calls.MediaType[pair[1]]);
                });
        });

        banner = document.getElementById('chatCallBanner');
        bannerText = document.getElementById('chatCallBannerText');
        bannerJoin = document.getElementById('chatCallBannerJoin');
        if (bannerJoin) bannerJoin.addEventListener('click', joinBannerCall);
        // Входящий групповой ринг, начало и конец своего звонка меняют актуальность баннера.
        ['incoming', 'connect', 'ended', 'ring_dismiss'].forEach(function (event) {
            BF.calls.on(event, refreshJoinBanner);
        });
        if (BF.realtime) BF.realtime.on('tab_visible', refreshJoinBanner);
    }

    window.BF.chatCalls = {
        init: init,
        start: startCall,
        refreshJoinBanner: refreshJoinBanner,
        hideJoinBanner: hideJoinBanner,
        setChatButtonsVisible: setChatButtonsVisible,
        setProfileButtonsVisible: setProfileButtonsVisible
    };
})();
