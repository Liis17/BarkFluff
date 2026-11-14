"""Select what a push to nightly/dev/master builds and publish the backend service catalog."""
from fnmatch import fnmatchcase
import json
import os
from pathlib import Path
import posixpath
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
# Image inputs outside the .csproj graph: SDK pin, Docker context and the Lognara agent layer.
BACKEND_COMMON = ('global.json', '.dockerignore', 'Docker/Lognara/**')
# Files that change how images are built or published, not what they contain.
BACKEND_TOOLING = ('.github/actions/docker-version/**', '.github/scripts/push-docker-image.sh',
                   'Backend/rebuild.trigger')
# MSBuild items whose Include is not a file path.
NON_FILE_ITEMS = ('PackageReference', 'FrameworkReference', 'Using', 'InternalsVisibleTo', 'PackageVersion')


def project_inputs(root, csproj, inputs, seen):
    """Collect repository paths a project builds from: its folder, referenced projects and linked files."""
    if csproj in seen or not (root / csproj).is_file():
        return
    seen.add(csproj)
    directory = posixpath.dirname(csproj)
    inputs.add(directory + '/')
    for item in ET.parse(root / csproj).iter():
        tag = item.tag.rsplit('}', 1)[-1]
        include = item.attrib.get('Include', '')
        if tag in NON_FILE_ITEMS or not include or '$(' in include:
            continue
        target = posixpath.normpath(posixpath.join(directory, include.replace(chr(92), '/')))
        if target.startswith('..'):
            continue
        if tag == 'ProjectReference':
            project_inputs(root, target, inputs, seen)
        elif '*' in target:
            inputs.add(target.split('*', 1)[0])
        else:
            inputs.add(target)


def project_patterns(csproj, root=ROOT):
    """Patterns for everything a project builds from, so a change elsewhere in Shared/ does not rebuild it."""
    inputs = set()
    project_inputs(root, csproj, inputs, set())
    patterns = set()
    for path in inputs:
        if path.endswith('/'):
            patterns.add(path + '**')
        elif (root / path).is_file():
            patterns.add(path)
        else:  # wildcard prefix such as Protos/api_ from Protos/api_*.proto
            patterns.add(posixpath.dirname(path) + '/**')
    return sorted(patterns)


def service(name, directory, project=None, dockerfile='Dockerfile.slim', extra=(), common=BACKEND_COMMON,
            node=None):
    project = project or directory.rsplit('/', 1)[-1]
    project_path = f'{directory}/{project}.csproj'
    return {
        'name': name,
        'image': f'barkfluff-{name}',
        'project': project_path,
        'dockerfile': f'./{directory}/{dockerfile}',
        'node_dir': node[0] if node else '',
        'node_version': node[1] if node else '',
        # Source inputs: a published image is valid for a commit only if these are unchanged.
        'sources': sorted({f'{directory}/**', *extra, *common, *project_patterns(project_path)}),
    }


SERVICES = [
    service('admin-panel', 'Backend/Barkfluff.AdminPanel', extra=('Icons/**',)),
    service('beacon', 'Backend/BarkFluff.Beacon'),
    service('bftproxy', 'Backend/BarkFluff.BftProxy', dockerfile='Dockerfile',
            common=('global.json', '.dockerignore')),
    service('bots', 'Backend/BarkFluff.Bots'),
    service('calls', 'Backend/BarkFluff.Calls'),
    service('client-storage', 'Backend/BarkFluff.ClientStorage'),
    service('cloud-messaging', 'Backend/Barkfluff.CloudMessaging'),
    service('developers', 'Backend/Barkfluff.Developers', extra=('Frontend/Developers/**', 'Shared/BarkFluff.Proto/**'),
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
    # The Docker build bundles every .proto for the browser client.
    service('web', 'Backend/BarkFluff.Web', extra=('Icons/**', 'Shared/BarkFluff.Proto/**')),
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
              'Windows/BarkFluff.WebApi.Core/**', 'Windows/BarkFluff.ClientV2.WPF/**',
              *project_patterns('Windows/BarkFluff.Client.WinUI/BarkFluff.Client.WinUI.csproj'),
              'global.json', 'Windows/client-rebuild.trigger', CLIENT_VERSION),
    'builder': ('Tools/BarkFluff.Builder/**', 'Backend/BarkFluff.Settings/Catalog/NodeSubdomains.cs',
                'Docker/nightly/barkfluff/**', 'Docker/nightly/nginx/**', 'Docker/Lognara/agent/**',
                'global.json', '**/*.props', '**/*.targets'),
}


def matches(path, pattern):
    if pattern.startswith('**/'):
        return fnmatchcase(path.rsplit('/', 1)[-1], pattern[3:])
    if pattern.endswith('/**'):
        return path.startswith(pattern[:-2])
    return path == pattern


def touches(paths, patterns):
    return any(matches(path, pattern) for path in paths for pattern in patterns)


def plan(branch, paths):
    """paths=None means everything (manual run, new branch or unknown base).

    Editing ci.yml itself publishes nothing: it also holds PR checks and notifications.
    After changing build steps, rebuild explicitly with workflow_dispatch mode=build.
    """
    rebuild_all = paths is None
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
