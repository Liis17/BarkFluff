const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class FakeElement {
    constructor() {
        this._textContent = '';
    }

    set textContent(value) {
        this._textContent = String(value);
    }

    get textContent() {
        return this._textContent;
    }

    get innerHTML() {
        return this._textContent
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    set innerHTML(value) {
        this._textContent = value
            .replace(/<[^>]*>/g, '')
            .replace(/&quot;/g, '"')
            .replace(/&#39;/g, "'")
            .replace(/&gt;/g, '>')
            .replace(/&lt;/g, '<')
            .replace(/&amp;/g, '&');
    }
}

function loadUtils() {
    const BF = {
        i18n: {
            current: function () { return 'ru'; },
            t: function (key) { return key; },
            tp: function (key) { return key; }
        },
        icons: { html: function () { return ''; } }
    };
    const context = {
        BF: BF,
        document: { createElement: function () { return new FakeElement(); } },
        window: { BF: BF }
    };
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(path.join(__dirname, '../wwwroot/js/app/utils.js'), 'utf8'),
        context
    );
    return context.window.BF.utils;
}

const utils = loadUtils();
const loginNotification = [
    '## Выполнен вход в твой аккаунт',
    '- **Устройство:** Firefox',
    '- **ОС:** Windows',
    '- **Приложение:** BarkFluff Developers v1.0.0',
    '> **Если это не ты**, смени пароль.'
].join('\n');

assert.equal(
    utils.markdownToPlainText(loginNotification),
    'Выполнен вход в твой аккаунт Устройство: Firefox ОС: Windows Приложение: BarkFluff Developers v1.0.0 Если это не ты, смени пароль.'
);
assert.equal(
    utils.markdownToPlainText('**Обновление**: [открыть](https://example.com) и `v1.0`'),
    'Обновление: открыть и v1.0'
);
assert.equal(
    utils.markdownToPlainText('<p>\nТекст\n</p>\n<img src="https://example.com/logo.png" alt="Логотип">'),
    'Текст Логотип'
);
assert.equal(utils.markdownToPlainText('   \n---\n'), '');
assert.equal(
    utils.markdownToPlainText('file_name https://example.com/a_b 2 * 3'),
    'file_name https://example.com/a_b 2 * 3'
);
assert.equal(
    utils.markdownToPlainText('```\n**literal** file_name https://example.com/a_b\n```'),
    '**literal** file_name https://example.com/a_b'
);

console.log('PASS: markdown chat previews contain readable plain text');
