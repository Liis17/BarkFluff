"""Build one Telegram message (legacy Markdown) for a CI run and send it via send-telegram.sh."""
import json
import os
from pathlib import Path
import subprocess
import sys

ICONS = {'success': '✅', 'failure': '❌', 'cancelled': '⛔', 'skipped': '⏭'}
BUILD_JOBS = {'backend': '🐳 Backend-образы', 'promote': '🔁 Перенос backend-образов', 'android': '📱 Android',
              'macos': '🍎 macOS', 'winui': '🪟 WinUI', 'builder': '🧰 Builder'}
SUITES = (('guard', 'Ветка-источник'), ('backend', 'Backend тесты'), ('clients', 'Клиенты'),
          ('scripts', 'Скрипты CI'), ('frontend', 'Frontend'), ('builder', 'Builder'))


def code(value):
    """Inline code is the only place where Markdown does not need escaping, except for backticks."""
    return '`' + str(value).replace('`', "'") + '`'


def plain(value):
    return ''.join(ch for ch in str(value) if ch not in '*_`[]')


def read_results(directory):
    """Result files are named <job>[-<item>].txt and hold `status|label|detail` lines."""
    results, failed_jobs = [], set()
    for path in sorted(Path(directory).rglob('*.txt')) if directory and Path(directory).is_dir() else []:
        job = path.stem.split('-', 1)[0]
        for line in path.read_text(encoding='utf-8').splitlines():
            status, label, detail = (line.split('|', 2) + ['', ''])[:3]
            if status:
                results.append((status, label, detail))
                if status != 'success':
                    failed_jobs.add(job)
    return results, failed_jobs


def missing_failures(needs, failed_jobs):
    """Failed jobs that left no failure line of their own (crash, timeout, cancel before the result step)."""
    return [(needs[job]['result'], label, '') for job, label in BUILD_JOBS.items()
            if needs.get(job, {}).get('result', 'skipped') not in ('success', 'skipped') and job not in failed_jobs]


def push_message(env, results, plan_result):
    failed = plan_result != 'success' or any(status != 'success' for status, _, _ in results)
    lines = [f'{"❌" if failed else "✅"} *Сборка {"с ошибками" if failed else "успешна"}*', '',
             f'📌 Ветка: {code(env["GITHUB_REF_NAME"])}']
    if plan_result != 'success':
        lines.append('❌ Не удалось определить состав сборки')
    for status, label, detail in results:
        lines.append(f'{ICONS.get(status, "❔")} {plain(label)}' + (f' {code(detail)}' if detail else ''))
    lines += ['', f'🔗 Коммит: {code(env["GITHUB_SHA"][:12])}', f'👤 Автор: {code(env["GITHUB_ACTOR"])}']
    return '\n'.join(lines)


def pr_message(env):
    passed = env['REQUIRED'] == 'success'
    lines = [f'{"✅" if passed else "❌"} *Проверки PR #{plain(env["PR_NUMBER"])} '
             f'{"пройдены" if passed else "не пройдены"}*', '',
             f'🔀 {code(env["HEAD_REF"])} → {code(env["BASE_REF"])}',
             f'📝 {plain(env["PR_TITLE"])[:200]}']
    for key, label in SUITES:
        result = env.get(key.upper(), 'skipped')
        if result != 'skipped':
            lines.append(f'{ICONS.get(result, "❔")} {label}')
    lines.append(f'👤 Автор: {code(env["GITHUB_ACTOR"])}')
    return '\n'.join(lines)


def main():
    env = os.environ
    if not env.get('TG_TOKEN') or not env.get('TG_CHAT'):
        print('Telegram secrets недоступны (например, PR из fork) — уведомление пропущено')
        return 0
    if sys.argv[1:] == ['pr']:
        message = pr_message(env)
    else:
        results, failed_jobs = read_results(env.get('RESULTS_DIR'))
        results += missing_failures(json.loads(env.get('NEEDS') or '{}'), failed_jobs)
        if not results and env['PLAN_RESULT'] == 'success':
            print('Нечего собирать — уведомление не нужно')
            return 0
        message = push_message(env, results, env['PLAN_RESULT'])
    print(message)
    script = Path(__file__).resolve().parent / 'send-telegram.sh'
    return subprocess.run(['bash', str(script)], env=dict(env, TG_MESSAGE=message)).returncode


if __name__ == '__main__':
    sys.exit(main())
