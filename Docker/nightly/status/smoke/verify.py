#!/usr/bin/env python3
"""CI-only проверки HTTPS-smoke страницы статуса.

states  <report.json>            состояния тестовой ноды, покрытие, отсутствие адресов целей в ответе
restart <before.json> <after.json>  история за 90 дней не потеряна после перезапуска контейнера
"""
import json
import sys

ORDER = [
    'beacon', 'bots', 'calls', 'developers', 'fast-auth', 'federation', 'files',
    'identity', 'messages', 'onliner', 'updates', 'users', 'web',
]

# Состояния, которые задают mock-компоненты smoke-стенда (см. mock-node.conf).
EXPECTED = {
    'beacon': 'operational',       # grpc_pass, healthy
    'bots': 'operational',         # proxy_pass, healthy
    'calls': 'down',               # 200 с HTML: непарсящийся ответ
    'developers': 'operational',   # proxy_pass, healthy
    'fast-auth': 'down',           # 301: редирект не выполняется
    'federation': 'operational',   # grpc_pass, SPKI-пин
    'files': 'starting',           # свежий starting в пределах grace
    'identity': 'operational',     # grpc_pass, healthy
    'messages': 'operational',     # grpc_pass, healthy
    'onliner': 'down',             # 503 JSON через grpc_pass
    'updates': 'stale',            # checkedAtUtc из 2020 года
    'users': 'degraded',           # 200 degraded
    'web': 'operational',          # proxy_pass, healthy
}

# Адреса стенда, внутренние имена mock и тексты ошибок не должны попадать в публичный ответ.
FORBIDDEN = ('smoke.test', 'https://', 'http://', 'mock', 'connection refused', 'gateway')


def load(path):
    with open(path, encoding='utf-8') as handle:
        return handle.read()


def check_states(path):
    raw = load(path)
    errors = [f'в ответе есть запрещённая подстрока: {token!r}' for token in FORBIDDEN if token in raw]
    report = json.loads(raw)
    services = {service['key']: service for service in report['services']}

    if [service['key'] for service in report['services']] != ORDER:
        errors.append('набор или порядок сервисов отличается от публичного каталога')
    for key, state in EXPECTED.items():
        actual = services.get(key, {}).get('state')
        if actual != state:
            errors.append(f'{key}: состояние {actual!r}, ожидалось {state!r}')
    if report.get('overallState') != 'partial_outage':
        errors.append(f"overallState {report.get('overallState')!r}, ожидалось 'partial_outage'")

    for key, service in services.items():
        if len(service['days']) != 90:
            errors.append(f'{key}: окно {len(service["days"])} дней вместо 90')
        coverage = service['coveragePercent']
        if coverage is None or not 0 < coverage <= 100:
            errors.append(f'{key}: покрытие {coverage!r} вне диапазона (0, 100]')
    if services['messages']['uptimePercent'] != 100.0:
        errors.append(f"messages: доступность {services['messages']['uptimePercent']}, ожидалось 100")
    if services['onliner']['uptimePercent'] != 0.0:
        errors.append(f"onliner: доступность {services['onliner']['uptimePercent']}, ожидалось 0")
    if services['users']['uptimePercent'] != 100.0 or services['users']['degradedSlots'] <= 0:
        errors.append('users: деградация должна считаться доступной и отмечаться отдельно')
    for key in ('updates', 'files'):
        if services[key]['uptimePercent'] is not None:
            errors.append(f'{key}: stale/starting не должны входить в доступность')
    return errors


def observed(report, key):
    service = next(s for s in report['services'] if s['key'] == key)
    return sum(day['observed'] for day in service['days'])


def check_restart(before_path, after_path):
    before = json.loads(load(before_path))
    after = json.loads(load(after_path))
    errors = []
    for key in ('messages', 'onliner', 'users'):
        was, now = observed(before, key), observed(after, key)
        if was <= 0:
            errors.append(f'{key}: до перезапуска нет истории')
        if now < was:
            errors.append(f'{key}: история потеряна после перезапуска ({was} → {now})')
    return errors


def main(argv):
    if len(argv) == 3 and argv[1] == 'states':
        errors = check_states(argv[2])
    elif len(argv) == 4 and argv[1] == 'restart':
        errors = check_restart(argv[2], argv[3])
    else:
        print(__doc__)
        return 2
    for error in errors:
        print(f'FAIL: {error}')
    if not errors:
        print(f'OK: {argv[1]}')
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
