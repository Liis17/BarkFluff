"""Regression tests for PR scope selection and the required test gate."""
import itertools
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / '.github/scripts'))
from pr_tests import SUITES, results_pass, select_suites


class PrTestSelectionTests(unittest.TestCase):
    def assert_suites(self, paths, expected):
        selected = select_suites(paths)
        self.assertEqual({name for name, enabled in selected.items() if enabled}, set(expected))

    def test_android_icon_runs_only_android(self):
        self.assert_suites([
            'Android/Barkfluff.Client.Android/app/src/main/res/drawable/ic_verified.xml'
        ], ['android'])

    def test_platform_sources_select_their_own_suite(self):
        for path, suite in [
            ('Backend/BarkFluff.Messages/Host/MessagesApiService.cs', 'backend'),
            ('Frontend/Developers/src/App.tsx', 'backend'),
            ('Windows/BarkFluff.Client.Core/Client.cs', 'windows'),
            ('Tests/BarkFluff.ClientV2.WPF.Tests/Example.cs', 'windows'),
            ('Mac/Barkfluff/Packages/BFCore/Sources/Example.swift', 'macos'),
        ]:
            with self.subTest(path=path):
                self.assert_suites([path], [suite])

    def test_builder_tool_changes_select_backend_suite(self):
        self.assert_suites(['Tools/BarkFluff.Builder/BuilderModel.cs'], ['backend'])

    def test_mixed_changes_combine_platforms(self):
        self.assert_suites(['Android/core/Example.kt', 'Mac/Barkfluff/Example.swift'], ['android', 'macos'])

    def test_shared_contracts_and_selector_changes_run_every_suite(self):
        for path in ['Shared/BarkFluff.Proto/messages.proto', '.github/workflows/pr-tests.yml',
                     '.github/scripts/pr_tests.py']:
            with self.subTest(path=path):
                self.assert_suites([path], SUITES)

    def test_docs_do_not_start_application_tests(self):
        self.assert_suites(['README.md', 'Obsidian/ClaudeVault/Testing.md'], [])

    def test_dotnet_sdk_change_runs_backend_and_windows(self):
        self.assert_suites(['global.json'], ['backend', 'windows'])

    def test_client_workflow_change_also_checks_scripts(self):
        self.assert_suites(['.github/workflows/build-client-android.yml'], ['android', 'scripts'])

    def test_client_test_workflow_runs_all_clients_and_scripts(self):
        self.assert_suites(['.github/workflows/test-clients.yml'], ['windows', 'android', 'macos', 'scripts'])

    def test_gate_rejects_failed_cancelled_and_unexpectedly_skipped_jobs(self):
        jobs = ('backend', 'clients', 'scripts')
        states = ('success', 'failure', 'cancelled', 'skipped')
        for selected_values in itertools.product((False, True), repeat=3):
            selected = dict(zip(jobs, selected_values))
            expected = {job: 'success' if selected[job] else 'skipped' for job in jobs}
            for values in itertools.product(states, repeat=3):
                results = dict(zip(jobs, values))
                for selection_result in states:
                    self.assertEqual(results_pass(selection_result, selected, results),
                                     selection_result == 'success' and results == expected,
                                     (selection_result, selected, results))

    def test_shallow_merge_diff_excludes_unrelated_target_branch_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            repo = directory / 'source'
            repo.mkdir()

            def git(*args):
                return subprocess.check_output([
                    'git', '-c', 'user.name=Tests', '-c', 'user.email=tests@example.invalid',
                    '-c', 'commit.gpgsign=false', *args
                ], cwd=repo, stderr=subprocess.STDOUT)

            def commit_file(path, message):
                target = repo / path
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(message)
                git('add', path)
                git('commit', '-m', message)

            git('init', '-b', 'target')
            commit_file('README.md', 'base')
            git('checkout', '-b', 'android-change')
            commit_file('Android/core/Example.kt', 'android')
            git('checkout', 'target')
            commit_file('Backend/BarkFluff.Messages/Example.cs', 'unrelated target change')
            git('merge', '--no-ff', '-m', 'synthetic PR merge', 'android-change')
            checkout = directory / 'checkout'
            git('clone', '--depth', '2', repo.as_uri(), str(checkout))
            output = directory / 'output'
            subprocess.run([sys.executable, str(ROOT / '.github/scripts/pr_tests.py')],
                           cwd=checkout, check=True, capture_output=True,
                           env=dict(os.environ, GITHUB_EVENT_NAME='pull_request', GITHUB_OUTPUT=str(output)))
            values = dict(line.split('=', 1) for line in output.read_text().splitlines())
            self.assertEqual(json.loads(values['platforms']), ['android'])
            self.assertEqual(json.loads(values['selected']), {'backend': False, 'clients': True, 'scripts': False})


if __name__ == '__main__':
    unittest.main()
