const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function makeEl() {
    var names = [];
    return {
        value: '',
        textContent: '',
        style: {},
        scrollHeight: 20,
        listeners: {},
        focused: false,
        classList: {
            add: function (name) {
                if (names.indexOf(name) < 0) names.push(name);
            },
            remove: function (name) {
                names = names.filter(function (n) {
                    return n !== name;
                });
            },
            contains: function (name) {
                return names.indexOf(name) >= 0;
            }
        },
        addEventListener: function (type, callback) {
            this.listeners[type] = callback;
        },
        focus: function () {
            this.focused = true;
        }
    };
}

function createHarness() {
    var els = {};
    ['#messageInput', '#replyPreviewBar', '#rpbAuthor', '#rpbText', '#editPreviewBar', '#epbText', '#rpbClose', '#epbClose'].forEach(
        function (selector) {
            els[selector] = makeEl();
        }
    );
    var draftCalls = [];
    var BF = {
        api: {
            setTypingStatus: function () {
                return Promise.resolve();
            }
        },
        drafts: {
            set: function (chatId, text, replyId) {
                draftCalls.push([chatId, text, replyId]);
            }
        },
        i18n: {
            t: function (key) {
                return key;
            }
        },
        utils: {
            attachmentEmoji: function () {
                return '';
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            querySelector: function (selector) {
                return els[selector] || null;
            }
        },
        Date: Date,
        Promise: Promise,
        setInterval: function () {},
        clearInterval: function () {}
    });
    context.window = { BF: BF, addEventListener: function () {} };
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/composer.js'), 'utf8'), context);
    var chatListRenders = 0;
    BF.composer.init({
        getCurrentChatId: function () {
            return 7;
        },
        getCurrentChatType: function () {
            return 0;
        },
        getMessages: function () {
            return [];
        },
        getMyUserId: function () {
            return 1;
        },
        getUser: function () {
            return Promise.resolve(null);
        },
        renderChatList: function () {
            chatListRenders++;
        },
        onSubmit: function () {},
        onSendFiles: function () {}
    });
    return {
        BF: BF,
        input: els['#messageInput'],
        editBar: els['#editPreviewBar'],
        replyBar: els['#replyPreviewBar'],
        draftCalls: draftCalls,
        renders: function () {
            return chatListRenders;
        }
    };
}

function test(name, fn) {
    fn();
    console.log('PASS: ' + name);
}

test('deleting the message being edited ends edit mode and keeps the typed text', function () {
    var h = createHarness();
    h.BF.composer.setEdit({ id: 42, senderId: 1, content: { text: 'original' } });
    assert.equal(h.BF.composer.getEdit().messageId, 42);
    assert.equal(h.editBar.classList.contains('visible'), true);
    h.input.value = 'original, corrected';

    h.BF.composer.onMessageDeleted(42);
    assert.equal(h.BF.composer.getEdit(), null);
    assert.equal(h.editBar.classList.contains('visible'), false, 'the edit bar is hidden');
    assert.equal(h.input.value, 'original, corrected', 'typed text is not lost');
    assert.deepEqual(JSON.parse(JSON.stringify(h.draftCalls[h.draftCalls.length - 1])), [7, 'original, corrected', 0]);
});

test('deleting the message being replied to clears the reply and its draft link', function () {
    var h = createHarness();
    h.BF.composer.setReply({ id: 50, senderId: 1, content: { text: 'quoted' } });
    assert.equal(h.BF.composer.getReplyToId(), 50);
    assert.equal(h.replyBar.classList.contains('visible'), true);
    h.input.value = 'my answer';

    h.BF.composer.onMessageDeleted('50'); // realtime может прислать id строкой
    assert.equal(h.BF.composer.getReplyToId(), 0);
    assert.equal(h.replyBar.classList.contains('visible'), false);
    assert.equal(h.input.value, 'my answer');
    assert.deepEqual(JSON.parse(JSON.stringify(h.draftCalls[h.draftCalls.length - 1])), [7, 'my answer', 0]);
});

test('deleting an unrelated message leaves edit and reply untouched', function () {
    var h = createHarness();
    h.BF.composer.setReply({ id: 60, senderId: 1, content: { text: 'quoted' } });
    var draftsBefore = h.draftCalls.length;
    h.BF.composer.onMessageDeleted(61);
    assert.equal(h.BF.composer.getReplyToId(), 60);
    assert.equal(h.replyBar.classList.contains('visible'), true);
    assert.equal(h.draftCalls.length, draftsBefore, 'no draft write for an unrelated deletion');

    h.BF.composer.clearReply(false);
    h.BF.composer.setEdit({ id: 70, senderId: 1, content: { text: 'x' } });
    h.BF.composer.onMessageDeleted(71);
    assert.equal(h.BF.composer.getEdit().messageId, 70);
    assert.equal(h.editBar.classList.contains('visible'), true);
});
