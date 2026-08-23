const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class FakeLayer {
    constructor() {
        this.attributes = new Map();
        this.style = { backgroundImage: '' };
        this.classList = {
            values: new Set(),
            add: (...values) => values.forEach((value) => this.classList.values.add(value)),
            remove: (...values) => values.forEach((value) => this.classList.values.delete(value))
        };
    }

    setAttribute(name, value) {
        this.attributes.set(name, String(value));
    }

    getAttribute(name) {
        return this.attributes.has(name) ? this.attributes.get(name) : null;
    }
}

function createHarness() {
    const expiredUrl = 'https://files.test/expired';
    const freshUrl = 'https://files.test/fresh';
    const layer = new FakeLayer();
    const css = new Map();
    let tempUrlRequests = 0;
    let expiredAttempts = 0;

    class FakeImage {
        set src(url) {
            setImmediate(() => {
                if (url === expiredUrl) {
                    expiredAttempts++;
                    if (this.onerror) this.onerror();
                } else if (url === freshUrl && this.onload) {
                    this.onload();
                }
            });
        }
    }

    const BF = {
        node: {
            proxied: () => false,
            meta: () => null
        },
        api: {
            getTempDownloadUrl: () => {
                tempUrlRequests++;
                return Promise.resolve({
                    files: [{ fileId: 'background-file', url: tempUrlRequests === 1 ? expiredUrl : freshUrl }]
                });
            },
            getUserSettings: () => Promise.resolve({
                settings: {
                    globalChatBackgroundFileId: 'background-file',
                    chatBackgrounds: []
                }
            })
        }
    };
    const document = {
        documentElement: { style: { setProperty: (name, value) => css.set(name, value) } },
        getElementById: (id) => id === 'messagesBgLayer' ? layer : null,
        addEventListener: () => {}
    };
    const localStorage = {
        getItem: () => null,
        setItem: () => {},
        removeItem: () => {}
    };
    const context = {
        BF,
        Image: FakeImage,
        Promise,
        Map,
        Set,
        Date,
        Number,
        String,
        isFinite,
        setImmediate,
        setInterval: () => 0,
        clearInterval: () => {},
        document,
        localStorage,
        console
    };
    context.window = context;

    const filesSource = fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/files.js'), 'utf8');
    const personalizationSource = fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/personalization.js'), 'utf8');
    vm.runInNewContext(filesSource, context);
    vm.runInNewContext(personalizationSource, context);

    return { BF, css, layer, expiredAttempts, get expiredAttemptsCount() { return expiredAttempts; }, get tempUrlRequests() { return tempUrlRequests; } };
}

async function settle() {
    for (let i = 0; i < 8; i++) await new Promise((resolve) => setImmediate(resolve));
}

async function main() {
    const harness = createHarness();
    await harness.BF.personalization.init();
    await settle();

    assert.equal(harness.expiredAttemptsCount, 1, 'the expired background URL should produce an image load error');
    assert.equal(harness.tempUrlRequests, 2, 'a failed background URL should trigger one fresh URL request');
    assert.equal(harness.layer.style.backgroundImage, 'url("https://files.test/fresh")', 'the background layer should use the refreshed URL');
    assert.equal(harness.css.get('--chat-bg-image'), 'url("https://files.test/fresh")', 'the resolved URL should stay fresh for settings previews');
    console.log('PASS: expired chat background URL is refreshed after a 404');
}

main().catch((error) => {
    console.error('FAIL: ' + error.message);
    process.exitCode = 1;
});
