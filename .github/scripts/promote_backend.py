"""Copy tested backend images nightly -> dev -> master without rebuilding them.

For every service the upstream `latest` image is copied by digest when it differs from the
target `latest`. The copy is allowed only if the image was built from a commit contained in the
current branch and the service sources did not change after that commit; otherwise the upstream
image does not match this branch and the service fails instead of shipping the wrong binary.
"""
import base64
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

sys.path.insert(0, str(Path(__file__).resolve().parent))
from ci_plan import SERVICES  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
REGISTRY = 'docker.barkfluff.com'
REVISION_LABEL = 'org.opencontainers.image.revision'
ACCEPT = ', '.join((
    'application/vnd.oci.image.index.v1+json',
    'application/vnd.oci.image.manifest.v1+json',
    'application/vnd.docker.distribution.manifest.list.v2+json',
    'application/vnd.docker.distribution.manifest.v2+json',
))


class PromotionError(Exception):
    pass


def git(*args):
    return subprocess.run(['git', *args], cwd=ROOT, capture_output=True, text=True)


def pathspec(pattern):
    return f':(glob){pattern}' if '*' in pattern else pattern


def check_revision(revision, sources, head='HEAD'):
    """Raise unless the image built from `revision` is valid for `head`."""
    if not revision:
        raise PromotionError(f'у образа нет метки {REVISION_LABEL}; пересоберите nightly')
    if git('cat-file', '-e', f'{revision}^{{commit}}').returncode:
        raise PromotionError(f'коммит образа {revision[:12]} не найден в репозитории')
    if git('merge-base', '--is-ancestor', revision, head).returncode:
        raise PromotionError(f'образ собран из {revision[:12]}, которого нет в этой ветке')
    if git('diff', '--quiet', revision, head, '--', *map(pathspec, sources)).returncode:
        raise PromotionError(f'исходники изменились после {revision[:12]}: '
                             'сборка nightly для актуального коммита отсутствует или упала')


class Registry:
    def __init__(self, user, password):
        token = base64.b64encode(f'{user}:{password}'.encode()).decode()
        self.headers = {'Authorization': f'Basic {token}', 'Accept': ACCEPT}

    def request(self, path, method='GET'):
        request = urllib.request.Request(f'https://{REGISTRY}/v2/{path}', headers=self.headers, method=method)
        for attempt in range(3):
            try:
                with urllib.request.urlopen(request, timeout=30) as response:
                    return response.headers, response.read()
            except urllib.error.HTTPError:
                raise
            except OSError:
                if attempt == 2:
                    raise
                time.sleep(5)

    def digest(self, repository, reference):
        try:
            headers, _ = self.request(f'{repository}/manifests/{reference}', 'HEAD')
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            raise
        return headers.get('Docker-Content-Digest')

    def json(self, path):
        return json.loads(self.request(path)[1])

    def labels(self, repository, digest):
        manifest = self.json(f'{repository}/manifests/{digest}')
        if 'manifests' in manifest:
            images = [m for m in manifest['manifests']
                      if m.get('platform', {}).get('os') == 'linux'
                      and m.get('platform', {}).get('architecture') == 'amd64']
            if len(images) != 1:
                raise PromotionError('в индексе образа нет единственного linux/amd64')
            manifest = self.json(f'{repository}/manifests/{images[0]["digest"]}')
        config = self.json(f'{repository}/blobs/{manifest["config"]["digest"]}')
        return config.get('config', {}).get('Labels') or {}


def compute_version(image, user, password):
    with tempfile.NamedTemporaryFile('w+', delete=False) as output:
        path = output.name
    try:
        env = dict(os.environ, IMAGE=image, REG_USER=user, REG_PASS=password, GITHUB_OUTPUT=path)
        result = subprocess.run(['bash', str(ROOT / '.github/actions/docker-version/compute-version.sh')],
                                env=env, capture_output=True, text=True)
        sys.stderr.write(result.stderr)
        if result.returncode:
            raise PromotionError('не удалось вычислить версию: ' + (result.stderr.strip().splitlines() or ['?'])[-1])
        return dict(line.split('=', 1) for line in Path(path).read_text().splitlines() if '=' in line)
    finally:
        os.unlink(path)


def copy_image(source, tags, attempts=6, delay=15):
    command = ['docker', 'buildx', 'imagetools', 'create']
    for tag in tags:
        command += ['-t', tag]
    for attempt in range(1, attempts + 1):
        if subprocess.run([*command, source]).returncode == 0:
            return
        if attempt < attempts:
            print(f'Попытка {attempt}/{attempts} не удалась, повтор через {delay} с...', file=sys.stderr)
            time.sleep(delay)
    raise PromotionError(f'не удалось скопировать образ после {attempts} попыток')


def promote(service, registry, user, password):
    outputs = compute_version(service['image'], user, password)
    source = outputs['source_image']
    source_repository, source_digest = source[len(REGISTRY) + 1:].split('@')
    tags = outputs['tags'].split(',')
    target_repository = tags[0][len(REGISTRY) + 1:].rsplit(':', 1)[0]
    if registry.digest(target_repository, 'latest') == source_digest:
        return 'unchanged', outputs['version']
    revision = registry.labels(source_repository, source_digest).get(REVISION_LABEL, '')
    check_revision(revision, service['sources'])
    copy_image(source, tags)
    return 'promoted', outputs['version']


def main():
    user, password = os.environ['REGISTRY_USERNAME'], os.environ['REGISTRY_PASSWORD']
    results = Path(os.environ['RESULTS_DIR'])
    results.mkdir(parents=True, exist_ok=True)
    registry = Registry(user, password)
    for attempt in range(1, 7):
        login = subprocess.run(['docker', 'login', REGISTRY, '-u', user, '--password-stdin'],
                               input=password, text=True)
        if login.returncode == 0:
            break
        if attempt == 6:
            print('::error::Не удалось войти в реестр после 6 попыток')
            return 1
        time.sleep(15)
    failed = False
    summary = []
    for service in SERVICES:
        name = service['name']
        print(f'::group::{name}')
        try:
            status, version = promote(service, registry, user, password)
            line = f'{"⏭" if status == "unchanged" else "✅"} {name} {version}'
            if status == 'promoted':
                (results / f'promote-{name}.txt').write_text(f'success|🐳 {name}|{version}\n', encoding='utf-8')
        except (PromotionError, urllib.error.URLError, KeyError, ValueError) as error:
            failed = True
            line = f'❌ {name}: {error}'
            print(f'::error title={name}::{error}')
            (results / f'promote-{name}.txt').write_text(f'failure|🐳 {name}|{error}\n', encoding='utf-8')
        print(line)
        summary.append(line)
        print('::endgroup::')
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as output:
            output.write('\n'.join(['### Перенос образов', '', *[f'- {line}' for line in summary]]) + '\n')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
