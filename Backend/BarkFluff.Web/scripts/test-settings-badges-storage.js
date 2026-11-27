const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function settle() {
    for (var i = 0; i < 10; i++) await new Promise((resolve) => setImmediate(resolve));
}

function makeEl(tag) {
    var classes = [];
    var el = {
        tagName: tag || 'div',
        children: [],
        dataset: {},
        style: {},
        attributes: {},
        listeners: {},
        disabled: false,
        _text: '',
        classList: {
            add: function (n) {
                if (classes.indexOf(n) < 0) classes.push(n);
            },
            remove: function () {
                var drop = Array.prototype.slice.call(arguments);
                classes = classes.filter(function (c) {
                    return drop.indexOf(c) < 0;
                });
            },
            toggle: function (n, on) {
                if (on === undefined) on = classes.indexOf(n) < 0;
                if (on) this.add(n);
                else this.remove(n);
            },
            contains: function (n) {
                return classes.indexOf(n) >= 0;
            }
        },
        setAttribute: function (n, v) {
            this.attributes[n] = v;
        },
        addEventListener: function (type, cb) {
            this.listeners[type] = cb;
        },
        appendChild: function (child) {
            this.children.push(child);
            return child;
        }
    };
    Object.defineProperty(el, 'innerHTML', {
        get: function () {
            return el._html || '';
        },
        set: function (v) {
            el._html = v;
            el.children = [];
            el._text = '';
        }
    });
    Object.defineProperty(el, 'textContent', {
        get: function () {
            return el._text + el.children.map(function (c) {
                return c.textContent;
            }).join('');
        },
        set: function (v) {
            el.children = [];
            el._text = String(v);
        }
    });
    return el;
}

function createHarness(api, security) {
    var elements = {};
    var langChanges = [];
    ['#settingsOverlay', '#sdBack', '#sdTitle', '#sdBody', '#confirmOverlay', '#sdClose', '#confirmCancel', '#confirmOk'].forEach(
        function (id) {
            elements[id] = makeEl();
        }
    );
    var badgeCacheWrites = [];
    var BF = {
        api: api,
        authUI: { cancelAll: function () {} },
        confirmations: {
            call: function (method) {
                assert.equal(method, 'getSecuritySettings');
                return security
                    ? Promise.resolve({ toObject: function () { return security; } })
                    : Promise.reject(new Error('unavailable'));
            }
        },
        sound: { play: function () {} },
        node: {
            pinned: function () {
                return true;
            },
            origin: function () {
                return 'https://node.test';
            }
        },
        userBadges: {
            set: function (userId, badges) {
                badgeCacheWrites.push({
                    userId: userId,
                    primaryIds: badges
                        .filter(function (b) {
                            return b.isPrimary;
                        })
                        .map(function (b) {
                            return b.id;
                        })
                });
            },
            createSlot: function () {
                return makeEl('span');
            }
        },
        icons: {
            html: function () {
                return '';
            }
        },
        i18n: {
            t: function (key, vars) {
                return vars ? key + ' ' + JSON.stringify(vars) : key;
            },
            tp: function (key, count) {
                return key + ' ' + count;
            },
            onChange: function () {},
            current: function () {
                return 'en';
            },
            langs: function () {
                return [
                    { code: 'en', name: 'English' },
                    { code: 'ru', name: 'Русский' }
                ];
            },
            setLang: function (code) {
                langChanges.push(code);
            }
        },
        pages: { register: function () {}, show: function () {}, hide: function () {} },
        utils: {
            openOverlay: function () {},
            closeOverlay: function () {},
            escapeHtml: String,
            formatFileSize: function (bytes) {
                return bytes + 'B';
            }
        }
    };
    var context = vm.createContext({
        BF: BF,
        document: {
            querySelector: function (sel) {
                return elements[sel] || null;
            },
            createElement: function (tag) {
                return makeEl(tag);
            }
        },
        addEventListener: function () {},
        Promise: Promise
    });
    context.window = context;
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/settings.js'), 'utf8'), context);
    BF.settings.init({ myUserId: 42 });
    return {
        BF: BF,
        body: elements['#sdBody'],
        backBtn: elements['#sdBack'],
        title: elements['#sdTitle'],
        langChanges: langChanges,
        badgeCacheWrites: badgeCacheWrites
    };
}

