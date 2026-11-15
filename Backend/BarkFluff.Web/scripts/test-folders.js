const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class FakeElement {
    constructor(tagName) {
        this.tagName = tagName;
        this.children = [];
        this.dataset = {};
        this.className = '';
        this.textContent = '';
        this.style = {};
        this.listeners = {};
        this.draggable = false;
    }

    set innerHTML(value) {
        this.children = [];
        this._innerHTML = value;
    }

    appendChild(child) {
        this.children.push(child);
        return child;
    }

    addEventListener(name, callback) {
        this.listeners[name] = callback;
    }

    classList = {
        add: function () {},
        remove: function () {},
        toggle: function () {}
    };
}

var DEFAULT_FOLDERS = [
    { folderId: 'work', folderName: 'Работа', folderIcon: '💼', sortOrder: 0, chatList: ['chat-1', 'chat-2'] },
    { folderId: 'personal', folderName: 'Личное', folderIcon: '🏠', sortOrder: 1, chatList: ['chat-1', 'chat-3'] }
];

function loadFolders(getChatFolders) {
    var elements = new Map();
    elements.set('folderTabs', new FakeElement('div'));
    var realtimeListeners = {};

    var context = {
        console: console,
        Map: Map,
        Set: Set,
        Promise: Promise,
        document: {
            getElementById: function (id) { return elements.get(id) || null; },
            createElement: function (tagName) { return new FakeElement(tagName); },
            createTextNode: function (value) { return { textContent: value, className: '' }; }
        },
        window: {
            BF: {
                utils: {},
                icons: {
                    element: function (name, className) {
                        var icon = new FakeElement('span');
                        icon.className = ('bf-icon ' + (className || '')).trim();
                        icon.dataset.bfIcon = name;
                        return icon;
                    }
                },
                api: {
                    getChatFolders:
                        getChatFolders ||
                        function () {
                            return Promise.resolve({ folders: DEFAULT_FOLDERS });
                        }
                },
                realtime: {
                    on: function (name, callback) {
                        realtimeListeners[name] = callback;
                    }
                },
                i18n: { t: function (key) { return key; } }
            }
        }
    };
    context.BF = context.window.BF;
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/folders.js'), 'utf8'),
        context
    );

    return {
        folders: context.window.BF.folders,
        tabs: elements.get('folderTabs'),
        emit: function (name, data) {
            realtimeListeners[name](data);
        }
    };
}

function findChild(element, className) {
    return element.children.find(function (child) { return child.className === className; });
}

async function main() {
    var loaded = loadFolders();
    await loaded.folders.init();

    loaded.folders.renderTabs([
        { id: 'chat-1', countUnread: 2 },
        { id: 'chat-2', countUnread: 100 },
        { id: 'chat-3', countUnread: 0 }
    ]);

    assert.equal(loaded.tabs.children.length, 3, 'all chats plus two folders should be rendered');
    assert.equal(findChild(loaded.tabs.children[1], 'folder-unread').textContent, '99+', 'folder unread count should be capped at 99+');
    assert.equal(findChild(loaded.tabs.children[2], 'folder-unread').textContent, '2', 'a chat in multiple folders should count in each folder');

    loaded.folders.renderTabs([
        { id: 'chat-1', countUnread: 0 },
        { id: 'chat-2', countUnread: 0 },
        { id: 'chat-3', countUnread: 0 }
    ]);
    assert.equal(findChild(loaded.tabs.children[1], 'folder-unread'), undefined, 'empty folders should not show an unread badge');

    console.log('PASS: folder tabs reflect unread messages in their chats');

    await testRemoteChanges();
}

async function settle() {
    for (var i = 0; i < 5; i++) await new Promise((resolve) => setImmediate(resolve));
}

async function testRemoteChanges() {
    var pending = [];
    var calls = 0;
    var loaded = loadFolders(function () {
        calls++;
        if (calls === 1) return Promise.resolve({ folders: DEFAULT_FOLDERS });
        return new Promise(function (resolve) {
            pending.push(resolve);
        });
    });
    await loaded.folders.init();
    var changes = 0;
    loaded.folders.setOnChange(function () {
        changes++;
    });
    loaded.folders.setActiveFolderId('personal');
    changes = 0;

    loaded.emit('chat_folders_changed', {});
    loaded.emit('resync', { source: 'privateEdits' }); // чужой поток — папки не перечитываем
    loaded.emit('resync', { source: 'chatFolders' });
    assert.equal(calls, 3, 'the signal and the folder stream re-open both re-read folders');

    pending[1]({ folders: [DEFAULT_FOLDERS[0]] }); // свежий ответ: «Личное» удалена на другом устройстве
    await settle();
    pending[0]({ folders: DEFAULT_FOLDERS }); // запоздалый ответ на первый сигнал
    await settle();

    assert.deepEqual(
        Array.from(loaded.folders.getAllFolders(), function (f) {
            return f.folderId;
        }),
        ['work'],
        'a stale response does not restore the deleted folder'
    );
    assert.equal(loaded.folders.getActiveFolderId(), 'all', 'a removed active folder falls back to all chats');
    assert.equal(changes, 1, 'the chat list re-renders once for the applied response');
    console.log('PASS: remote folder changes re-read folders and ignore stale responses');
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
