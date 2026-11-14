"""Select affected PR test suites and validate their aggregate result."""
import json
import os
from pathlib import Path
import subprocess
import sys

from ci_plan import CLIENTS, matches

SUITES = ('backend', 'windows', 'android', 'macos', 'scripts', 'frontend', 'builder')
JOBS = ('backend', 'clients', 'scripts', 'frontend', 'builder')
FRONTEND = ('Backend/BarkFluff.Web/', 'Frontend/Developers/', 'Backend/BarkFluff.Navigator/Admin/',
            'Shared/BarkFluff.Proto/')
BUILDER = (*CLIENTS['builder'], 'Tests/BarkFluff.Builder.Tests/**')


def select_suites(paths):
    selected = dict.fromkeys(SUITES, False)
    for path in paths:
        if path in ('.github/workflows/ci.yml', '.github/scripts/pr_tests.py', '.github/scripts/ci_plan.py'):
            return dict.fromkeys(SUITES, True)
        if path.startswith('Shared/'):
            selected.update(dict.fromkeys(SUITES, True))
        if path.startswith(('Backend/', 'Frontend/Developers/', 'Docker/', 'Icons/', 'Tools/')):
            selected['backend'] = True
        if path.startswith('Tests/'):
            selected['windows' if path.startswith(('Tests/BarkFluff.Client.WinUI.Tests/',
                                                  'Tests/BarkFluff.ClientV2.WPF.Tests/'))
                     else 'backend'] = True
        if path == 'Docs/BFTv1.md' or path.startswith('Docs/bft/'):
            selected['backend'] = True
        if path.startswith('Windows/'):
            selected['windows'] = True
        if path.startswith('Android/') or path.startswith('Backend/Barkfluff.WebServer/html/legal/'):
            selected['android'] = True
        if path.startswith('Mac/'):
            selected['macos'] = True
        if path == 'global.json' or path.endswith(('.props', '.targets')):
            selected['backend'] = selected['windows'] = True
        if path == '.dockerignore':
            selected['backend'] = True
        if path == '.github/workflows/test-backend.yml':
            selected['backend'] = True
        if path == '.github/workflows/test-clients.yml':
            selected.update(dict.fromkeys(('windows', 'android', 'macos'), True))
        if path.startswith(FRONTEND):
            selected['frontend'] = True
        if any(matches(path, pattern) for pattern in BUILDER):
            selected['builder'] = True
        if path.startswith(('.github/', 'Backend/BarkFluff.Web/', 'Backend/Barkfluff.AdminPanel/',
                            'Tests/BarkFluff.AdminPanel.Tests/Frontend/', 'Windows/BarkFluff.Client.WinUI/')):
            selected['scripts'] = True
    return selected


def selected_jobs(suites):
    return {
        'backend': suites['backend'],
        'clients': any(suites[platform] for platform in ('windows', 'android', 'macos')),
        'scripts': suites['scripts'],
        'frontend': suites['frontend'],
        'builder': suites['builder'],
    }


def results_pass(selection_result, selected, results, guard_result='success'):
    """Every selected job must succeed and every other one must be skipped.

    Promotion PRs (into dev/master) skip selection entirely: their code was tested on the way
    into nightly, so only the source-branch guard has to pass.
    """
    if guard_result != 'success':
        return False
    if selection_result == 'skipped':
        return all(results[job] == 'skipped' for job in JOBS)
    return selection_result == 'success' and all(
        results[job] == ('success' if selected.get(job) else 'skipped') for job in JOBS)


def main():
    if sys.argv[1:] == ['--check']:
        selected = json.loads(os.environ.get('SELECTED_JOBS') or '{}')
        results = {job: os.environ[job.upper()] for job in JOBS}
        print(f'Guard: {os.environ["GUARD"]}; selected jobs: {selected}; results: {results}')
        return 0 if results_pass(os.environ['SELECTION_RESULT'], selected, results, os.environ['GUARD']) else 1

    if os.environ['GITHUB_EVENT_NAME'] == 'workflow_dispatch':
        suites = dict.fromkeys(SUITES, True)
    else:
        # checkout fetch-depth: 2 includes the synthetic PR merge and its base parent.
        # Comparing their trees excludes changes already present in the target branch.
        paths = subprocess.check_output([
            'git', 'diff', '--no-renames', '--name-only', '-z', 'HEAD^1', 'HEAD'
        ]).decode('utf-8').split('\0')
        suites = select_suites(paths)

    outputs = {name: str(enabled).lower() for name, enabled in suites.items()}
    outputs['platforms'] = json.dumps([name for name in ('windows', 'android', 'macos') if suites[name]])
    outputs['selected'] = json.dumps(selected_jobs(suites))
    with Path(os.environ['GITHUB_OUTPUT']).open('a', encoding='utf-8') as output:
        for name, value in outputs.items():
            output.write(f'{name}={value}\n')
    print(json.dumps(suites))
    return 0


if __name__ == '__main__':
    sys.exit(main())
