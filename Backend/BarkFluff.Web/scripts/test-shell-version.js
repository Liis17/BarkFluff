const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bf-shell-version-'));
const web = path.join(root, 'Backend', 'BarkFluff.Web');
const script = path.join(web, 'scripts', 'build-shell-version.js');
const worker = path.join(web, 'wwwroot', 'service-worker.js');

function write(file, content) {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, content);
}

function buildCacheName() {
    execFileSync(process.execPath, [script], { stdio: 'pipe' });
    return fs.readFileSync(worker, 'utf8').match(/CACHE_NAME = '([^']+)'/)[1];
}

try {
    write(script, fs.readFileSync(path.join(__dirname, 'build-shell-version.js')));
    write(worker, "const CACHE_NAME = 'barkfluff-shell-00000000';\nconst APP_SHELL = ['/app.css'];\n");
    write(path.join(web, 'wwwroot', 'app.css'), 'body {}');

    const icon = path.join(root, 'Icons', 'work.svg');
    write(icon, '<svg>old</svg>');
    const initial = buildCacheName();
    assert.equal(buildCacheName(), initial, 'an unchanged build must keep the cache');

    write(icon, '<svg>new</svg>');
    const edited = buildCacheName();
    assert.notEqual(edited, initial, 'a redrawn icon must invalidate the cache');

    const added = path.join(root, 'Icons', 'nested', 'added.svg');
    write(added, '<svg>added</svg>');
    assert.notEqual(buildCacheName(), edited, 'an added icon must invalidate the cache');

    fs.rmSync(added);
    assert.equal(buildCacheName(), edited, 'the same icon set must give the same cache');

    write(path.join(root, 'Icons', 'notes.txt'), 'not served');
    assert.equal(buildCacheName(), edited, 'files outside the served pack must be ignored');
} finally {
    fs.rmSync(root, { recursive: true, force: true });
}

console.log('shell version tests passed');
