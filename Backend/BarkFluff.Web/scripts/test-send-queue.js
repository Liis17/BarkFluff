const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 30; i++) await new Promise((resolve) => setImmediate(resolve));
}

// Objects/arrays built inside the vm sandbox belong to a different realm; a JSON round-trip strips
// that so assert.deepEqual can compare them against plain literals from this file's realm.
function plain(value) {
    return JSON.parse(JSON.stringify(value));
}

function makeEl(id) {
    return {
        id: id,
        disabled: false,
        value: '',
        files: [],
        dataset: {},
        isConnected: true,
        scrollHeight: 0,
        scrollTop: 0,
        clientHeight: 0,
        listeners: {},
        addEventListener: function (type, callback) {
            this.listeners[type] = callback;
        },
        click: function () {
            (this.calls || (this.calls = [])).push('click');
        }
    };
}

function file(name, size, type) {
    return { name: name, size: size || 10, type: type || 'image/png' };
}

function createHarness(options) {
    options = options || {};
    var els = {};
    ['messagesArea', 'sendBtn', 'attachBtn', 'fileInput'].forEach(function (id) {
        els['#' + id] = makeEl(id);
    });
    var calls = [];
    var timers = [];
    var nextTimer = 1;
    var groupEls = {};
    var state = {
        chatId: options.chatId === undefined ? 'chat-a' : options.chatId,
        chatType: options.chatType || 0,
        myUserId: 1,
        chats: options.chats || [],
        messages: options.messages || [],
        storedEntries: options.storedEntries || [],
        putResult: options.putResult === undefined ? true : options.putResult,
        sendResult:
            options.sendResult ||
            function () {
                return Promise.resolve({ message: { id: 100, senderId: 1, content: { text: 'hi' } } });
            },
        editResult:
            options.editResult ||
            function () {
                return Promise.resolve({ message: { id: 5, editedAt: 1, content: {} } });
            },
        uploadResult:
            options.uploadResult ||
            function (f) {
                return Promise.resolve('file-' + f.name);
            },
        uploadStatusResult:
            options.uploadStatusResult ||
            function (fileId) {
                return Promise.resolve({ state: 'completed', fileId: fileId });
            },
        matches: options.matches === undefined ? true : options.matches,
        uploadType: options.uploadType === undefined ? 2 : options.uploadType,
        buildElementResult: options.buildElementResult,
        groupEls: groupEls
    };
    var BF = {
        i18n: {
            t: function (key) {
                return key;
            }
        },
        utils: {
            formatDate: function (ts) {
                return 'date:' + ts;
            }
        },
        composer: {
            replyToId: options.replyToId || 0,
            text: options.text || '',
            edit: options.edit || null,
            getReplyToId: function () {
                return BF.composer.replyToId;
            },
            getText: function () {
                return BF.composer.text;
            },
            getEdit: function () {
                return BF.composer.edit;
            },
            stopTyping: function (v) {
                calls.push(['stopTyping', v]);
            },
            clearEdit: function () {
                calls.push(['clearEdit']);
            },
            clearForSend: function () {
                calls.push(['clearForSend']);
            },
            restoreFromPending: function (entry) {
                calls.push(['restoreFromPending', entry.localId]);
            },
            openAttach: function (files) {
                calls.push(['openAttach', files.length]);
            }
        },
        feed: {
            findGroup: function (localId) {
                return groupEls[localId] || null;
            },
            buildElement: function (msg) {
                calls.push(['buildElement', msg.id]);
                return state.buildElementResult
                    ? state.buildElementResult(msg)
                    : Promise.resolve(makeEl('built-' + msg.id));
            },
            replaceElement: function (oldEl, newEl) {
                calls.push(['replaceElement', oldEl.id, newEl.id, newEl.dataset.date]);
            },
            removeElement: function (el) {
                calls.push(['removeElement', el.id]);
            },
            append: function (msg) {
                calls.push(['append', msg.id]);
                return Promise.resolve();
            },
            scrollToBottom: function () {
                calls.push(['scrollToBottom']);
            },
            render: function () {
                calls.push(['render']);
                return Promise.resolve();
            }
        },
        files: {
            getUploadFileType: function () {
                return state.uploadType;
            },
            uploadFile: function (f, uploadType, progress, options) {
                calls.push(['uploadFile', f.name]);
                progress(50);
                return state.uploadResult(f, uploadType, options);
            },
            retryUpload: function (f, uploadType, upload, progress, options) {
                calls.push(['retryUpload', f.name]);
                progress(75);
                return state.uploadResult(f, uploadType, options);
            },
            getUploadStatus: function (fileId) {
                calls.push(['getUploadStatus', fileId]);
                return state.uploadStatusResult(fileId);
            },
            matchesPendingUpload: function (upload, f) {
                calls.push(['matchesPendingUpload', f.name]);
                return state.matches;
            }
        },
        messages: {
            updateAttachmentProgress: function (localId, index, percent) {
                calls.push(['progress', index, percent]);
            }
        },
        messageEvents: {
            applyEdit: function (chatId, msg) {
                calls.push(['applyEdit', chatId, msg.id]);
            }
        },
        api: {
            sendMessage: function (payload) {
                calls.push([
                    'api.sendMessage',
                    payload.chatId,
                    payload.text,
                    plain(payload.fileIds),
                    payload.replyToMessageId
                ]);
                return state.sendResult(payload);
            },
            editMessage: function (id, text, keepFileIds) {
                calls.push(['api.editMessage', id, text, plain(keepFileIds)]);
                return state.editResult(id, text, keepFileIds);
            }
        },
        privateChatUI: {
            send: function (text) {
                calls.push(['privateSend', text]);
            }
        },
        sound: {
            play: function (name) {
                calls.push(['sound', name]);
            }
        }
    };
    if (options.drafts !== false) {
        BF.drafts = {
            snapshot: function (chatId) {
                calls.push(['draftSnapshot', chatId]);
                return { generation: 1 };
            },
            clearSent: function (chatId, snap) {
                calls.push(['draftClearSent', chatId, snap.generation]);
            }
        };
    }
    if (options.pendingSends !== false) {
        BF.pendingSends = {
            put: function (snapshot) {
                calls.push(['pendingSends.put', snapshot.chatId, snapshot.state]);
                return state.putResult;
            },
            remove: function (operationId) {
                calls.push(['pendingSends.remove', operationId]);
            },
            all: function () {
                return state.storedEntries;
            }
        };
    }
    function FakeAbortController() {
        this.aborted = false;
        this.signal = {};
        this.abort = function () {
            this.aborted = true;
            calls.push(['abort']);
        };
    }
    var context = vm.createContext({
        BF: BF,
        document: {
            querySelector: function (selector) {
                return els[selector] || null;
            }
        },
        URL: {
            createObjectURL: function (f) {
                calls.push(['createObjectURL', f.name]);
                return 'blob:' + f.name;
            },
            revokeObjectURL: function (url) {
                calls.push(['revokeObjectURL', url]);
            }
        },
        AbortController: FakeAbortController,
        setTimeout: function (callback, ms) {
            var id = nextTimer++;
            timers.push({ id: id, callback: callback, ms: ms });
            return id;
        },
        clearTimeout: function (id) {
            timers = timers.filter(function (t) {
                return t.id !== id;
            });
        },
        Promise: Promise
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/send-queue.js'), 'utf8'), context);
    BF.sendQueue.init({
        getCurrentChatId: function () {
            return state.chatId;
        },
        getCurrentChatType: function () {
            return state.chatType;
        },
        getMyUserId: function () {
            return state.myUserId;
        },
        getChats: function () {
            return state.chats;
        },
        getMessages: function () {
            return state.messages;
        },
        renderChatList: function () {
            calls.push(['renderChatList']);
        },
        showToast: function (text, isError) {
            calls.push(['toast', text, !!isError]);
        }
    });
    return {
        BF: BF,
        els: els,
        state: state,
        groupEls: groupEls,
        calls: function () {
            return calls.slice();
        },
        names: function () {
            return calls.map(function (c) {
                return c[0];
            });
        },
        has: function (name) {
            return calls.some(function (c) {
                return c[0] === name;
            });
        },
        clear: function () {
            calls.length = 0;
        },
        flushTimers: function (ms) {
            var due = timers.filter(function (t) {
                return t.ms === ms;
            });
            timers = timers.filter(function (t) {
                return t.ms !== ms;
            });
            due.forEach(function (t) {
                t.callback();
            });
        }
    };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    // --- sendMessage: guards and the private-chat branch ---

    await test('sendMessage does nothing without an open chat', async function () {
        var h = createHarness({ chatId: null, text: 'hi' });
        h.BF.sendQueue.sendMessage();
        assert.deepEqual(h.calls(), []);
    });

    await test('sendMessage in a group/private chat with empty text does nothing but still stops typing', async function () {
        var h = createHarness({ text: '   ' });
        h.BF.sendQueue.sendMessage();
        assert.deepEqual(h.calls(), [['stopTyping', true]]);
    });

    await test('sendMessage in a private chat forwards the text to BF.privateChatUI.send', async function () {
        var h = createHarness({ chatType: 1, text: 'hey' });
        h.BF.sendQueue.sendMessage();
        assert.deepEqual(h.calls(), [
            ['stopTyping', true],
            ['privateSend', 'hey']
        ]);
    });

    await test('sendMessage in a private chat with empty text does not call BF.privateChatUI.send', async function () {
        var h = createHarness({ chatType: 1, text: '  ' });
        h.BF.sendQueue.sendMessage();
        assert.deepEqual(h.calls(), [['stopTyping', true]]);
    });

    // --- sendMessage: edit branch ---

    await test('editing with no text and no keepable attachments does nothing', async function () {
        var h = createHarness({ edit: { messageId: 5 }, text: '  ', messages: [{ id: 5, content: {} }] });
        h.BF.sendQueue.sendMessage();
        assert.deepEqual(h.calls(), [['stopTyping', true]]);
    });

    await test('editing keeps attachment fileIds but drops forwarded-message markers (both type spellings)', async function () {
        var h = createHarness({
            edit: { messageId: 5 },
            text: '',
            messages: [
                {
                    id: 5,
                    content: {
                        attachments: [
                            { type: 'IMAGE', fileId: 'f1' },
                            { type: 'FORWARDED_MESSAGE', fileId: 'f2' },
                            { type: 8, fileId: 'f3' },
                            { type: '8', fileId: 'f4' },
                            { type: 'DOCUMENT', fileId: null }
                        ]
                    }
                }
            ]
        });
        h.BF.sendQueue.sendMessage();
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'api.editMessage'),
            [['api.editMessage', 5, '', ['f1']]]
        );
    });

    await test('editing succeeds: applies the edit to the chat open when the response arrives, then clears edit state', async function () {
        var h = createHarness({ edit: { messageId: 5 }, text: 'new text', messages: [{ id: 5, content: {} }] });
        h.BF.sendQueue.sendMessage();
        assert.equal(h.els['#sendBtn'].disabled, true, 'the button locks immediately');
        h.state.chatId = 'chat-b'; // the user switched chats while the request was in flight
        await settle();
        assert.equal(h.els['#sendBtn'].disabled, false);
        assert.deepEqual(h.calls(), [
            ['stopTyping', true],
            ['api.editMessage', 5, 'new text', []],
            ['applyEdit', 'chat-b', 5],
            ['clearEdit']
        ]);
    });

    await test('editing failure re-enables the button and does not apply anything', async function () {
        var h = createHarness({
            edit: { messageId: 5 },
            text: 'new text',
            messages: [{ id: 5, content: {} }],
            editResult: function () {
                return Promise.reject(new Error('nope'));
            }
        });
        h.BF.sendQueue.sendMessage();
        await settle();
        assert.equal(h.els['#sendBtn'].disabled, false);
        assert.equal(h.has('applyEdit'), false);
        assert.equal(h.has('clearEdit'), false);
    });

    await test('a response without a message does not apply an edit and does not throw', async function () {
        var h = createHarness({
            edit: { messageId: 5 },
            text: 'x',
            messages: [{ id: 5, content: {} }],
            editResult: function () {
                return Promise.resolve({});
            }
        });
        h.BF.sendQueue.sendMessage();
        await settle();
        assert.equal(h.has('applyEdit'), false);
        assert.equal(h.has('clearEdit'), true);
        assert.equal(h.els['#sendBtn'].disabled, false);
    });

    // --- sendMessage: plain text send ---

    await test('a storage failure shows a toast and sends nothing, without touching the composer', async function () {
        var h = createHarness({ text: 'hello', putResult: false });
        h.BF.sendQueue.sendMessage();
        assert.deepEqual(h.calls(), [
            ['stopTyping', true],
            ['draftSnapshot', 'chat-a'],
            ['pendingSends.put', 'chat-a', 'sending'],
            ['toast', 'error.pendingStorage', false]
        ]);
        assert.deepEqual(h.state.messages, []);
    });

    await test('a plain send shows the pending message immediately, then reconciles and lifts the chat on success', async function () {
        var h = createHarness({
            text: 'hello',
            chats: [
                { id: 'chat-a', title: 'A' },
                { id: 'chat-b', title: 'B' }
            ],
            sendResult: function (payload) {
                return Promise.resolve({
                    message: { id: 200, senderId: 1, clientOperationId: 'unused', content: { text: payload.text } }
                });
            }
        });
        h.BF.sendQueue.sendMessage();
        assert.equal(h.state.messages.length, 1, 'the optimistic message is visible right away');
        var localId = h.state.messages[0].id;
        assert.equal(h.state.messages[0].pendingState, 'sending');
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'append'),
            [['append', localId]]
        );
        assert.equal(h.els['#sendBtn'].disabled, true);
        await settle();
        assert.equal(h.state.messages.length, 1);
        assert.equal(h.state.messages[0].id, 200, 'the server message replaced the local one');
        assert.deepEqual(
            h.state.chats.map((c) => c.id),
            ['chat-a', 'chat-b'],
            'moved to the top, not duplicated'
        );
        assert.equal(h.state.chats[0].lastMessage.id, 200);
        assert.equal(h.has('renderChatList'), true);
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'sound'),
            [['sound', 'tick']]
        );
        assert.equal(h.els['#sendBtn'].disabled, false);
        assert.equal(h.has('clearForSend'), true);
    });

    await test('a send that fails on the network sets a failed pending state and re-enables the button', async function () {
        var h = createHarness({
            text: 'hello',
            sendResult: function () {
                return Promise.reject(new Error('network'));
            }
        });
        h.BF.sendQueue.sendMessage();
        await settle();
        assert.equal(h.state.messages[0].pendingState, 'failed');
        assert.equal(h.els['#sendBtn'].disabled, false);
        assert.equal(h.has('sound'), false);
        assert.equal(h.has('renderChatList'), false);
    });

    await test('a send that fails with an unknown outcome is marked unknown, not failed', async function () {
        var h = createHarness({
            text: 'hello',
            sendResult: function () {
                var err = new Error('timeout');
                err.outcomeUnknown = true;
                return Promise.reject(err);
            }
        });
        h.BF.sendQueue.sendMessage();
        await settle();
        assert.equal(h.state.messages[0].pendingState, 'unknown');
    });

    await test('a response without a message body is treated as a send failure', async function () {
        var h = createHarness({
            text: 'hello',
            sendResult: function () {
                return Promise.resolve({});
            }
        });
        h.BF.sendQueue.sendMessage();
        await settle();
        assert.equal(h.state.messages[0].pendingState, 'failed');
    });

    await test('sending into an unknown chat does not touch the chat list', async function () {
        var h = createHarness({ text: 'hello', chats: [{ id: 'other' }] });
        h.BF.sendQueue.sendMessage();
        await settle();
        assert.equal(h.has('renderChatList'), false);
        assert.equal(h.state.chats.length, 1);
    });

    // --- sendMessageWithFiles: guards ---

    await test('sendMessageWithFiles is a no-op while editing', async function () {
        var h = createHarness({ edit: { messageId: 1 } });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, '');
        assert.deepEqual(h.calls(), []);
    });

    await test('a storage failure for a file send releases the object URLs and shows a toast', async function () {
        var h = createHarness({ putResult: false });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, 'caption');
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'createObjectURL' || c[0] === 'revokeObjectURL' || c[0] === 'toast'),
            [
                ['createObjectURL', 'a.png'],
                ['revokeObjectURL', 'blob:a.png'],
                ['toast', 'error.pendingStorage', false]
            ]
        );
        assert.equal(h.has('uploadFile'), false);
        assert.equal(h.state.messages.length, 0);
    });

    // --- sendMessageWithFiles: happy path and upload wiring ---

    await test('sending files uploads each one in order, reports progress, then dispatches and reconciles', async function () {
        var h = createHarness({
            chats: [{ id: 'chat-a' }],
            sendResult: function (payload) {
                return Promise.resolve({
                    message: {
                        id: 300,
                        senderId: 1,
                        content: { attachments: payload.fileIds.map((id) => ({ fileId: id })) }
                    }
                });
            }
        });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png'), file('b.png')], false, 'caption');
        assert.equal(h.state.messages.length, 1);
        assert.equal(h.state.messages[0].content.attachments.length, 2);
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'uploadFile'),
            [
                ['uploadFile', 'a.png'],
                ['uploadFile', 'b.png']
            ]
        );
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'progress'),
            [
                ['progress', 0, 50],
                ['progress', 1, 50]
            ]
        );
        assert.equal(h.state.messages.length, 1);
        assert.equal(h.state.messages[0].id, 300);
        assert.equal(h.state.chats[0].lastMessage.id, 300);
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'api.sendMessage'),
            [['api.sendMessage', 'chat-a', 'caption', ['file-a.png', 'file-b.png'], 0]]
        );
    });

    await test('a document attachment does not create an object-URL preview even for an image mime type', async function () {
        var h = createHarness();
        h.BF.sendQueue.sendMessageWithFiles([file('report.png')], true, '');
        assert.equal(h.has('createObjectURL'), false);
        assert.equal(h.state.messages[0].content.attachments[0].type, 'DOCUMENT');
    });

    await test('a non-image upload type is sent as DOCUMENT and keeps its preview off', async function () {
        var h = createHarness({ uploadType: 99 });
        h.BF.sendQueue.sendMessageWithFiles([file('a.bin')], false, '');
        assert.equal(h.has('createObjectURL'), false);
        assert.equal(h.state.messages[0].content.attachments[0].type, 'DOCUMENT');
    });

    await test('a file caption is trimmed before it becomes the pending message text', async function () {
        var h = createHarness();
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, '  hi  ');
        assert.equal(h.state.messages[0].content.text, 'hi');
    });

    await test('two uploads that resolve to the same fileId are only recorded once', async function () {
        var sentPayload;
        var h = createHarness({
            uploadResult: function () {
                return Promise.resolve('shared-file-id');
            },
            sendResult: function (payload) {
                sentPayload = payload;
                return Promise.resolve({ message: { id: 1, senderId: 1, content: {} } });
            }
        });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png'), file('b.png')], false, '');
        await settle();
        assert.deepEqual(plain(sentPayload.fileIds), ['shared-file-id']);
    });

    await test('a file reservation mid-upload is persisted right away, before the upload itself finishes', async function () {
        var h = createHarness({
            uploadResult: function (f, uploadType, options) {
                options.onReserved('reserved-' + f.name);
                return new Promise(function () {}); // the upload itself never finishes in this test
            }
        });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, '');
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'pendingSends.put'),
            [
                ['pendingSends.put', 'chat-a', 'uploading'], // the initial storage check before anything runs
                ['pendingSends.put', 'chat-a', 'uploading'], // uploadPendingFiles's own setPendingState('uploading')
                ['pendingSends.put', 'chat-a', 'uploading'] // onReserved, mid-upload
            ]
        );
    });

    await test('an upload rejected with a processing error sets the processing state and still re-enables the button', async function () {
        var h = createHarness({
            uploadResult: function () {
                var err = new Error('upload_processing');
                return Promise.reject(err);
            }
        });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, '');
        await settle();
        assert.equal(h.state.messages[0].pendingState, 'processing');
        assert.equal(h.els['#sendBtn'].disabled, false);
        assert.equal(h.has('api.sendMessage'), false);
    });

    await test('an upload rejected with a generic error sets failed (or unknown) and does not dispatch', async function () {
        var h = createHarness({
            uploadResult: function () {
                return Promise.reject(new Error('boom'));
            }
        });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, '');
        await settle();
        assert.equal(h.state.messages[0].pendingState, 'failed');
        assert.equal(h.has('api.sendMessage'), false);
    });

    // --- DOM wiring set up by init() ---

    await test('the send button click listener triggers sendMessage', async function () {
        var h = createHarness({ text: 'hi' });
        h.els['#sendBtn'].listeners.click();
        assert.equal(h.state.messages.length, 1);
    });

    await test('the attach button click listener resets a pending retry selection and opens the file picker', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'pending'
                        }
                    ]
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1'); // sets the pending file-selection entry
        await settle();
        h.clear();
        h.els['#attachBtn'].listeners.click();
        assert.deepEqual(h.els['#fileInput'].calls, ['click', 'click']); // one from retry, one from the attach button
        // the reset selection means a subsequent file pick is treated as a fresh attachment, not a retry
        h.els['#fileInput'].files = [file('new.png')];
        h.els['#fileInput'].listeners.change();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'openAttach'),
            [['openAttach', 1]]
        );
    });

    await test('picking no file is a no-op for the change listener', async function () {
        var h = createHarness();
        h.els['#fileInput'].files = [];
        h.els['#fileInput'].listeners.change();
        assert.deepEqual(h.calls(), []);
    });

    await test('a plain file pick with no pending retry selection opens the attach dialog', async function () {
        var h = createHarness();
        h.els['#fileInput'].value = 'C:\\fakepath\\a.png';
        h.els['#fileInput'].files = [file('a.png'), file('b.png')];
        h.els['#fileInput'].listeners.change();
        assert.deepEqual(h.calls(), [['openAttach', 2]]);
        assert.equal(
            h.els['#fileInput'].value,
            '',
            'the input is cleared so picking the same file again still fires change'
        );
    });

    await test('re-picking the right files for a waiting retry resumes the upload', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'pending'
                        }
                    ]
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle(); // needsFile: pendingFileSelectionEntry is now set, fileInput.click() was called
        h.groupEls['pending-send-op-1'] = makeEl('el-pending-send-op-1');
        h.clear();
        h.els['#fileInput'].files = [file('a.png')];
        h.els['#fileInput'].listeners.change();
        await settle();
        assert.equal(h.has('openAttach'), false);
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'retryUpload'),
            [['retryUpload', 'a.png']]
        );
        assert.equal(h.has('api.sendMessage'), true);
    });

    await test('re-picking the wrong number of files for a waiting retry shows a toast and stays waiting-file', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'pending'
                        }
                    ]
                }
            ],
            matches: false
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        h.clear();
        h.els['#fileInput'].files = [file('wrong.png')];
        h.els['#fileInput'].listeners.change();
        assert.deepEqual(h.calls(), [
            ['matchesPendingUpload', 'wrong.png'],
            ['toast', 'error.uploadAttachment', false],
            ['pendingSends.put', 'chat-a', 'waiting-file']
        ]);
        assert.equal(h.has('retryUpload'), false);
    });

    await test('re-picking more files than a waiting retry needs is rejected, even if each one would match', async function () {
        // files.every() only walks as many entries as the *picked* files, so an extra file must be caught
        // by an explicit count check, not by matchesPendingUpload alone.
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'pending'
                        }
                    ]
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        h.clear();
        h.els['#fileInput'].files = [file('a.png'), file('extra.png')];
        h.els['#fileInput'].listeners.change();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'toast' || c[0] === 'retryUpload'),
            [['toast', 'error.uploadAttachment', false]]
        );
    });

    await test('a retry selection is dropped even when the retry it belonged to fails and stays unsettled', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'pending'
                        }
                    ]
                }
            ],
            uploadResult: function () {
                return Promise.reject(new Error('boom'));
            }
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        h.els['#fileInput'].files = [file('a.png')];
        h.els['#fileInput'].listeners.change(); // consumes the selection; the retry then fails, entry stays unsettled
        await settle();
        h.clear();
        h.els['#fileInput'].files = [file('unrelated.png')];
        h.els['#fileInput'].listeners.change();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'openAttach' || c[0] === 'retryUpload'),
            [['openAttach', 1]],
            'the consumed selection must not be reused for an unrelated pick'
        );
    });

    await test('re-picking a file for a retry lands in the right slot even when another upload already completed', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: ['done'],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'done.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'completed',
                            resultFileId: 'done'
                        },
                        {
                            operationId: 'u2',
                            name: 'b.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'pending'
                        }
                    ]
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        h.clear();
        h.els['#fileInput'].files = [file('b.png')]; // only the still-incomplete upload is asked for
        h.els['#fileInput'].listeners.change();
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'retryUpload'),
            [['retryUpload', 'b.png']]
        );
        assert.equal(h.has('api.sendMessage'), true, 'the file landed in the right slot, so the retry could complete');
    });

    await test('a retry selection abandoned by settling the entry another way falls back to a plain attach', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'pending'
                        }
                    ]
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        // the entry settles behind the back of the file dialog, e.g. the message got deleted meanwhile
        h.BF.sendQueue.reconcilePendingUpload('chat-a', { id: 1, senderId: 1, clientOperationId: 'op-1', content: {} });
        h.clear();
        h.els['#fileInput'].files = [file('a.png')];
        h.els['#fileInput'].listeners.change();
        assert.deepEqual(h.calls(), [['openAttach', 1]]);
    });

    // --- cancelPendingSend ---

    await test('cancel is a no-op for an unknown id or an entry that is not mid-upload', async function () {
        var h = createHarness();
        h.BF.sendQueue.cancelPendingSend('missing');
        assert.deepEqual(h.calls(), []);

        h.BF.composer.text = 'hello';
        h.BF.sendQueue.sendMessage(); // a text-only send goes straight to "sending", never "uploading"
        var localId = h.state.messages[0].id;
        h.clear();
        h.BF.sendQueue.cancelPendingSend(localId);
        assert.deepEqual(h.calls(), [], 'a "sending" entry is not "uploading", so cancel does nothing');
    });

    await test('cancel aborts the upload, restores the composer, removes the message and unlocks the button', async function () {
        var h = createHarness({
            uploadResult: function () {
                return new Promise(function () {}); // never resolves
            }
        });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, 'caption');
        var localId = h.state.messages[0].id;
        h.groupEls[localId] = makeEl('el-' + localId);
        await settle();
        assert.equal(h.state.messages[0].pendingState, 'uploading');
        h.clear();
        h.BF.sendQueue.cancelPendingSend(localId);
        var removeCall = h.calls().find(function (c) {
            return c[0] === 'pendingSends.remove';
        });
        assert.deepEqual(h.names(), [
            'abort',
            'restoreFromPending',
            'pendingSends.remove',
            'revokeObjectURL',
            'removeElement'
        ]);
        assert.equal(typeof removeCall[1], 'string', 'the random operationId is forwarded, whatever it is');
        assert.deepEqual(
            h.calls().filter((c) => c[0] !== 'pendingSends.remove'),
            [
                ['abort'],
                ['restoreFromPending', localId],
                ['revokeObjectURL', 'blob:a.png'],
                ['removeElement', 'el-' + localId]
            ]
        );
        assert.equal(h.state.messages.length, 0);
        assert.equal(h.els['#sendBtn'].disabled, false);
        // a second cancel is a no-op: the entry is already settled
        h.clear();
        h.BF.sendQueue.cancelPendingSend(localId);
        assert.deepEqual(h.calls(), []);
    });

    await test('an upload that resolves after the user already cancelled must never reach the network', async function () {
        // AbortController.abort() only signals; it does not retroactively fail an in-flight upload promise,
        // so dispatchPendingSend's own settled check is what stops the stale send.
        var release;
        var h = createHarness({
            uploadResult: function (f) {
                return new Promise(function (resolve) {
                    release = function () {
                        resolve('file-' + f.name);
                    };
                });
            }
        });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, '');
        var localId = h.state.messages[0].id;
        h.groupEls[localId] = makeEl('el-' + localId);
        await settle(); // let uploadFile() actually run so `release` is captured
        h.BF.sendQueue.cancelPendingSend(localId);
        assert.equal(h.state.messages.length, 0, 'cancel already removed the placeholder');
        h.clear();
        release(); // the upload finishes anyway, after the user cancelled
        await settle();
        assert.equal(h.has('api.sendMessage'), false, 'a cancelled send must never reach the network');
    });

    await test('restore rebuilds the exact local message shape from stored metadata', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    text: 'raw-text',
                    caption: 'the-caption',
                    createdAt: 12345,
                    state: 'failed',
                    fileIds: ['rf1'],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.gif',
                            size: 5,
                            type: 'image/gif',
                            uploadType: 4,
                            state: 'completed',
                            resultFileId: 'rf1'
                        }
                    ]
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        h.state.messages = [];
        h.BF.sendQueue.mergePendingUploadsIntoMessages('chat-a');
        var msg = h.state.messages[0];
        assert.equal(msg.id, 'pending-send-op-1');
        assert.equal(msg.sentAt, 12345);
        assert.equal(msg.isPending, true);
        assert.equal(msg.pendingState, 'failed', 'every upload is completed, so the terminal state comes from storage');
        assert.equal(msg.content.text, 'the-caption', 'a caption takes priority over the plain text');
        assert.equal(msg.content.attachments.length, 1);
        var att = msg.content.attachments[0];
        assert.equal(att.type, 'GIF');
        assert.equal(att.fileId, 'rf1');
        assert.equal(att.uploadProgress, 100);
    });

    await test('restore copies the stored fileIds array instead of aliasing it', async function () {
        var storedFileIds = [];
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: storedFileIds,
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'processing',
                            reservedFileId: 'rf1'
                        }
                    ]
                }
            ],
            uploadStatusResult: function (fileId) {
                return Promise.resolve({ state: 'completed', fileId: 'server-' + fileId });
            }
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        assert.deepEqual(storedFileIds, [], 'the original fixture array must not have been mutated by the retry');
    });

    // --- retryPendingSend, via restorePendingSends fixtures (deterministic operation ids) ---

    await test('restore rebuilds a pending entry as waiting-file when an upload never completed', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    text: '',
                    caption: 'hi',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'pending'
                        }
                    ]
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        assert.deepEqual(h.state.messages, [], 'restore only rebuilds bookkeeping, it does not touch the feed');
        h.BF.sendQueue.cancelPendingSend('pending-send-op-1'); // pendingState is 'waiting-file', not 'uploading' — no-op
        assert.deepEqual(h.calls(), []);
    });

    await test('retry with no runtime files and an unreserved upload asks for the file again', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'pending'
                        }
                    ]
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        assert.equal(h.has('getUploadStatus'), false, 'nothing was ever reserved, so there is no status to check');
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'pendingSends.put'),
            [
                ['pendingSends.put', 'chat-a', 'waiting-file'],
                ['pendingSends.put', 'chat-a', 'waiting-file']
            ]
        );
        assert.equal(h.els['#fileInput'].calls[0], 'click');
        assert.equal(h.has('api.sendMessage'), false);
    });

    await test('retry finds the reserved upload already completed server-side and dispatches without re-uploading', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'processing',
                            reservedFileId: 'rf1'
                        }
                    ]
                }
            ],
            uploadStatusResult: function (fileId) {
                return Promise.resolve({ state: 'completed', fileId: 'server-' + fileId });
            },
            sendResult: function (payload) {
                return Promise.resolve({
                    message: { id: 400, senderId: 1, content: {}, clientOperationId: payload.clientOperationId }
                });
            }
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'getUploadStatus'),
            [['getUploadStatus', 'rf1']]
        );
        assert.equal(h.has('uploadFile'), false, 'the file is already completed, no re-upload happens');
        assert.equal(h.has('retryUpload'), false);
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'api.sendMessage'),
            [['api.sendMessage', 'chat-a', null, ['server-rf1'], 0]]
        );
    });

    await test('retry finds the reserved upload still processing and waits, without dispatching', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'processing',
                            reservedFileId: 'rf1'
                        }
                    ]
                }
            ],
            uploadStatusResult: function (fileId) {
                return Promise.resolve({ state: 'processing', fileId: fileId });
            }
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'pendingSends.put'),
            [
                ['pendingSends.put', 'chat-a', 'waiting-file'],
                ['pendingSends.put', 'chat-a', 'processing']
            ]
        );
        assert.equal(h.has('api.sendMessage'), false);
        assert.equal(h.has('click'), false);
    });

    await test('retry finds the reserved upload rejected server-side and asks for the file again', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'processing',
                            reservedFileId: 'rf1'
                        }
                    ]
                }
            ],
            uploadStatusResult: function () {
                return Promise.resolve({ state: 'failed' });
            }
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'pendingSends.put'),
            [
                ['pendingSends.put', 'chat-a', 'waiting-file'],
                ['pendingSends.put', 'chat-a', 'waiting-file']
            ]
        );
        assert.equal(h.els['#fileInput'].calls[0], 'click');
    });

    await test('a status-check failure marks the entry failed/unknown and unlocks retrying', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'processing',
                            reservedFileId: 'rf1'
                        }
                    ]
                }
            ],
            uploadStatusResult: function () {
                return Promise.reject(new Error('offline'));
            }
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'pendingSends.put'),
            [['pendingSends.put', 'chat-a', 'failed']]
        );
        // retrying again is now allowed since entry.retrying was reset
        h.clear();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        assert.equal(h.has('getUploadStatus'), true);
    });

    await test('restoring an entry with every upload already completed marks it by its stored terminal state', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: ['rf1'],
                    state: 'failed',
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'completed',
                            resultFileId: 'rf1'
                        }
                    ]
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        // retry: all uploads already completed -> hasIncompleteUpload is false -> the direct branch runs,
        // no status-check chain, straight to dispatch (uploadPendingFiles skips network calls for completed files)
        var sent;
        h.state.sendResult = function (payload) {
            sent = payload;
            return Promise.resolve({ message: { id: 5, senderId: 1, content: {} } });
        };
        h.BF.sendQueue.retryPendingSend('pending-send-op-1');
        await settle();
        assert.equal(h.has('getUploadStatus'), false);
        assert.equal(h.has('retryUpload'), false);
        assert.deepEqual(plain(sent.fileIds), ['rf1']);
    });

    await test('a completed upload keeps its resultFileId, so a retry does not re-upload it', async function () {
        var bAttempts = 0;
        var h = createHarness({
            uploadResult: function (f) {
                if (f.name === 'b.png') {
                    bAttempts++;
                    if (bAttempts === 1) return Promise.reject(new Error('boom'));
                }
                return Promise.resolve('file-' + f.name);
            }
        });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png'), file('b.png')], false, '');
        await settle();
        assert.equal(h.state.messages[0].pendingState, 'failed');
        var localId = h.state.messages[0].id;
        h.clear();
        h.BF.sendQueue.retryPendingSend(localId);
        await settle();
        assert.deepEqual(
            h
                .calls()
                .filter((c) => c[0] === 'uploadFile' || c[0] === 'retryUpload')
                .map((c) => c[1]),
            ['b.png'],
            'a.png already completed with its resultFileId recorded, only b.png needs another attempt'
        );
    });

    await test('retry with runtime files still attached skips the status check and re-uploads directly', async function () {
        // A fresh entry created via sendMessageWithFiles keeps its File objects (runtimeFiles) in memory,
        // so a retry right after a failure goes straight to re-uploading instead of asking for the file.
        var h = createHarness({
            uploadResult: function () {
                return Promise.reject(new Error('boom'));
            }
        });
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, '');
        var localId = h.state.messages[0].id;
        await settle();
        assert.equal(h.state.messages[0].pendingState, 'failed');
        h.state.uploadResult = function (f) {
            return Promise.resolve('file-' + f.name);
        };
        h.clear();
        h.BF.sendQueue.retryPendingSend(localId);
        await settle();
        assert.equal(h.has('getUploadStatus'), false);
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'retryUpload'),
            [['retryUpload', 'a.png']]
        );
        assert.equal(h.has('api.sendMessage'), true);
    });

    await test('retry is a no-op while already retrying or once the entry is settled', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    fileIds: [],
                    uploads: [
                        {
                            operationId: 'u1',
                            name: 'a.png',
                            size: 5,
                            type: 'image/png',
                            uploadType: 2,
                            state: 'pending'
                        }
                    ]
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1'); // starts the async status/needsFile flow, entry.retrying=true
        h.clear();
        h.BF.sendQueue.retryPendingSend('pending-send-op-1'); // still retrying
        assert.deepEqual(h.calls(), []);
        await settle();
        h.BF.sendQueue.cancelPendingSend('pending-send-op-1'); // waiting-file: settle by cancel-equivalent path is unavailable, use direct removal via a second restore
        h.BF.sendQueue.retryPendingSend('missing-id');
        assert.deepEqual(
            h.calls().filter((c) => c[0] !== 'pendingSends.put' && c[0] !== 'click'),
            []
        );
    });

    // --- restorePendingSends without a persistence backend ---

    await test('without BF.pendingSends, restore is a no-op and sending refuses with a storage-failure toast', async function () {
        // persistPendingEntry treats "no backend" the same as "storage full": both make it return a falsy
        // value, and every send checks that return value before showing anything. Pre-existing behavior,
        // not something this refactor changes.
        var h = createHarness({ pendingSends: false, text: 'hello' });
        h.BF.sendQueue.restorePendingSends();
        h.BF.sendQueue.sendMessage();
        assert.deepEqual(h.state.messages, []);
        assert.deepEqual(h.calls(), [
            ['stopTyping', true],
            ['draftSnapshot', 'chat-a'],
            ['toast', 'error.pendingStorage', false]
        ]);
    });

    // --- mergePendingUploadsIntoMessages ---

    await test('merging re-adds a still-unsettled pending message on top of the freshly fetched history', async function () {
        var h = createHarness({
            storedEntries: [
                {
                    operationId: 'op-1',
                    chatId: 'chat-a',
                    text: 'hi',
                    fileIds: [],
                    uploads: []
                }
            ]
        });
        h.BF.sendQueue.restorePendingSends();
        h.state.messages = [{ id: 1 }]; // simulates the freshly loaded page for the reopened chat
        h.BF.sendQueue.mergePendingUploadsIntoMessages('chat-a');
        assert.deepEqual(
            h.state.messages.map((m) => m.id),
            [1, 'pending-send-op-1']
        );
    });

    await test('merging drops a pending entry whose message already arrived, clearing its draft snapshot', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-a', text: '', fileIds: ['rf1'], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        h.state.messages = [{ id: 9, senderId: 1, content: { attachments: [{ fileId: 'rf1' }] } }];
        h.BF.sendQueue.mergePendingUploadsIntoMessages('chat-a');
        assert.deepEqual(
            h.state.messages.map((m) => m.id),
            [9],
            'the pending placeholder is not duplicated'
        );
        assert.equal(h.has('pendingSends.remove'), true);
        assert.equal(h.has('draftClearSent'), true);
    });

    await test('merging ignores entries from another chat or already settled', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-b', text: 'x', fileIds: [], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        h.state.messages = [];
        h.BF.sendQueue.mergePendingUploadsIntoMessages('chat-a');
        assert.deepEqual(h.state.messages, []);
    });

    // --- reconcilePendingUpload: direct DOM/message-array branches ---

    await test('reconciling a message from another chat only releases previews, without touching the open chat', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-b', text: '', fileIds: [], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        h.state.messages = [{ id: 1 }];
        var result = h.BF.sendQueue.reconcilePendingUpload('chat-b', {
            id: 500,
            senderId: 1,
            clientOperationId: 'op-1'
        });
        assert.equal(result, true);
        assert.deepEqual(h.state.messages, [{ id: 1 }]);
    });

    await test('reconciling with no matching pending entry returns false', async function () {
        var h = createHarness();
        var result = h.BF.sendQueue.reconcilePendingUpload('chat-a', { id: 1, senderId: 1 });
        assert.equal(result, false);
    });

    await test('a message from someone else never reconciles a pending upload', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-a', text: '', fileIds: [], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        var result = h.BF.sendQueue.reconcilePendingUpload('chat-a', { id: 1, senderId: 2, clientOperationId: 'op-1' });
        assert.equal(result, false);
    });

    await test('reconciling by matching fileIds replaces the pending DOM element in place, keeping its date', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-a', text: '', fileIds: ['rf1'], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        var localId = 'pending-send-op-1';
        h.state.messages = [{ id: localId }];
        h.groupEls[localId] = makeEl('el-' + localId);
        var msg = { id: 900, senderId: 1, sentAt: 123, content: { attachments: [{ fileId: 'rf1' }] } };
        var result = h.BF.sendQueue.reconcilePendingUpload('chat-a', msg);
        assert.equal(result, true);
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'replaceElement'),
            [['replaceElement', 'el-' + localId, 'built-900', 'date:123']]
        );
        assert.deepEqual(
            h.state.messages.map((m) => m.id),
            [900]
        );
    });

    await test('reconciling releases the object-URL preview after successfully swapping in the real element', async function () {
        var h = createHarness();
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, '');
        var localId = h.state.messages[0].id;
        h.groupEls[localId] = makeEl('el-' + localId);
        var msg = {
            id: 900,
            senderId: 1,
            sentAt: 1,
            clientOperationId: h.state.messages[0].clientOperationId,
            content: {}
        };
        h.clear();
        h.BF.sendQueue.reconcilePendingUpload('chat-a', msg);
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'revokeObjectURL'),
            [['revokeObjectURL', 'blob:a.png']]
        );
    });

    await test('reconciling with a detached DOM element skips the replace but still releases the preview', async function () {
        var h = createHarness();
        h.BF.sendQueue.sendMessageWithFiles([file('a.png')], false, '');
        var localId = h.state.messages[0].id;
        var detached = makeEl('el-' + localId);
        detached.isConnected = false;
        h.groupEls[localId] = detached;
        var msg = {
            id: 901,
            senderId: 1,
            sentAt: 1,
            clientOperationId: h.state.messages[0].clientOperationId,
            content: {}
        };
        h.clear();
        h.BF.sendQueue.reconcilePendingUpload('chat-a', msg);
        await settle();
        assert.equal(h.has('replaceElement'), false);
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'revokeObjectURL'),
            [['revokeObjectURL', 'blob:a.png']]
        );
    });

    await test('reconciling falls back to a full re-render when building the replacement element fails', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-a', text: '', fileIds: ['rf1'], uploads: [] }],
            buildElementResult: function () {
                return Promise.reject(new Error('boom'));
            }
        });
        h.BF.sendQueue.restorePendingSends();
        var localId = 'pending-send-op-1';
        h.state.messages = [{ id: localId }];
        h.groupEls[localId] = makeEl('el-' + localId);
        var msg = { id: 900, senderId: 1, content: { attachments: [{ fileId: 'rf1' }] } };
        h.BF.sendQueue.reconcilePendingUpload('chat-a', msg);
        await settle();
        assert.equal(h.has('render'), true);
        assert.equal(h.has('replaceElement'), false);
    });

    await test('reconciling when the server message is already present drops the local placeholder without appending twice', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-a', text: '', fileIds: ['rf1'], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        var localId = 'pending-send-op-1';
        var msg = { id: 900, senderId: 1, content: { attachments: [{ fileId: 'rf1' }] } };
        h.state.messages = [{ id: localId }, msg];
        h.groupEls[localId] = makeEl('el-' + localId);
        h.BF.sendQueue.reconcilePendingUpload('chat-a', msg);
        assert.deepEqual(
            h.state.messages.map((m) => m.id),
            [900]
        );
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'removeElement'),
            [['removeElement', 'el-' + localId]]
        );
        assert.equal(h.has('append'), false);
        assert.equal(h.has('buildElement'), false);
    });

    await test('reconciling with neither a local placeholder nor a duplicate appends the message to the feed', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-a', text: '', fileIds: ['rf1'], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        h.state.messages = []; // the placeholder is gone, e.g. after a full re-render
        var msg = { id: 900, senderId: 1, content: { attachments: [{ fileId: 'rf1' }] } };
        h.els['#messagesArea'].scrollHeight = 1000;
        h.els['#messagesArea'].clientHeight = 500;
        h.els['#messagesArea'].scrollTop = 500;
        h.BF.sendQueue.reconcilePendingUpload('chat-a', msg);
        await settle();
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'append'),
            [['append', 900]]
        );
        assert.deepEqual(
            h.calls().filter((c) => c[0] === 'scrollToBottom'),
            [['scrollToBottom']]
        );
    });

    await test('matching a pending entry to its server echo by operationId is case-insensitive', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'OP-1', chatId: 'chat-a', text: '', fileIds: [], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        h.state.messages = [{ id: 5, clientOperationId: 'op-1' }];
        h.BF.sendQueue.mergePendingUploadsIntoMessages('chat-a');
        assert.deepEqual(
            h.state.messages.map((m) => m.id),
            [5],
            'recognized and dropped, not duplicated'
        );
    });

    await test('matching a pending entry to its server echo by fileIds is case-insensitive', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-a', text: '', fileIds: ['RF1'], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        // a different clientOperationId isolates the fileId comparison from the operationId one
        h.state.messages = [
            { id: 5, clientOperationId: 'unrelated', senderId: 1, content: { attachments: [{ fileId: 'rf1' }] } }
        ];
        h.BF.sendQueue.mergePendingUploadsIntoMessages('chat-a');
        assert.deepEqual(
            h.state.messages.map((m) => m.id),
            [5]
        );
    });

    await test('a message reporting more fileIds than the pending entry has never counts as a match', async function () {
        // pendingIds.every() only walks as many entries as the pending side has, so a message with EXTRA
        // fileIds must be rejected by an explicit length check, not just the per-index comparison.
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-a', text: '', fileIds: ['rf1'], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        h.state.messages = [
            {
                id: 5,
                clientOperationId: 'unrelated',
                senderId: 1,
                content: { attachments: [{ fileId: 'rf1' }, { fileId: 'rf2' }] }
            }
        ];
        h.BF.sendQueue.mergePendingUploadsIntoMessages('chat-a');
        assert.deepEqual(
            h.state.messages.map((m) => m.id),
            [5, 'pending-send-op-1'],
            'no match found, so the pending placeholder is pushed back in alongside the real message'
        );
    });

    await test('server-reported fileIds are compared case-insensitively too', async function () {
        var h = createHarness({
            storedEntries: [{ operationId: 'op-1', chatId: 'chat-a', text: '', fileIds: ['rf1'], uploads: [] }]
        });
        h.BF.sendQueue.restorePendingSends();
        h.state.messages = [
            { id: 5, clientOperationId: 'unrelated', senderId: 1, content: { attachments: [{ fileId: 'RF1' }] } }
        ];
        h.BF.sendQueue.mergePendingUploadsIntoMessages('chat-a');
        assert.deepEqual(
            h.state.messages.map((m) => m.id),
            [5]
        );
    });

    await test('a hinted entry from dispatchPendingSend is reconciled without a second lookup by fileIds', async function () {
        var h = createHarness({
            sendResult: function (payload) {
                return Promise.resolve({
                    message: { id: 700, senderId: 1, clientOperationId: payload.clientOperationId, content: {} }
                });
            }
        });
        h.BF.sendQueue.sendMessage(); // no text configured yet
        h.BF.composer.text = 'hi';
        h.BF.sendQueue.sendMessage();
        await settle();
        assert.equal(h.state.messages.length, 1);
        assert.equal(h.state.messages[0].id, 700);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
