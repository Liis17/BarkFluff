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
    workflow = ROOT / '.github/workflows/build-client-winui.yml'
    step = workflow.read_text().split(f'      - name: {step_name}\n', 1)[1]
    step = step.split('\n      - name:', 1)[0]
    return textwrap.dedent(step.split('        run: |\n', 1)[1])


class ClientVersionTests(unittest.TestCase):
    def run_version(self, branch, body='{"version":"1.2.9"}', exit_code=0, ref_type='branch', app='barkfluffkotlin'):
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
                       CLIENT_APP=app, CA_FILE='mock-ca.pem', GITHUB_OUTPUT=str(output),
                       GITHUB_REF_NAME=branch, GITHUB_REF_TYPE=ref_type,
                       BODY=body, CURL_EXIT=str(exit_code), REQUESTS=str(requests))
            result = subprocess.run(['/bin/bash', str(ROOT / '.github/scripts/client-version.sh')],
                                    env=env, text=True, capture_output=True)
            return result, output.read_text(), requests.read_text() if requests.exists() else ''

    def test_branch_policy_for_both_clients(self):
        for app in ['barkfluffkotlin', 'barkfluffmacos']:
            for branch, source, version in [('nightly', 'nightly', '1.2.10'),
                                            ('dev', 'nightly', '1.2.9'), ('master', 'dev', '1.2.9')]:
                with self.subTest(app=app, branch=branch):
                    result, output, request = self.run_version(branch, app=app)
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertEqual(output, f'version={version}\n')
                    self.assertEqual(request, f'https://storage.barkfluff.com/get/{app}/{source}/version')

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
class WinUIVersionTests(unittest.TestCase):
    def run_variant(self, channel, body='{"version":"1.2.9"}', exit_code=0):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            client = directory / 'Windows/BarkFluff.Client.WinUI'
            client.mkdir(parents=True)
            for filename in ['Package.appxmanifest', 'app.manifest']:
                shutil.copyfile(ROOT / 'Windows/BarkFluff.Client.WinUI' / filename, client / filename)
            original = (client / 'Package.appxmanifest').read_text()
            output = directory / 'output'
            output.touch()
            requests = directory / 'requests'
            script = directory / 'test.ps1'
            script.write_text('''$ErrorActionPreference = 'Stop'
function curl.exe {
    $url = $args[-1]
    Add-Content -LiteralPath $env:REQUESTS -Value $url
    $global:LASTEXITCODE = [int]$env:CURL_EXIT
    Write-Output $env:BODY
}
''' + workflow_script('Подготовка варианта MSIX и версии'))
            branch = 'master' if channel == 'release' else channel
            suffix = '' if channel == 'release' else '.' + channel
            env = dict(os.environ, GITHUB_WORKSPACE=str(directory), RUNNER_TEMP=str(directory),
                       GITHUB_RUN_ID='offline', GITHUB_OUTPUT=str(output), CHANNEL=channel,
                       BRANCH_NAME=branch, ASSET_DIRECTORY=branch,
                       IDENTITY_NAME='7895OrbitinSpace.Barkfluff' + suffix.capitalize(),
                       DISPLAY_NAME='Barkfluff' + suffix, SHORT_NAME='barkfluff' + suffix,
                       BODY=body, CURL_EXIT=str(exit_code), REQUESTS=str(requests))
            result = subprocess.run([PWSH, '-NoLogo', '-NoProfile', '-File', str(script)],
                                    env=env, text=True, capture_output=True)
            return result, output.read_text(encoding='utf-8-sig'), requests.read_text(), original == (client / 'Package.appxmanifest').read_text()

    def test_branch_policy_and_manifest_versions(self):
        for channel, source, version in [('nightly', 'nightly', '1.2.10'),
                                         ('dev', 'nightly', '1.2.9'), ('release', 'dev', '1.2.9')]:
            result, output, request, unchanged = self.run_variant(channel)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn(f'version={version}\n', output)
            self.assertIn(f'manifest_version={version}.0\n', output)
            self.assertEqual(request.strip(), f'https://storage.barkfluff.com/get/barkfluffwinui/{source}/version')
            self.assertFalse(unchanged)

    def test_failure_keeps_original_manifest_and_no_version_output(self):
        for channel in ['nightly', 'dev', 'release']:
            for body, code in [('', 22), ('{"version":"1.2.9"}', 28), ('{', 0), ('{}', 0),
                               ('{"version":"1.2.3 beta"}', 0), ('{"version":"65536.1.1"}', 0)]:
                with self.subTest(channel=channel, body=body, code=code):
                    result, output, request, unchanged = self.run_variant(channel, body, code)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertEqual(output, '')
                    self.assertEqual(len(request.splitlines()), 1)
                    self.assertTrue(unchanged)

    def test_nightly_patch_overflow_fails(self):
        result, output, _, unchanged = self.run_variant('nightly', '{"version":"1.2.65535"}')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(output, '')
        self.assertTrue(unchanged)


if __name__ == '__main__':
    unittest.main()
