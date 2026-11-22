const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const html = fs.readFileSync(path.join(root, 'Backend/Barkfluff.AdminPanel/Pages/v2/services.html'), 'utf8');
const extract = name => html.match(new RegExp(`    function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n    \\}`))[0];
const escapeHtml = text => String(text);
const { recentJobResult, rollbackLabel, stepDetailHtml } = vm.runInNewContext(`
${['recentJobResult', 'rollbackLabel', 'stepDetailHtml'].map(extract).join('\n')}
({ recentJobResult, rollbackLabel, stepDetailHtml })`, { escapeHtml });

assert.equal(recentJobResult({ state: 'Queued' }).text, 'В очереди');
assert.equal(recentJobResult({ state: 'Running' }).text, 'Выполняется');
assert.equal(recentJobResult({ state: 'Completed' }).text, 'Успешно');
assert.equal(recentJobResult({ state: 'Completed' }).cls, 'md-status-success');
assert.equal(recentJobResult({ state: 'Failed' }).text, 'С ошибками');
assert.equal(recentJobResult({ state: 'Failed' }).cls, 'md-status-error');
console.log('PASS: recent operation result is derived from the job state');

assert.equal(rollbackLabel({ rolledBack: true }), 'откат выполнен');
assert.equal(rollbackLabel({ rolledBack: false }), 'без отката');
console.log('PASS: rollback state of a failed step is labelled');

assert.equal(stepDetailHtml({ state: 'Completed', message: 'Перезапущен' }), '');
const detail = stepDetailHtml({ state: 'Failed', message: 'Контейнер не запустился', rolledBack: true });
assert.match(detail, /Контейнер не запустился · откат выполнен/);
assert.equal(stepDetailHtml({ state: 'Failed', message: null, rolledBack: false }).includes('Причина не записана · без отката'), true);
console.log('PASS: failed step shows its reason and rollback state only in result view');
