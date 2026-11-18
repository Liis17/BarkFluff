const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function createHarness() {
    var buttons = {};
    [
        'btnCallAudio',
        'btnCallVideo',
        'profileCallAudioBtn',
        'profileCallVideoBtn',
        'chatCallBanner',
        'chatCallBannerText',
        'chatCallBannerJoin'
    ].forEach(function (id) {
        buttons[id] = {
            hidden: false,
            listeners: {},
            addEventListener: function (type, callback) {
                this.listeners[type] = callback;
            }
        };
    });
    var initiated = [];
    var joined = [];
    var activeCallRequests = [];
    var callListeners = {};
    var permissionCalls = [];
    var errors = [];
    var permissionResults = [];
    var state = { chatId: 7, chatInfo: null, peerIsBot: false, currentCall: null };
    var BF = {
        calls: {
            MediaType: { AUDIO: 'audio', VIDEO: 'video' },
            initiate: function (target, media) {
                initiated.push({ target: target, media: media });
                return Promise.resolve();
            },
            join: function (callId, chatId, media) {
                joined.push([callId, chatId, media]);
                return Promise.resolve();
            },
            on: function (event, callback) {
                callListeners[event] = callback;
            },
            getCurrent: function () {
                return state.currentCall;
            }
        },
        api: {
            getActiveCalls: function (chatIds) {
                return new Promise(function (resolve, reject) {
                    activeCallRequests.push({ chatIds: chatIds, resolve: resolve, reject: reject });
                });
            }
        },
        i18n: {
            tp: function (key, count) {
                return key + ':' + count;
            }
        },
        callsUI: {
            ensureMediaPermissions: function (media) {
                permissionCalls.push(media);
                // По умолчанию доступ есть; тест подкладывает отложенный или отклонённый результат.
                return permissionResults.length ? permissionResults.shift() : Promise.resolve();
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            getElementById: function (id) {
                return buttons[id] || null;
            }
        },
        Promise: Promise,
        console: {
            error: function () {
                errors.push(Array.from(arguments));
            }
        }
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/chat-calls.js'), 'utf8'), context);
    BF.chatCalls.init({
        getCurrentChatId: function () {
            return state.chatId;
        },
        getCurrentChatInfo: function () {
            return state.chatInfo;
        },
        getPeerIsBot: function () {
            return state.peerIsBot;
        },
        getMyUserId: function () {
            return 1;
        }
    });
    return {
        BF: BF,
        buttons: buttons,
        state: state,
        initiated: initiated,
        joined: joined,
        activeCallRequests: activeCallRequests,
        emitCall: function (event) {
            callListeners[event]({});
        },
        permissionCalls: permissionCalls,
        errors: errors,
        queuePermission: function (promise) {
            permissionResults.push(promise);
        }
    };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('a group chat is called by chat id, a private chat by the peer user id', async function () {
        var h = createHarness();
        h.state.chatInfo = { isGroupChat: true };
        h.buttons.btnCallVideo.listeners.click();
        await settle();
        assert.deepEqual(JSON.parse(JSON.stringify(h.initiated)), [{ target: { chatId: 7 }, media: 'video' }]);

        h.state.chatInfo = { isGroupChat: false, membersId: [1, 42] };
        h.buttons.profileCallAudioBtn.listeners.click(); // кнопка в профиле работает так же
        await settle();
        assert.deepEqual(JSON.parse(JSON.stringify(h.initiated[1])), { target: { userId: 42 }, media: 'audio' });
    });

    await test('no call without a chat, chat info, a peer, or with a bot peer', async function () {
        var h = createHarness();
        h.buttons.btnCallAudio.listeners.click(); // chatInfo ещё не загружен
        h.state.chatInfo = { isGroupChat: false, membersId: [1] }; // собеседника нет
        h.buttons.btnCallAudio.listeners.click();
        h.state.chatInfo = { isGroupChat: false, membersId: [1, 5] };
        h.state.peerIsBot = true;
        h.buttons.btnCallAudio.listeners.click();
        h.state.peerIsBot = false;
        h.state.chatId = null;
        h.buttons.btnCallAudio.listeners.click();
        await settle();
        assert.equal(h.initiated.length, 0);
        assert.equal(h.permissionCalls.length, 0, 'the permission prompt is not shown either');
    });

    await test('a second click while a call is being started is ignored, then calling works again', async function () {
        var h = createHarness();
        h.state.chatInfo = { isGroupChat: true };
        var release;
        h.queuePermission(
            new Promise(function (resolve) {
                release = resolve;
            })
        );
        h.buttons.btnCallAudio.listeners.click();
        h.buttons.btnCallVideo.listeners.click();
        assert.deepEqual(h.permissionCalls, ['audio'], 'only the first click asks for permissions');
        release();
        await settle();
        assert.equal(h.initiated.length, 1);
        h.buttons.btnCallVideo.listeners.click();
        await settle();
        assert.equal(h.initiated.length, 2);
    });

    await test('a dismissed permission prompt is silent, any other failure is logged; both unlock the buttons', async function () {
        var h = createHarness();
        h.state.chatInfo = { isGroupChat: true };
        h.queuePermission(Promise.reject({ code: 'media-permission-dismissed' }));
        h.buttons.btnCallAudio.listeners.click();
        await settle();
        assert.equal(h.errors.length, 0);
        assert.equal(h.initiated.length, 0);

        h.queuePermission(Promise.reject(new Error('boom')));
        h.buttons.btnCallAudio.listeners.click();
        await settle();
        assert.equal(h.errors.length, 1);

        h.buttons.btnCallAudio.listeners.click();
        await settle();
        assert.equal(h.initiated.length, 1, 'the guard is released after failures');
    });

    await test('header and profile buttons are shown or hidden independently', async function () {
        var h = createHarness();
        h.BF.chatCalls.setChatButtonsVisible(false);
        assert.equal(h.buttons.btnCallAudio.hidden, true);
        assert.equal(h.buttons.btnCallVideo.hidden, true);
        assert.equal(h.buttons.profileCallAudioBtn.hidden, false);
        h.BF.chatCalls.setProfileButtonsVisible(false);
        assert.equal(h.buttons.profileCallVideoBtn.hidden, true);
        h.BF.chatCalls.setChatButtonsVisible(true);
        assert.equal(h.buttons.btnCallAudio.hidden, false);
    });

    function activeCall(overrides) {
        return Object.assign({ callId: 'call-1', chatId: 7, mediaType: 'audio', participantUserIds: [2, 3] }, overrides);
    }

    await test('a running group call shows the join banner; joining asks for permissions and calls JoinCall', async function () {
        var h = createHarness();
        h.state.chatInfo = { isGroupChat: true };
        h.BF.chatCalls.refreshJoinBanner();
        assert.equal(h.buttons.chatCallBanner.hidden, true, 'hidden until Calls answers');
        assert.deepEqual(Array.from(h.activeCallRequests[0].chatIds), [7]);
        h.activeCallRequests[0].resolve({ calls: [activeCall({ chatId: '7' })] });
        await settle();
        assert.equal(h.buttons.chatCallBanner.hidden, false);
        assert.equal(h.buttons.chatCallBannerText.textContent, 'call.activeBanner:2');

        h.buttons.chatCallBannerJoin.listeners.click();
        await settle();
        assert.deepEqual(h.permissionCalls, ['audio']);
        assert.deepEqual(h.joined, [['call-1', 7, 'audio']]);
        assert.equal(h.buttons.chatCallBanner.hidden, true);
    });

    await test('no banner for personal chats, without an active call, or while already in a call', async function () {
        var h = createHarness();
        h.state.chatInfo = { isGroupChat: false, membersId: [1, 2] };
        h.BF.chatCalls.refreshJoinBanner();
        assert.equal(h.activeCallRequests.length, 0);

        h.state.chatInfo = { isGroupChat: true };
        h.BF.chatCalls.refreshJoinBanner();
        h.activeCallRequests[0].resolve({ calls: [] });
        await settle();
        assert.equal(h.buttons.chatCallBanner.hidden, true);

        h.state.currentCall = { callId: 'mine' };
        h.emitCall('ended');
        assert.equal(h.activeCallRequests.length, 1, 'no request while the user is in a call');
    });

    await test('a response for a chat that is no longer open is dropped', async function () {
        var h = createHarness();
        h.state.chatInfo = { isGroupChat: true };
        h.BF.chatCalls.refreshJoinBanner();
        h.state.chatId = 8;
        h.BF.chatCalls.hideJoinBanner();
        h.activeCallRequests[0].resolve({ calls: [activeCall()] });
        await settle();
        assert.equal(h.buttons.chatCallBanner.hidden, true);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
