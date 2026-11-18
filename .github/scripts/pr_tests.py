"""Select affected PR test suites and validate their aggregate result."""
from functools import lru_cache
import json
import os
from pathlib import Path
import subprocess
import sys

from ci_plan import CLIENTS, matches, project_inputs

SUITES = ('backend', 'windows', 'android', 'macos', 'scripts', 'frontend', 'builder')
JOBS = ('backend', 'clients', 'scripts', 'frontend', 'builder')
FRONTEND = ('Backend/BarkFluff.Web/', 'Frontend/Developers/', 'Backend/BarkFluff.Navigator/Admin/',
            'Shared/BarkFluff.Proto/')
BUILDER = (*CLIENTS['builder'], 'Tests/BarkFluff.Builder.Tests/**')
ROOT = Path(__file__).resolve().parents[2]
SELECTOR_FILES = ('.github/workflows/ci.yml', '.github/scripts/pr_tests.py', '.github/scripts/ci_plan.py')
# Change how all .NET code is built or tested: every backend test project runs.
BACKEND_EVERYTHING = ('global.json', '.dockerignore', '.github/workflows/test-backend.yml')
# Paths tests read at runtime without an MSBuild item.
BFT_SPEC = ('Docs/BFTv1.md', 'Docs/bft/')
RUNTIME_INPUTS = ((BFT_SPEC, {'BarkFluff.Bft.Tests', 'BarkFluff.BftProxy.Tests'}),)
# Always added by test-backend.yml when a project list is given.
COMMON_BACKEND_TESTS = 'BarkFluff.GrpcServer.Tests'
# Code outside any known test input: run the whole backend suite rather than miss a test.
BACKEND_FALLBACK = ('Backend/', 'Shared/', 'Frontend/Developers/', 'Docker/', 'Icons/', 'Tools/', 'Tests/')


@lru_cache(maxsize=None)
def test_projects(root=ROOT):
    """{test project name: (is Windows-only, repository paths it depends on)}."""
    projects = {}
    for csproj in sorted((root / 'Tests').glob('*/*.csproj')):
        inputs = set()
        project_inputs(root, csproj.relative_to(root).as_posix(), inputs, set())
        projects[csproj.stem] = ('-windows' in csproj.read_text(encoding='utf-8'), inputs)
    return projects


@lru_cache(maxsize=None)
def untested_services(root):
    """Backend project folders that no test project builds from."""
    tested = set().union(*(inputs for _, inputs in test_projects(root).values()))
    return tuple(f'{csproj.parent.relative_to(root).as_posix()}/'
                 for csproj in sorted((root / 'Backend').glob('*/*.csproj'))
                 if f'{csproj.parent.relative_to(root).as_posix()}/' not in tested)


def depends(path, inputs):
    return any(path == item or (item.endswith('/') and path.startswith(item)) for item in inputs)


def select(paths, projects=None):
    """Return (suites, backend test projects); None for projects means every backend test project."""
    projects = test_projects() if projects is None else projects
    selected = dict.fromkeys(SUITES, False)
    backend_tests, backend_everything = set(), False
    for path in paths:
        if not path:
            continue
        if path in SELECTOR_FILES:
            return dict.fromkeys(SUITES, True), None
        users = {name for name, (_, inputs) in projects.items() if depends(path, inputs)}
        windows_users = {name for name in users if projects[name][0]}
        backend_users = users - windows_users
        for prefixes, names in RUNTIME_INPUTS:
            if path.startswith(prefixes):
                backend_users |= names
        if windows_users or path.startswith('Windows/'):
            selected['windows'] = True
        if path in BACKEND_EVERYTHING or path.endswith(('.props', '.targets')):
            selected['backend'] = selected['windows'] = backend_everything = True
        elif backend_users:
            selected['backend'] = True
            backend_tests |= backend_users
        elif path.startswith(untested_services(ROOT)):
            # Services without their own test project are covered only by the common GrpcServer tests.
            selected['backend'] = True
        elif path.startswith(BACKEND_FALLBACK) and not windows_users and not path.endswith('.md'):
            selected['backend'] = backend_everything = True
        if path.startswith(('Android/', 'Backend/Barkfluff.WebServer/html/legal/', *BFT_SPEC)):
            selected['android'] = True
        if path.startswith('Mac/'):
            selected['macos'] = True
        if path == '.github/workflows/test-clients.yml':
            selected.update(dict.fromkeys(('windows', 'android', 'macos'), True))
        if path.startswith(FRONTEND):
            selected['frontend'] = True
        if any(matches(path, pattern) for pattern in BUILDER):
            selected['builder'] = True
        if path.startswith(('.github/', 'Backend/BarkFluff.Web/', 'Backend/Barkfluff.AdminPanel/',
                            'Tests/BarkFluff.AdminPanel.Tests/Frontend/', 'Windows/BarkFluff.Client.WinUI/')):
            selected['scripts'] = True
    if backend_everything or not selected['backend']:
        return selected, None
    return selected, sorted(backend_tests | {COMMON_BACKEND_TESTS})


def select_suites(paths, projects=None):
    return select(paths, projects)[0]


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
        suites, backend_projects = dict.fromkeys(SUITES, True), None
    else:
        # checkout fetch-depth: 2 includes the synthetic PR merge and its base parent.
        # Comparing their trees excludes changes already present in the target branch.
        paths = subprocess.check_output([
            'git', 'diff', '--no-renames', '--name-only', '-z', 'HEAD^1', 'HEAD'
        ]).decode('utf-8').split('\0')
        suites, backend_projects = select(paths)

    outputs = {name: str(enabled).lower() for name, enabled in suites.items()}
    outputs['platforms'] = json.dumps([name for name in ('windows', 'android', 'macos') if suites[name]])
    outputs['selected'] = json.dumps(selected_jobs(suites))
    # Empty means every backend test project (the test-backend.yml default).
    outputs['backend_projects'] = ','.join(backend_projects or [])
    with Path(os.environ['GITHUB_OUTPUT']).open('a', encoding='utf-8') as output:
        for name, value in outputs.items():
            output.write(f'{name}={value}\n')
    print(json.dumps(suites))
    print('backend test projects:', ', '.join(backend_projects) if backend_projects else 'all')
    return 0


if __name__ == '__main__':
    sys.exit(main())
