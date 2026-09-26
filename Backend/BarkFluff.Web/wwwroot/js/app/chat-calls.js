/**
 * Call buttons of the chat header and the profile panel: starting an audio/video call (1-on-1 with the peer,
 * or a group call for the chat) and showing/hiding the buttons (hidden for bot chats).
 * Requires: BF.calls, BF.callsUI
 * Exposes: BF.chatCalls
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var isInitiatingCall = false;

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
                if (!e || e.code !== 'media-permission-dismissed') console.error('call start failed:', e);
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
    }

    window.BF.chatCalls = {
        init: init,
        start: startCall,
        setChatButtonsVisible: setChatButtonsVisible,
        setProfileButtonsVisible: setProfileButtonsVisible
    };
})();
