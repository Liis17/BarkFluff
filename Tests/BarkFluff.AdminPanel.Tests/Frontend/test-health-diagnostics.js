const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const pageDir = path.join(root, 'Backend/Barkfluff.AdminPanel/Pages/v2');
const dashboard = fs.readFileSync(path.join(pageDir, 'dashboard.html'), 'utf8');
const logs = fs.readFileSync(path.join(pageDir, 'logs.html'), 'utf8');

function sliceBetween(source, startMarker, endMarker) {
    const start = source.indexOf(startMarker);
    assert.notEqual(start, -1, `marker not found: ${startMarker}`);
    const end = source.indexOf(endMarker, start);
    assert.notEqual(end, -1, `marker not found: ${endMarker}`);
    return source.slice(start, end);
}

const healthHelpers = sliceBetween(dashboard, '    // Порядок тяжести', '    function renderServicesHealth(');
const { sortServicesBySeverity, serviceDiagnosticsLink } = vm.runInNewContext(
    `${healthHelpers}; ({ sortServicesBySeverity, serviceDiagnosticsLink })`,
    { URLSearchParams });

const CHECKED_AT = '2026-11-23T14:03:21.000Z';

{
    const services = [
        { name: 'BarkFluff.Alpha', status: 'healthy' },
        { name: 'BarkFluff.Beta', status: 'unknown' },
        { name: 'BarkFluff.Gamma', status: 'degraded' },
        { name: 'BarkFluff.Delta', status: 'down' },
        { name: 'BarkFluff.Epsilon', status: 'healthy' },
        { name: 'BarkFluff.Zeta', status: 'down' },
        { name: 'BarkFluff.Eta', status: 'unknown' }
    ];
    const order = Array.from(sortServicesBySeverity(services), svc => svc.name.replace('BarkFluff.', ''));
    assert.deepEqual(order, ['Delta', 'Zeta', 'Gamma', 'Beta', 'Eta', 'Alpha', 'Epsilon']);
    console.log('PASS: problem services sort first by severity and keep snapshot order inside a group');
}

{
    const logsLink = serviceDiagnosticsLink(
        { name: 'BarkFluff.Files', status: 'down' },
        { generatedAtUtc: CHECKED_AT, seqEnabled: true });
    const url = new URL(logsLink.href, 'http://admin.test');
    assert.equal(logsLink.label, 'Открыть логи');
    assert.equal(url.pathname, '/logs');
    assert.equal(url.searchParams.get('service'), 'BarkFluff.Files');
    assert.equal(url.searchParams.get('level'), 'Error');
    assert.equal(url.searchParams.get('from'), '2026-11-23T13:03:21.000Z');
    console.log('PASS: logs link covers the hour before the check time');
}

{
    const servicesLink = serviceDiagnosticsLink(
        { name: 'BarkFluff.Files', status: 'unknown' },
        { generatedAtUtc: CHECKED_AT, seqEnabled: false });
    const url = new URL(servicesLink.href, 'http://admin.test');
    assert.equal(servicesLink.label, 'Открыть в «Сервисах»');
    assert.equal(url.pathname, '/services');
    assert.equal(url.searchParams.get('service'), 'BarkFluff.Files');
    console.log('PASS: with Seq disabled the link opens the service row in Services');
}

const logsPeriod = sliceBetween(logs, '    // Период из URL', '    function renderPeriodFilter()');
const { parseFromUtc } = vm.runInNewContext(`${logsPeriod}; ({ parseFromUtc })`, {});
{
    assert.equal(parseFromUtc('2026-11-23T13:03:21Z'), '2026-11-23T13:03:21.000Z');
    assert.equal(parseFromUtc('2026-11-23T15:03:21+02:00'), '2026-11-23T13:03:21.000Z');
    for (const invalid of ['', null, undefined, 'yesterday', '2026-11-23T13:03:21', '2026-13-01T00:00:00Z'])
        assert.equal(parseFromUtc(invalid), '', `expected ignored value: ${invalid}`);
    console.log('PASS: logs period accepts only ISO date-time with a zone and normalizes it to UTC');
}

const fetchMoreSource = sliceBetween(logs, '    async function fetchMore() {', '    async function reload() {');

async function eventRequestUrls({ viewMode, activeFromUtc, activeLevel }) {
    const calls = [];
    const sandbox = {
        URLSearchParams,
        calls,
        BF: { request: async url => { calls.push(url); return viewMode === 'groups' ? { groups: [] } : []; } },
        document: { getElementById: () => ({ innerHTML: '' }) },
        extractEvents: () => [],
        normalizeEvent: event => event,
        normalizeGroup: group => group
    };
    await vm.runInNewContext(`(async () => {
        let loading = false, hasMore = true, allLogs = [], lastAfterId = null, groupMetadata = null, seqUnavailable = false;
        const pageSize = 50;
        let activeService = '', searchQuery = '', activeContext = null;
        let viewMode = ${JSON.stringify(viewMode)};
        let activeFromUtc = ${JSON.stringify(activeFromUtc)};
        let activeLevel = ${JSON.stringify(activeLevel)};
        ${fetchMoreSource}
        await fetchMore();
    })()`, sandbox);
    return calls.map(url => new URL(url, 'http://admin.test').searchParams);
}

(async () => {
    const [withPeriod] = await eventRequestUrls({ viewMode: 'events', activeFromUtc: '2026-11-23T13:03:21.000Z', activeLevel: 'Error' });
    assert.equal(withPeriod.get('fromUtc'), '2026-11-23T13:03:21.000Z');
    assert.equal(withPeriod.get('level'), 'Error');

    const [withoutPeriod] = await eventRequestUrls({ viewMode: 'events', activeFromUtc: '', activeLevel: '' });
    assert.equal(withoutPeriod.has('fromUtc'), false);

    const [groupsRequest] = await eventRequestUrls({ viewMode: 'groups', activeFromUtc: '2026-11-23T13:03:21.000Z', activeLevel: 'Error' });
    assert.equal(groupsRequest.has('fromUtc'), false);
    console.log('PASS: events request sends fromUtc only in events mode and only when a period is set');
})();
