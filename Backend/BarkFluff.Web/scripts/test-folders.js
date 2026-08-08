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

function loadFolders() {
    var elements = new Map();
    elements.set('folderTabs', new FakeElement('div'));

    var context = {
        console: console,
        Map: Map,
        Set: Set,
        Promise: Promise,
        document: {
            getElementById: function (id) { return elements.get(id) || null; },
            createElement: function (tagName) { return new FakeElement(tagName); }
        },
        window: {
            BF: {
                utils: {},
                api: {
                    getChatFolders: function () {
                        return Promise.resolve({
                            folders: [
                                { folderId: 'work', folderName: 'Работа', folderIcon: '💼', sortOrder: 0, chatList: ['chat-1', 'chat-2'] },
                                { folderId: 'personal', folderName: 'Личное', folderIcon: '🏠', sortOrder: 1, chatList: ['chat-1', 'chat-3'] }
                            ]
                        });
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
        tabs: elements.get('folderTabs')
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
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