function findAll(el, predicate, out) {
    out = out || [];
    if (predicate(el)) out.push(el);
    el.children.forEach(function (c) {
        findAll(c, predicate, out);
    });
    return out;
}

function badgeRows(body) {
    return findAll(body, function (el) {
        return /sd-badge-item/.test(el.className || '');
    });
}

async function test(name, fn) {
    await fn();
    console.log('PASS: ' + name);
}

async function main() {
    await test('badges load for the current user; choosing one makes it primary, choosing it again clears it', async function () {
        var calls = [];
        var badgeRequests = [];
        var h = createHarness({
            getUserBadges: function (userId) {
                badgeRequests.push(userId);
                return Promise.resolve({
                    badges: [
                        { id: 1, name: 'Early', description: 'First users', imageUrl: '', isPrimary: true },
                        { id: 2, name: 'Dev', description: '', imageUrl: 'https://example.test/dev.png', isPrimary: false }
                    ]
                });
            },
            setPrimaryBadge: function (badgeId) {
                calls.push(badgeId);
                return Promise.resolve({ primaryBadge: badgeId ? { id: badgeId } : null });
            }
        });
        h.BF.settings.open('badges');
        await settle();
        assert.deepEqual(badgeRequests, [42]);
        var rows = badgeRows(h.body);
        assert.equal(rows.length, 2);
        assert.match(rows[0].className, /primary/);
        assert.match(rows[0].textContent, /settings\.badges\.primary/);

        rows[1].listeners.click();
        assert.equal(badgeRows(h.body)[0].disabled, true, 'buttons are locked while saving');
        await settle();
        rows = badgeRows(h.body);
        assert.deepEqual(calls, [2]);
        assert.doesNotMatch(rows[0].className, /primary/);
        assert.match(rows[1].className, /primary/);

        rows[1].listeners.click();
        await settle();
        assert.deepEqual(calls, [2, 0], 'clicking the primary badge again clears the choice');
        assert.deepEqual(
            JSON.parse(JSON.stringify(h.badgeCacheWrites)),
            [
                { userId: 42, primaryIds: [1] },
                { userId: 42, primaryIds: [2] },
                { userId: 42, primaryIds: [] }
            ],
            'the shared badge cache follows the loaded list and every successful choice'
        );
        assert.equal(badgeRows(h.body).filter(function (r) {
            return /primary/.test(r.className);
        }).length, 0);
    });

    await test('a failed SetPrimaryBadge keeps the previous choice and shows an error', async function () {
        var h = createHarness({
            getUserBadges: function () {
                return Promise.resolve({ badges: [{ id: 1, name: 'A', isPrimary: false }] });
            },
            setPrimaryBadge: function () {
                return Promise.reject(new Error('not found'));
            }
        });
        h.BF.settings.open('badges');
        await settle();
        badgeRows(h.body)[0].listeners.click();
        await settle();
        assert.doesNotMatch(badgeRows(h.body)[0].className, /primary/);
        assert.match(h.body.textContent, /common\.error/);
        assert.equal(h.badgeCacheWrites.length, 1, 'a failed choice does not touch the shared cache');
    });

    await test('storage shows used of limit and non-empty types sorted by size', async function () {
        var h = createHarness({
            getUserStorageInfo: function () {
                return Promise.resolve({
                    totalUsed: 300,
                    limit: 1000,
                    byType: [
                        { fileType: 2, used: 100 },
                        { fileType: 3, used: 200 },
                        { fileType: 5, used: 0 }
                    ]
                });
            }
        });
        h.BF.settings.open('storage');
        await settle();
        var text = h.body.textContent;
        assert.match(text, /settings\.storage\.used \{"used":"300B","limit":"1000B"\}/);
        var bar = findAll(h.body, function (el) {
            return el.className === 'sd-storage-bar';
        })[0];
        assert.equal(bar.children[0].style.width, '30%');
        var labels = findAll(h.body, function (el) {
            return el.className === 'sd-about-label';
        }).map(function (el) {
            return el.textContent;
        });
        assert.deepEqual(labels, ['attachment.video', 'attachment.photo']);
    });

    await test('navigate slides the screen in from the right, goBack from the left; opening a screen does not slide', async function () {
        var h = createHarness({
            // Без пользователя главный экран не трогает аватар (в моке нет replaceChild): переход — единственное, что проверяем.
            getUser: function () {
                return Promise.resolve({});
            },
            getLoginNotificationSettings: function () {
                return Promise.resolve({});
            },
            getUserStorageInfo: function () {
                return Promise.resolve({ totalUsed: 0, limit: 0, byType: [] });
            },
            getUserBadges: function () {
                return Promise.resolve({ badges: [] });
            },
            getActiveSessions: function () {
                return Promise.resolve({ sessions: [] });
            }
        });
        var forward = function () {
            return h.body.classList.contains('sd-slide-forward');
        };
        var back = function () {
            return h.body.classList.contains('sd-slide-back');
        };
        var openStorageRow = function () {
            findAll(h.body, function (el) {
                return el.className === 'sd-item' && /settings\.storage/.test(el.innerHTML);
            })[0].listeners.click();
        };

        h.BF.settings.open('main');
        await settle();
        assert.equal(forward() || back(), false, 'the first open has no transition');

        openStorageRow();
        assert.equal(h.body.dataset.view, 'storage');
        assert.equal(forward(), true);
        assert.equal(back(), false);

        h.backBtn.listeners.click();
        assert.equal(h.body.dataset.view, 'main');
        assert.equal(back(), true, 'going back replaces the forward direction');
        assert.equal(forward(), false);

        h.body.listeners.animationend({ target: {} });
        assert.equal(back(), true, 'animations of children do not clear the direction');
        h.body.listeners.animationend({ target: h.body });
        assert.equal(back(), false, 'the direction clears when the body animation ends');

        openStorageRow();
        assert.equal(forward(), true);
        h.BF.settings.open('language');
        assert.equal(forward() || back(), false, 'a screen opened from outside does not inherit a leftover slide');
    });

    await test('settings rows are keyboard-operable buttons and mark the current choice', async function () {
        var h = createHarness({});
        h.BF.settings.open('language');
        var rows = findAll(h.body, function (el) {
            return el.className === 'sd-item';
        });
        assert.equal(rows.length, 2);
        rows.forEach(function (row) {
            assert.equal(row.attributes.role, 'button');
            assert.equal(row.tabIndex, 0);
        });
        assert.equal(rows[0].attributes['aria-current'], 'true', 'current language is exposed');
        assert.equal(rows[1].attributes['aria-current'], undefined);

        var prevented = 0;
        var key = function (k) {
            return { key: k, preventDefault: function () { prevented++; } };
        };
        rows[1].listeners.keydown(key('Tab'));
        rows[1].listeners.keydown(key('Enter'));
        rows[1].listeners.keydown(key(' '));
        assert.deepEqual(h.langChanges, ['ru', 'ru'], 'Enter and Space activate, other keys do not');
        assert.equal(prevented, 2, 'Space must not scroll the sheet');
    });
    await test('main screen fills stats and row values; the 2FA warning shows only for password-only sign-in', async function () {
        var api = {
            getUser: function () {
                return Promise.resolve({});
            },
            getUserBadges: function () {
                return Promise.resolve({
                    badges: [
                        { id: 1, name: 'Early', isPrimary: false },
                        { id: 2, name: 'Crown', isPrimary: true }
                    ]
                });
            },
            getActiveSessions: function () {
                return Promise.resolve({ sessions: [{ deviceId: 'a' }, { deviceId: 'b' }, { deviceId: 'c' }] });
            },
            getUserStorageInfo: function () {
                return Promise.resolve({ totalUsed: 250, limit: 1000, byType: [] });
            }
        };
        var byClass = function (root, cls) {
            return findAll(root, function (el) {
                return el.className === cls;
            });
        };
        var valueOf = function (root, label) {
            var row = findAll(root, function (el) {
                return el.className === 'sd-item' && el.innerHTML.indexOf(label) >= 0;
            })[0];
            return byClass(row, 'sd-item-value')[0];
        };

        var h = createHarness(api, { loginMode: 1 });
        h.BF.settings.open('main');
        await settle();
        assert.deepEqual(
            byClass(h.body, 'sd-stat-value').map(function (el) {
                return el.textContent;
            }),
            ['2', '3', '25%']
        );
        assert.equal(byClass(h.body, 'sd-stat-label')[1].textContent, 'settings.stats.sessions 3');
        assert.equal(valueOf(h.body, 'settings.badges').textContent, 'Crown', 'the primary badge is the row value');
        assert.equal(valueOf(h.body, 'settings.language.item').textContent, 'English');
        assert.equal(byClass(h.body, 'sd-alert')[0].hidden, false, 'password-only sign-in shows the warning');
        assert.match(valueOf(h.body, 'settings.twofa').innerHTML, /settings\.twofa\.off/);

        h = createHarness(api, { loginMode: 3 });
        h.BF.settings.open('main');
        await settle();
        assert.equal(byClass(h.body, 'sd-alert')[0].hidden, true);
        assert.match(valueOf(h.body, 'settings.twofa').innerHTML, /settings\.twofa\.on/);

        h = createHarness(Object.assign({}, api, {
            getActiveSessions: function () {
                return Promise.reject(new Error('down'));
            }
        }));
        h.BF.settings.open('main');
        await settle();
        assert.equal(byClass(h.body, 'sd-stat-value')[1].textContent, '—', 'a failed summary shows a dash');
        assert.equal(byClass(h.body, 'sd-alert')[0].hidden, true, 'unknown security settings show no warning');
    });

    await test('profile saves name, username and bio separately; a failed field keeps its input and shows its error', async function () {
        var calls = [];
        var saved = { firstName: 'Li', lastName: '', username: 'li_is', bio: '' };
        var h = createHarness({
            getUser: function () {
                return Promise.resolve({ user: Object.assign({}, saved) });
            },
            changeName: function (fn, ln) {
                calls.push(['name', fn, ln]);
                saved.firstName = fn;
                saved.lastName = ln;
                return Promise.resolve({});
            },
            changeUsername: function (un) {
                calls.push(['username', un]);
                return Promise.reject(new Error('conflict'));
            },
            changeBio: function (bio) {
                calls.push(['bio', bio]);
                saved.bio = bio;
                return Promise.resolve({});
            }
        });
        h.BF.settings.open('account');
        await settle();
        var inputs = findAll(h.body, function (el) {
            return el.className === 'sd-input';
        });
        assert.deepEqual(
            inputs.map(function (el) {
                return el.value;
            }),
            ['Li', '', 'li_is', ''],
            'first name, last name, username and bio are separate fields'
        );
        inputs[1].value = 'Is';
        inputs[2].value = 'taken_name';
        inputs[3].value = 'Hello';
        var save = findAll(h.body, function (el) {
            return el.className === 'sd-btn sd-btn-primary';
        })[0];
        save.listeners.click();
        await settle();
        assert.deepEqual(calls, [['name', 'Li', 'Is'], ['username', 'taken_name'], ['bio', 'Hello']]);
        assert.equal(inputs[1].value, 'Is');
        assert.equal(inputs[2].value, 'taken_name', 'the failed username keeps what was typed');
        assert.equal(inputs[3].value, 'Hello');
        var errors = findAll(h.body, function (el) {
            return el.className === 'sd-hint error' && el.textContent;
        }).map(function (el) {
            return el.textContent;
        });
        assert.deepEqual(errors, ['settings.profile.saveFailed'], 'only the username reports an error');
        assert.equal(save.disabled, false);
    });
}

main().catch(function (error) {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
