/* Computes a content hash over the service-worker APP_SHELL files and the shared
   icon pack (served cache-first from /icons) and writes it into CACHE_NAME so
   their changes invalidate the SW cache automatically.
   The browser checks the main service-worker script byte-for-byte on navigation,
   so the version must live in service-worker.js itself (not an importScripts). */
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');

const projectRoot = path.resolve(__dirname, '..');
const wwwroot = path.join(projectRoot, 'wwwroot');
const serviceWorkerPath = path.join(wwwroot, 'service-worker.js');
const iconPack = path.resolve(projectRoot, '..', '..', 'Icons');

const ROUTE_TO_FILE = {
    '/': 'index.html',
    '/messenger': 'messenger.html'
};

function parseAppShell(source) {
    const match = source.match(/const\s+APP_SHELL\s*=\s*\[([\s\S]*?)\]/);
    if (!match) throw new Error('Could not find APP_SHELL in service-worker.js');
    return match[1]
        .split(',')
        .map(function (item) {
            return item.trim().match(/^'([^']*)'/);
        })
        .filter(Boolean)
        .map(function (m) {
            return m[1];
        });
}

function resolveShellFile(urlPath) {
    const relative = ROUTE_TO_FILE[urlPath] || urlPath;
    return path.join(wwwroot, relative.split('?')[0]);
}

async function listIconFiles() {
    const files = await fs.readdir(iconPack, { recursive: true });
    return files
        .filter(function (file) {
            return file.endsWith('.svg');
        })
        .map(function (file) {
            return file.split(path.sep).join('/');
        })
        .sort();
}

async function computeShellVersion(entries, icons) {
    const hash = crypto.createHash('sha1');
    for (const entry of entries) {
        const filePath = resolveShellFile(entry);
        const content = await fs.readFile(filePath);
        hash.update(entry).update('\0').update(content).update('\0');
    }
    for (const icon of icons) {
        const content = await fs.readFile(path.join(iconPack, icon));
        hash.update('/icons/' + icon)
            .update('\0')
            .update(content)
            .update('\0');
    }
    return hash.digest('hex').slice(0, 8);
}

async function run() {
    const original = await fs.readFile(serviceWorkerPath, 'utf8');
    const entries = parseAppShell(original);
    const version = await computeShellVersion(entries, await listIconFiles());
    const next = original.replace(
        /const CACHE_NAME = 'barkfluff-shell-[^']+'/,
        "const CACHE_NAME = 'barkfluff-shell-" + version + "'"
    );
    if (next === original) {
        process.stdout.write('Shell version already up to date: ' + version + '\n');
        return;
    }
    await fs.writeFile(serviceWorkerPath, next, 'utf8');
    process.stdout.write('Wrote shell version: barkfluff-shell-' + version + '\n');
}

run().catch(function (error) {
    console.error(error);
    process.exitCode = 1;
});
