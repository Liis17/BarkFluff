"""Offline client-version checks; set TEST_PWSH to also run WinUI's real step."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import textwrap
import unittest

ROOT = Path(__file__).resolve().parents[2]


def workflow_script(step_name):
    workflow = ROOT / '.github/workflows/ci.yml'
    step = workflow.read_text().split(f'      - name: {step_name}\n', 1)[1]
    step = step.split('\n      - name:', 1)[0]
    return textwrap.dedent(step.split('        run: |\n', 1)[1])


class ClientVersionTests(unittest.TestCase):
    def run_version(self, branch, body='{"version":"1.2.9"}', exit_code=0, ref_type='branch', app='barkfluffkotlin',
                    ca_file='mock-ca.pem'):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            curl = directory / 'curl'
            curl.write_text('''#!/usr/bin/env python3
import os, pathlib, sys
pathlib.Path(os.environ['REQUESTS']).write_text(sys.argv[-1])
print(os.environ['BODY'])
sys.exit(int(os.environ['CURL_EXIT']))
''')
            curl.chmod(0o755)
            output = directory / 'output'
            output.touch()
            requests = directory / 'requests'
            env = dict(os.environ, PATH=f'{directory}{os.pathsep}{os.environ["PATH"]}',
                       CLIENT_APP=app, CA_FILE=ca_file, GITHUB_OUTPUT=str(output),
                       GITHUB_REF_NAME=branch, GITHUB_REF_TYPE=ref_type,
                       BODY=body, CURL_EXIT=str(exit_code), REQUESTS=str(requests))
            result = subprocess.run(['/bin/bash', str(ROOT / '.github/scripts/client-version.sh')],
                                    env=env, text=True, capture_output=True)
            return result, output.read_text(), requests.read_text() if requests.exists() else ''

    def test_branch_policy_for_both_clients(self):
        for app in ['barkfluffkotlin', 'barkfluffmacos', 'barkfluffwinui']:
            for branch, source, version in [('nightly', 'nightly', '1.2.10'),
                                            ('dev', 'nightly', '1.2.9'), ('master', 'dev', '1.2.9')]:
                with self.subTest(app=app, branch=branch):
                    result, output, request = self.run_version(branch, app=app)
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertEqual(output, f'version={version}\n')
                    self.assertEqual(request, f'https://storage.barkfluff.com/get/{app}/{source}/version')

    def test_ca_file_is_optional(self):
        result, output, _ = self.run_version('dev', app='barkfluffwinui', ca_file='')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(output, 'version=1.2.9\n')

    def test_bad_or_missing_version_never_falls_back(self):
        bodies = ['', '{', '{}', 'null', '[]', '{"version":123}', '{"version":""}',
                  '{"version":"1.2"}', '{"version":"1.2.3 beta"}', '{"version":"01.2.3"}']
        for branch in ['nightly', 'dev', 'master']:
            for body in bodies:
                with self.subTest(branch=branch, body=body):
                    result, output, _ = self.run_version(branch, body)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertEqual(output, '')

    def test_http_and_transport_failures_never_write_a_version(self):
        for branch in ['nightly', 'dev', 'master']:
            for code in [22, 28, 60]:
                with self.subTest(branch=branch, code=code):
                    result, output, _ = self.run_version(branch, exit_code=code)
                    self.assertEqual(result.returncode, code)
                    self.assertEqual(output, '')

    def test_unsupported_branch_and_tag_do_not_request_storage(self):
        for branch, ref_type in [('feature', 'branch'), ('master', 'tag')]:
            result, output, request = self.run_version(branch, ref_type=ref_type)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(output, '')
            self.assertEqual(request, '')


PWSH = os.environ.get('TEST_PWSH') or shutil.which('pwsh')


@unittest.skipUnless(PWSH, 'Set TEST_PWSH or install pwsh for WinUI checks')
class WinUIManifestTests(unittest.TestCase):
    """The version itself comes from client-version.sh; this step only applies it to the manifests."""

    def run_variant(self, channel, version):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            client = directory / 'Windows/BarkFluff.Client.WinUI'
            client.mkdir(parents=True)
            for filename in ['Package.appxmanifest', 'app.manifest']:
                shutil.copyfile(ROOT / 'Windows/BarkFluff.Client.WinUI' / filename, client / filename)
            original = (client / 'Package.appxmanifest').read_text()
            output = directory / 'output'
            output.touch()
            script = directory / 'test.ps1'
            script.write_text("$ErrorActionPreference = 'Stop'\n" + workflow_script('Подготовка варианта MSIX и версии'))
            branch = 'master' if channel == 'release' else channel
            suffix = '' if channel == 'release' else '.' + channel
            env = dict(os.environ, GITHUB_WORKSPACE=str(directory), RUNNER_TEMP=str(directory),
                       GITHUB_RUN_ID='offline', GITHUB_OUTPUT=str(output), CHANNEL=channel,
                       BRANCH_NAME=branch, ASSET_DIRECTORY=branch,
                       IDENTITY_NAME='7895OrbitinSpace.Barkfluff' + suffix.capitalize(),
                       DISPLAY_NAME='Barkfluff' + suffix, SHORT_NAME='barkfluff' + suffix,
                       PACKAGE_VERSION=version)
            result = subprocess.run([PWSH, '-NoLogo', '-NoProfile', '-File', str(script)],
                                    env=env, text=True, capture_output=True)
            return result, output.read_text(encoding='utf-8-sig'), original == (client / 'Package.appxmanifest').read_text()

    def test_channel_manifests_use_computed_version(self):
        for channel in ['nightly', 'dev', 'release']:
            with self.subTest(channel=channel):
                result, output, unchanged = self.run_variant(channel, '1.2.10')
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn('version=1.2.10\n', output)
                self.assertIn('manifest_version=1.2.10.0\n', output)
                self.assertFalse(unchanged)

    def test_invalid_or_out_of_range_version_keeps_manifest(self):
        for version in ['', '1.2', '1.2.3 beta', 'a.b.c', '65536.1.1', '1.2.65536', '1.2.3.4']:
            with self.subTest(version=version):
                result, output, unchanged = self.run_variant('nightly', version)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(output, '')
                self.assertTrue(unchanged)

    def test_valid_version_equal_to_original_application_manifest(self):
        result, output, _ = self.run_variant('dev', '1.0.0')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('manifest_version=1.0.0.0\n', output)


if __name__ == '__main__':
    unittest.main()
