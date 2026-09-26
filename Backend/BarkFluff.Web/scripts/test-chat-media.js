const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class FakeEl {
    constructor(tag) {
        this.tag = tag;
        this.className = '';
        this.dataset = {};
        this.attributes = {};
        this.children = [];
        this.listeners = {};
        this.value = '';
        this.textContent = '';
        this._html = '';
        var self = this;
        this.active = new Set();
        this.classList = {
            add: function (name) {
                self.active.add(name);
            },
            toggle: function (name, force) {
                if (force) self.active.add(name);
                else self.active.delete(name);
            },
            contains: function (name) {
                return self.active.has(name);
            }
        };
    }

    set innerHTML(value) {
        this._html = value;
        this.children = [];
    }

    get innerHTML() {
        return this._html;
    }

    appendChild(child) {
        this.children.push(child);
        return child;
    }

    replaceChildren() {
        this.children = Array.from(arguments);
        this._html = '';
    }

    setAttribute(name, value) {
        this.attributes[name] = String(value);
    }

    addEventListener(type, callback) {
        this.listeners[type] = callback;
    }
}

async function settle() {
    for (var i = 0; i < 20; i++) await new Promise((resolve) => setImmediate(resolve));
}

function createHarness() {
    var timers = [];
    var calls = [];
    var overlays = [];
    var state = { chatId: 7 };
    var BF = {
        api: {
            // Каждый вызов — отложенный ответ, которым тест управляет вручную.
            listChatAttachments: function (chatId, type, offset, limit, query) {
                return new Promise(function (resolve) {
                    calls.push({ chatId: chatId, type: type, query: query, resolve: resolve });
                });
            }
        },
        files: {
            getFileUrls: function (ids) {
                return Promise.resolve(
                    ids.map(function (id) {
                        return { url: 'https://f/' + id, previewUrl: 'https://p/' + id };
                    })
                );
            },
            bindResilientMedia: function () {},
            bindResilientLink: function () {}
        },
        i18n: {
            t: function (key) {
                return key;
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            createElement: function (tag) {
                return new FakeEl(tag);
            },
            createTextNode: function (text) {
                return { text: text };
            },
            querySelectorAll: function () {
                return [];
            }
        },
        Promise: Promise,
        setTimeout: function (callback) {
            timers.push({ callback: callback, active: true });
            return timers.length;
        },
        clearTimeout: function (id) {
            if (timers[id - 1]) timers[id - 1].active = false;
        }
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/chat-media.js'), 'utf8'), context);
    BF.chatMedia.init({
        getCurrentChatId: function () {
            return state.chatId;
        },
        showMediaOverlay: function (kind, url, fileId) {
            overlays.push([kind, url, fileId]);
        }
    });
    var container = new FakeEl('div');
    var panels = BF.chatMedia.createPanels(container);
    return {
        BF: BF,
        state: state,
        panels: panels,
        container: container,
        calls: calls,
        overlays: overlays,
        fireTimers: function () {
            timers.splice(0).forEach(function (timer) {
                if (timer.active) timer.callback();
            });
        }
    };
}

function file(id, name) {
    return { attachment: { fileId: id, fileName: name, type: 'DOCUMENT' } };
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('createPanels builds four panes with a file search box; setTabActive switches the active pane', async function () {
        var h = createHarness();
        assert.deepEqual(Object.keys(h.panels.panes), ['media', 'files', 'audio', 'voice']);
        assert.equal(h.container.children.length, 4);
        assert.equal(h.panels.fileSearch.type, 'search');
        assert.equal(h.panels.panes.media.classList.contains('active'), true);
        h.BF.chatMedia.setTabActive('.tab', h.panels, 'audio');
        assert.equal(h.panels.panes.audio.classList.contains('active'), true);
        assert.equal(h.panels.panes.media.classList.contains('active'), false);
    });

    await test('the files tab lists attachments; empty results depend on whether a query was typed', async function () {
        var h = createHarness();
        h.BF.chatMedia.render('files', h.panels);
        assert.equal(h.calls[0].type, 4);
        h.calls[0].resolve({ attachments: [file('f1', 'a.pdf'), file('f2', 'b.pdf')] });
        await settle();
        var list = h.panels.contents.files.children[0];
        assert.equal(list.children.length, 2);
        assert.equal(list.children[0].href, 'https://f/f1');

        h.panels.fileSearch.value = 'zzz';
        h.BF.chatMedia.render('files', h.panels);
        assert.equal(h.calls[1].query, 'zzz');
        h.calls[1].resolve({ attachments: [] });
        await settle();
        assert.match(h.panels.contents.files.innerHTML, /media\.notFound\.files/);

        h.panels.fileSearch.value = '';
        h.BF.chatMedia.render('files', h.panels);
        h.calls[2].resolve({ attachments: [] });
        await settle();
        assert.match(h.panels.contents.files.innerHTML, /media\.empty\.files/);
    });

    await test('a response for a superseded request in the same tab is dropped', async function () {
        var h = createHarness();
        h.BF.chatMedia.render('files', h.panels);
        h.BF.chatMedia.render('files', h.panels);
        h.calls[1].resolve({ attachments: [file('new', 'new.pdf')] });
        await settle();
        h.calls[0].resolve({ attachments: [file('old1', 'old1.pdf'), file('old2', 'old2.pdf')] });
        await settle();
        assert.equal(h.panels.contents.files.children.length, 1, 'the stale response did not add a second list');
        var list = h.panels.contents.files.children[0];
        assert.equal(list.children.length, 1);
        assert.equal(list.children[0].href, 'https://f/new');
    });

    await test('switching chats resets the panes and drops responses that belong to the previous chat', async function () {
        var h = createHarness();
        h.BF.chatMedia.render('files', h.panels); // чат 7
        h.state.chatId = 8;
        h.panels.fileSearch.value = 'leftover';
        h.BF.chatMedia.render('files', h.panels); // чат 8
        assert.equal(h.panels.fileSearch.value, '', 'the search box is cleared for the new chat');
        assert.equal(h.calls[1].chatId, 8);
        h.calls[1].resolve({ attachments: [file('c8', 'chat8.pdf')] });
        await settle();
        h.calls[0].resolve({ attachments: [file('c7', 'chat7.pdf')] }); // запоздалый ответ прошлого чата
        await settle();
        assert.equal(h.panels.contents.files.children.length, 1, 'the previous chat did not add a second list');
        var list = h.panels.contents.files.children[0];
        assert.equal(list.children.length, 1);
        assert.equal(list.children[0].href, 'https://f/c8');
    });

    await test('a response arriving after the open chat changed is dropped even before the panel is re-rendered', async function () {
        var h = createHarness();
        h.BF.chatMedia.render('files', h.panels); // чат 7
        h.state.chatId = 8; // пользователь переключился, панель ещё не перерисована
        h.calls[0].resolve({ attachments: [file('c7', 'chat7.pdf')] });
        await settle();
        assert.equal(h.panels.contents.files.children.length, 0, 'nothing from the previous chat is shown');
    });

    await test('typing in the file search re-renders the tab after the debounce', async function () {
        var h = createHarness();
        h.BF.chatMedia.render('files', h.panels);
        h.panels.fileSearch.value = 'rep';
        h.panels.fileSearch.listeners.input();
        h.panels.fileSearch.value = 'report';
        h.panels.fileSearch.listeners.input();
        assert.equal(h.calls.length, 1, 'nothing is requested until the debounce fires');
        h.fireTimers();
        assert.equal(h.calls.length, 2);
        assert.equal(h.calls[1].query, 'report', 'only the last input triggers a request');
    });

    await test('the media tab merges photos, videos and GIFs newest first; a tile opens the viewer', async function () {
        var h = createHarness();
        h.BF.chatMedia.render('media', h.panels);
        assert.deepEqual(
            h.calls.map(function (c) {
                return c.type;
            }),
            [1, 2, 3]
        );
        var img = function (id, sentAt, type) {
            return { sentAt: sentAt, attachment: { fileId: id, type: type || 'IMAGE' } };
        };
        h.calls[0].resolve({ attachments: [img('old', 100)] });
        h.calls[1].resolve({ attachments: [img('vid', 300, 'VIDEO')] });
        h.calls[2].resolve({ attachments: [img('gif', 200, 'GIF')] });
        await settle();
        var grid = h.panels.contents.media.children[0];
        assert.equal(grid.children.length, 3);
        grid.children[0].listeners.click(); // самое новое — видео
        grid.children[1].listeners.click(); // GIF: в просмотрщик уходит неподвижное превью, без fileId
        grid.children[2].listeners.click();
        assert.deepEqual(JSON.parse(JSON.stringify(h.overlays)), [
            ['video', 'https://f/vid', 'vid'],
            ['image', 'https://p/gif', null],
            ['image', 'https://f/old', 'old']
        ]);
    });

    await test('nothing is requested without an open chat', async function () {
        var h = createHarness();
        h.state.chatId = null;
        h.BF.chatMedia.render('media', h.panels);
        assert.equal(h.calls.length, 0);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
