"""Select what a push to nightly/dev/master builds and publish the backend service catalog."""
import json
import os
from pathlib import Path
import subprocess
import sys

CI_FILES = ('.github/workflows/ci.yml', '.github/scripts/ci_plan.py')

# Inputs shared by every backend image except BftProxy, which has no GrpcServer/Lognara dependency.
BACKEND_COMMON = ('global.json', '.dockerignore', 'Docker/Lognara/**', 'Shared/**',
                  'Backend/BarkFluff.GrpcServer/**')
# Files that change how images are built or published, not what they contain.
BACKEND_TOOLING = ('.github/actions/docker-version/**', '.github/scripts/push-docker-image.sh',
                   'Backend/rebuild.trigger')


def service(name, directory, project=None, dockerfile='Dockerfile.slim', extra=(), common=BACKEND_COMMON,
            node=None):
    project = project or directory.rsplit('/', 1)[-1]
    return {
        'name': name,
        'image': f'barkfluff-{name}',
        'project': f'{directory}/{project}.csproj',
        'dockerfile': f'./{directory}/{dockerfile}',
        'node_dir': node[0] if node else '',
        'node_version': node[1] if node else '',
        # Source inputs: a published image is valid for a commit only if these are unchanged.
        'sources': [f'{directory}/**', *extra, *common],
    }


SERVICES = [
    service('admin-panel', 'Backend/Barkfluff.AdminPanel', extra=('Icons/**',)),
    service('beacon', 'Backend/BarkFluff.Beacon'),
    service('bftproxy', 'Backend/BarkFluff.BftProxy', dockerfile='Dockerfile',
            common=('global.json', '.dockerignore', 'Shared/BarkFluff.Bft/**')),
    service('bots', 'Backend/BarkFluff.Bots'),
    service('calls', 'Backend/BarkFluff.Calls'),
    service('client-storage', 'Backend/BarkFluff.ClientStorage'),
    service('cloud-messaging', 'Backend/Barkfluff.CloudMessaging'),
    service('developers', 'Backend/Barkfluff.Developers', extra=('Frontend/Developers/**',),
            node=('Frontend/Developers', '20')),
    service('fast-auth', 'Backend/BarkFluff.FastAuth', extra=('Backend/BarkFluff.Identity/**',)),
    service('federation', 'Backend/BarkFluff.Federation'),
    service('files', 'Backend/BarkFluff.Files'),
    service('identity', 'Backend/BarkFluff.Identity'),
    service('messages', 'Backend/BarkFluff.Messages'),
    service('navigator', 'Backend/BarkFluff.Navigator', node=('Backend/BarkFluff.Navigator/Admin', '22')),
    service('notification', 'Backend/BarkFluff.Notification'),
    service('onliner', 'Backend/BarkFluff.Onliner'),
    service('settings', 'Backend/BarkFluff.Settings'),
    service('setup', 'Backend/BarkFluff.Setup'),
    service('updates', 'Backend/BarkFluff.Updates'),
    service('users', 'Backend/BarkFluff.Users'),
    service('web', 'Backend/BarkFluff.Web', extra=('Icons/**',)),
    service('webserver', 'Backend/Barkfluff.WebServer'),
]

CLIENT_VERSION = '.github/scripts/client-version.sh'
CLIENTS = {
    'android': ('Android/Barkfluff.Client.Android/**', 'Android/core/**', 'Android/settings.gradle.kts',
                'Android/build.gradle.kts', 'Android/gradle.properties', 'Android/gradle/**',
                'Android/gradlew', 'Android/gradlew.bat', 'Android/tools/**', 'Android/android-rebuild.trigger',
                # Legal markdown is packaged into the APK by copyLegalDocs.
                'Backend/Barkfluff.WebServer/html/legal/**', CLIENT_VERSION),
    'macos': ('Mac/Barkfluff/**', 'Mac/macos-rebuild.trigger', CLIENT_VERSION),
    'winui': ('Windows/BarkFluff.Client.WinUI/**', 'Windows/BarkFluff.Client.Core/**',
              'Windows/BarkFluff.WebApi.Core/**', 'Windows/BarkFluff.ClientV2.WPF/**', 'Shared/**',
              'global.json', 'Windows/client-rebuild.trigger', CLIENT_VERSION),
    'builder': ('Tools/BarkFluff.Builder/**', 'Backend/BarkFluff.Settings/Catalog/NodeSubdomains.cs',
                'Docker/nightly/barkfluff/**', 'Docker/nightly/nginx/**', 'Docker/Lognara/agent/**',
                'global.json', '**/*.props', '**/*.targets'),
}


def matches(path, pattern):
    if pattern.startswith('**/'):
        return path.endswith(pattern[2:])
    if pattern.endswith('/**'):
        return path.startswith(pattern[:-2])
    return path == pattern


def touches(paths, patterns):
    return any(matches(path, pattern) for path in paths for pattern in patterns)


def plan(branch, paths):
    """paths=None means everything (manual run, new branch or unknown base)."""
    rebuild_all = paths is None or touches(paths, CI_FILES)
    every = lambda patterns: rebuild_all or touches(paths, patterns)
    result = {name: every(patterns) for name, patterns in CLIENTS.items()}
    if branch == 'nightly':
        result['backend'] = [s for s in SERVICES if every((*s['sources'], *BACKEND_TOOLING))]
        result['promote'] = False
    else:
        # dev/master never rebuild images: they copy the tested upstream image when it differs.
        result['backend'] = []
        result['promote'] = True
    return result


def changed_paths(before, sha):
    if not before or set(before) == {'0'}:
        return None
    if subprocess.run(['git', 'cat-file', '-e', f'{before}^{{commit}}'], capture_output=True).returncode:
        return None
    output = subprocess.check_output(['git', 'diff', '--no-renames', '--name-only', '-z', before, sha])
    return [path for path in output.decode('utf-8').split('\0') if path]


def main():
    branch = os.environ['GITHUB_REF_NAME']
    if branch not in ('nightly', 'dev', 'master'):
        sys.exit(f'Ветка {branch} не привязана к каналу сборок')
    paths = None
    if os.environ['GITHUB_EVENT_NAME'] == 'push':
        paths = changed_paths(os.environ.get('BEFORE', ''), os.environ['GITHUB_SHA'])
    result = plan(branch, paths)
    outputs = {name: str(value).lower() for name, value in result.items() if isinstance(value, bool)}
    outputs['backend'] = json.dumps(result['backend'], separators=(',', ':'))
    outputs['has_backend'] = str(bool(result['backend'])).lower()
    outputs['services'] = json.dumps(SERVICES, separators=(',', ':'))
    with Path(os.environ['GITHUB_OUTPUT']).open('a', encoding='utf-8') as output:
        for name, value in outputs.items():
            output.write(f'{name}={value}\n')
    print(json.dumps({k: v for k, v in result.items() if k != 'backend'}))
    print('backend:', ', '.join(s['name'] for s in result['backend']) or '-')
    return 0


if __name__ == '__main__':
    sys.exit(main())
