const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const pagesDir = path.join(root, 'Backend/Barkfluff.AdminPanel/Pages/v2');

// Функции выполняются в отдельном контексте vm: копии объектов приводим к обычным для сравнения.
const plain = value => JSON.parse(JSON.stringify(value));

function readPage(name) {
    return fs.readFileSync(path.join(pagesDir, name), 'utf8');
}

function extractFunction(html, name) {
    const match = html.match(new RegExp(`    (?:async )?function ${name}\\([\\s\\S]*?\\n    \\}`));
    assert.ok(match, `function ${name} must exist`);
    return match[0];
}

function extractConst(html, name) {
    const match = html.match(new RegExp(`    const ${name} = .*;`));
    assert.ok(match, `const ${name} must exist`);
    return match[0];
}

// ---- Логи: ссылка на фильтры зеркальна восстановлению из URL ----
const logsHtml = readPage('logs.html');
const logs = vm.runInNewContext([
    extractConst(logsHtml, 'LOG_CONTEXT_KINDS'),
    extractConst(logsHtml, 'ISO_DATE_TIME_PATTERN'),
    extractFunction(logsHtml, 'parseFromUtc'),
    extractFunction(logsHtml, 'buildFilterQuery'),
    extractFunction(logsHtml, 'parseFilterQuery'),
    '({ buildFilterQuery, parseFilterQuery })'
].join('\n'), { URLSearchParams });

const eventsState = {
    service: 'BarkFluff.Messages', level: 'Error', search: 'таймаут & retry',
    context: { kind: 'correlationId', value: 'abc-123' }, viewMode: 'events'
};
const eventsQuery = logs.buildFilterQuery(eventsState).toString();
const eventsParams = new URLSearchParams(eventsQuery);
assert.equal(eventsParams.get('service'), 'BarkFluff.Messages');
assert.equal(eventsParams.get('level'), 'Error');
assert.equal(eventsParams.get('search'), 'таймаут & retry');
assert.equal(eventsParams.get('correlationId'), 'abc-123');
assert.equal(eventsParams.has('view'), false);
console.log('PASS: events link carries service, level, search and context with encoded values');

assert.equal(logs.buildFilterQuery({ service: '', level: '', search: '', context: null, viewMode: 'events' }).toString(), '');
console.log('PASS: empty filters produce no query parameters');

const groupsQuery = logs.buildFilterQuery({ service: 'BarkFluff.Users', level: 'Error', search: '', context: null, viewMode: 'groups' }).toString();
assert.equal(groupsQuery, 'service=BarkFluff.Users&view=groups');
console.log('PASS: groups link omits level because groups mode forces Error on restore');

const logStates = [
    eventsState,
    { service: 'BarkFluff.Users', level: 'Error', search: '', context: null, viewMode: 'groups' },
    { service: '', level: 'Warning', search: '', context: { kind: 'userId', value: '42' }, viewMode: 'events' },
    { service: '', level: '', search: 'a/b+c', context: { kind: 'requestId', value: 'req-1' }, viewMode: 'events' },
    { service: 'BarkFluff.Files', level: 'Error', search: '', context: null, viewMode: 'events', fromUtc: '2026-11-23T13:03:21.000Z' }
];
for (const state of logStates) {
    const restored = logs.parseFilterQuery(new URLSearchParams(logs.buildFilterQuery(state).toString()));
    assert.deepEqual(plain(restored), {
        service: state.service,
        level: state.viewMode === 'groups' ? 'Error' : state.level,
        search: state.search,
        context: state.context,
        viewMode: state.viewMode,
        fromUtc: state.fromUtc || ''
    });
}
console.log('PASS: link query restores the same service, level, search, context, mode and period');

// Копирование: без navigator.clipboard (http) и при отказе writeText работает fallback через execCommand.
function runCopyText(navigatorStub, execResult) {
    const area = { value: '', style: {}, setAttribute() {}, select() { area.selected = true; }, remove() { area.removed = true; } };
    const commands = [];
    const sandbox = {
        navigator: navigatorStub,
        document: {
            body: { appendChild() {} },
            createElement() { return area; },
            execCommand(command) { commands.push(command); return execResult; }
        }
    };
    const copyText = vm.runInNewContext(`${extractFunction(logsHtml, 'copyText')}; copyText`, sandbox);
    return { copyText, area, commands };
}

const link = 'https://admin.example.test/logs?service=BarkFluff.Messages&level=Error';

async function runCopyTests() {
    {
        const { copyText, area, commands } = runCopyText({}, true);
        assert.equal(await copyText(link), true);
        assert.equal(area.value, link);
        assert.deepEqual(commands, ['copy']);
        assert.equal(area.removed, true);
    }
    {
        const { copyText, area } = runCopyText({ clipboard: { writeText: async () => { throw new Error('denied'); } } }, true);
        assert.equal(await copyText(link), true);
        assert.equal(area.value, link);
    }
    {
        const { copyText } = runCopyText({}, false);
        assert.equal(await copyText(link), false);
    }
    console.log('PASS: copy falls back to a textarea when the clipboard API is unavailable or rejected');
}

// ---- Пользователи: URL выборки и валидация параметров ----
const usersHtml = readPage('users.html');
const users = vm.runInNewContext([
    extractConst(usersHtml, 'USER_STATUSES'),
    extractConst(usersHtml, 'MAX_RESTORED_PAGES'),
    extractFunction(usersHtml, 'buildUsersUrlQuery'),
    extractFunction(usersHtml, 'readUsersUrlState'),
    '({ buildUsersUrlQuery, readUsersUrlState })'
].join('\n'), { URLSearchParams });

assert.deepEqual(plain(users.readUsersUrlState('')), { query: '', status: 'all', page: 1, userId: null });
assert.deepEqual(plain(users.readUsersUrlState('?q=ivan&status=blocked&page=3')), { query: 'ivan', status: 'blocked', page: 3, userId: null });
assert.deepEqual(plain(users.readUsersUrlState('?status=deleted&page=0&userId=12a')), { query: '', status: 'all', page: 1, userId: null });
assert.equal(users.readUsersUrlState('?page=-2').page, 1);
assert.equal(users.readUsersUrlState('?page=abc').page, 1);
assert.equal(users.readUsersUrlState('?page=1e3').page, 1);
assert.equal(users.readUsersUrlState('?page=1000').page, 20);
console.log('PASS: invalid status, page and userId are ignored and page is capped');

assert.deepEqual(plain(users.readUsersUrlState('?userId=42')), { query: '42', status: 'all', page: 1, userId: '42' });
assert.deepEqual(plain(users.readUsersUrlState('?q=ivan&userId=42')), { query: 'ivan', status: 'all', page: 1, userId: '42' });
console.log('PASS: legacy /users?userId=N searches by id and opens the card; q takes precedence');

assert.equal(users.buildUsersUrlQuery({ query: '', status: 'all', pages: 1, userId: null }), '');
assert.equal(users.buildUsersUrlQuery({ query: 'a', status: 'all', pages: 1, userId: null }), 'q=a');
const usersState = { query: 'ivan петров', status: 'pending_deletion', pages: 3, userId: '42' };
const usersSearch = users.buildUsersUrlQuery(usersState);
assert.deepEqual(plain(users.readUsersUrlState(`?${usersSearch}`)), { query: 'ivan петров', status: 'pending_deletion', page: 3, userId: '42' });
console.log('PASS: users URL omits defaults and restores query, status, page and open card');

runCopyTests().catch(error => {
    console.error(error);
    process.exit(1);
});
