const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const html = fs.readFileSync(path.join(root, 'Backend/Barkfluff.AdminPanel/Pages/v2/services.html'), 'utf8');
const fn = html.match(/    function updatableContainers\(\) \{[\s\S]*?\n    \}/)[0];
const servicesData = [
    { name: 'BarkFluff.Bots', container: 'my-project-bots-1', updateAvailable: true },
    { name: 'BarkFluff.CloudMessaging', container: 'cloud-messaging', updateAvailable: true },
    { name: 'BarkFluff.Files', container: 'files', updateAvailable: false },
    { name: 'Redis', container: 'redis', updateAvailable: true }
];
const result = vm.runInNewContext(`${fn}; updatableContainers()`, { servicesData });
assert.deepEqual(Array.from(result), ['my-project-bots-1', 'cloud-messaging']);
console.log('PASS: available updates use actual container names and exclude infrastructure');
