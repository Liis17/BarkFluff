const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadTerminal() {
    const context = { window: {} };
    vm.createContext(context);
    vm.runInContext(
        fs.readFileSync(
            path.join(__dirname, '../../../Backend/Barkfluff.AdminPanel/Pages/v2/assets/terminal.js'),
            'utf8'
        ),
        context
    );
    return context.window.BarkFluffTerminal;
}

function main() {
    const Terminal = loadTerminal();
    const buffer = new Terminal.Buffer({ columns: 120 });

    buffer.write('\x1b[01;32m');
    buffer.write('docker-compose.yml\x1b[0m\x1b]3008;end=9ddeea1d;exit=success\x07\n');
    buffer.write('\x1b[?2004hroot@node# ');

    assert.equal(buffer.toText(), 'docker-compose.yml\nroot@node#');
    assert.equal(buffer.getCell(0, 0).style.foreground, '#0dbc79');

    const split = new Terminal.Buffer({ columns: 120 });
    split.write('\x1b[');
    split.write('01;34mnginx\x1b');
    split.write('[0m\x1b]3008;start=7d6e3069;type=command\x1b\\\n');
    assert.equal(split.toText(), 'nginx\n');

    const escapeIntermediate = new Terminal.Buffer({ columns: 40 });
    escapeIntermediate.write('before\x1b(Bafter');
    assert.equal(escapeIntermediate.toText(), 'beforeafter');

    const controls = new Terminal.Buffer({ columns: 40 });
    controls.write('progress 10%\rprogress 100%\n');
    controls.write('abc\b\bXY\n');
    controls.write('hello\x1b[2D!!\n');
    assert.equal(controls.toText(), 'progress 100%\naXY\nhel!!\n');

    const redraw = new Terminal.Buffer({ columns: 40 });
    redraw.write('stale output');
    redraw.write('\rnew\x1b[K');
    assert.equal(redraw.toText(), 'new');
    redraw.write('\nsecond\x1b[2J\x1b[Hok');
    assert.equal(redraw.toText(), 'ok');

    console.log('PASS: terminal renderer consumes split ANSI/OSC sequences and basic PTY controls');
}

try {
    main();
} catch (error) {
    console.error('FAIL: ' + error.stack);
    process.exitCode = 1;
}
