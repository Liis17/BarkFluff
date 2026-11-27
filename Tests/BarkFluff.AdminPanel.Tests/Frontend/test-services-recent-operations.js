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

const DEPLOY_KIND_LABELS = { Update: 'Обновление', Restart: 'Перезапуск', SwitchBranch: 'Переключение ветки' };
const queueContext = {
    escapeHtml,
    bfIcon: name => `[${name}]`,
    DEPLOY_KIND_LABELS,
    recentJobs: [
        { id: 'a', kind: 'Update', state: 'Running', steps: [{ service: 'settings', state: 'Completed' }, { service: 'messages', state: 'InProgress' }] },
        { id: 'b', kind: 'Restart', state: 'Queued', steps: [{ service: 'users', state: 'Pending' }] },
        { id: 'c', kind: 'Restart', state: 'Completed', steps: [{ service: 'files', state: 'Completed' }] }
    ]
};
const { isActiveJob, queuedActionBadgeHtml, jobServicesLabel } = vm.runInNewContext(`
${['isActiveJob', 'queuedActionBadgeHtml', 'jobServicesLabel'].map(extract).join('\n')}
({ isActiveJob, queuedActionBadgeHtml, jobServicesLabel })`, queueContext);

assert.equal(recentJobResult({ state: 'Cancelled' }).text, 'Отменена');
assert.match(queuedActionBadgeHtml('Messages'), /Обновление<\/span>$/);
assert.match(queuedActionBadgeHtml('users'), /Перезапуск · в очереди/);
assert.equal(queuedActionBadgeHtml('settings'), '');
assert.equal(queuedActionBadgeHtml('files'), '');
assert.equal(isActiveJob({ state: 'Cancelled' }), false);
console.log('PASS: services show their queued and running deploy steps');

assert.equal(jobServicesLabel({ steps: [{ service: 'users' }, { service: 'files' }] }), 'users, files');
assert.equal(jobServicesLabel({ steps: Array.from({ length: 16 }, (_, i) => ({ service: `s${i}` })) }), '16');
console.log('PASS: short jobs list service names, bulk jobs show a count');
