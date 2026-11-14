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
from pr_tests import JOBS, SUITES, results_pass, select, select_suites


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
            ('Windows/BarkFluff.Client.Core/Client.cs', 'windows'),
            ('Tests/BarkFluff.ClientV2.WPF.Tests/Example.cs', 'windows'),
            ('Mac/Barkfluff/Packages/BFCore/Sources/Example.swift', 'macos'),
        ]:
            with self.subTest(path=path):
                self.assert_suites([path], [suite])

    def test_builder_tool_changes_select_backend_and_builder_suites(self):
        self.assert_suites(['Tools/BarkFluff.Builder/BuilderModel.cs'], ['backend', 'builder'])
        self.assert_suites(['Tests/BarkFluff.Builder.Tests/BuilderTests.cs'], ['backend', 'builder'])

    def test_frontend_sources_select_frontend_checks(self):
        self.assert_suites(['Frontend/Developers/src/App.tsx'], ['backend', 'frontend'])
        self.assert_suites(['Backend/BarkFluff.Web/scripts/app.js'], ['backend', 'frontend', 'scripts'])
        self.assert_suites(['Backend/BarkFluff.Navigator/Admin/src/main.tsx'], ['backend', 'frontend'])

    def test_mixed_changes_combine_platforms(self):
        self.assert_suites(['Android/core/Example.kt', 'Mac/Barkfluff/Example.swift'], ['android', 'macos'])

    def test_selector_changes_run_every_suite(self):
        for path in ['.github/workflows/ci.yml', '.github/scripts/ci_plan.py', '.github/scripts/pr_tests.py']:
            with self.subTest(path=path):
                self.assertEqual(select([path]), (dict.fromkeys(SUITES, True), None))

    def test_shared_code_selects_only_its_consumers(self):
        # Android and macOS keep their own .proto copies; only Windows clients build from Shared.
        self.assert_suites(['Shared/BarkFluff.Proto/messages_api.proto'], ['backend', 'windows', 'frontend'])
        self.assert_suites(['Shared/BarkFluff.Shared.Auth/Example.cs'], ['backend', 'windows'])
        self.assert_suites(['Shared/BarkFluff.Shared.Queue/Example.cs'], ['backend'])
        self.assert_suites(['Shared/BarkFluff.Bft/Example.cs'], ['backend'])

    def test_backend_runs_only_test_projects_that_build_from_the_change(self):
        common = 'BarkFluff.GrpcServer.Tests'
        for paths, expected in [
            (['Backend/Barkfluff.AdminPanel/Services/Example.cs'], {'BarkFluff.AdminPanel.Tests'}),
            (['Backend/BarkFluff.Calls/Services/Example.cs', 'Tests/BarkFluff.Calls.Tests/Example.cs'],
             {'BarkFluff.Calls.Tests'}),
            (['Shared/BarkFluff.Bft/Example.cs', 'Docs/bft/bftv1-vectors.json'],
             {'BarkFluff.Bft.Tests', 'BarkFluff.BftProxy.Tests'}),
            (['Tools/BarkFluff.Builder/BuilderModel.cs'], {'BarkFluff.Builder.Tests'}),
            # Services without their own tests are covered by the common GrpcServer tests only.
            (['Backend/BarkFluff.Beacon/Program.cs'], set()),
        ]:
            with self.subTest(paths=paths):
                self.assertEqual(set(select(paths)[1]), expected | {common})
        self.assertGreater(len(select(['Shared/BarkFluff.Proto/messages_api.proto'])[1]), 5)

    def test_unmapped_backend_inputs_run_every_backend_test(self):
        for path in ['Docker/nightly/barkfluff/docker-compose.yml', 'Backend/rebuild.trigger', 'global.json',
                     'Tests/LoadTests/run-load-test.ps1']:
            with self.subTest(path=path):
                suites, projects = select([path])
                self.assertTrue(suites['backend'])
                self.assertIsNone(projects)

    def test_server_and_android_change_skips_other_clients(self):
        self.assert_suites(['Android/core/src/main/java/com/barkfluff/client/bft/Proxy.kt',
                            'Backend/BarkFluff.BftProxy/Program.cs', 'Shared/BarkFluff.Bft/Frame.cs',
                            'Tests/BarkFluff.BftProxy.Tests/ProxyTests.cs', 'Obsidian/ClaudeVault/Index.md'],
                           ['backend', 'android'])

    def test_docs_do_not_start_application_tests(self):
        self.assert_suites(['README.md', 'Obsidian/ClaudeVault/Testing.md'], [])

    def test_bft_spec_and_vectors_select_backend_and_android_tests(self):
        # Android unit tests read Docs/bft vectors too.
        for path in ['Docs/BFTv1.md', 'Docs/bft/bftv1-vectors.json', 'Docs/bft/generate_vectors.py']:
            with self.subTest(path=path):
                self.assert_suites([path], ['backend', 'android'])

    def test_dotnet_sdk_change_runs_backend_and_windows(self):
        self.assert_suites(['global.json'], ['backend', 'windows', 'builder'])

    def test_ci_helper_change_checks_scripts(self):
        self.assert_suites(['.github/scripts/client-version.sh'], ['scripts'])

    def test_client_test_workflow_runs_all_clients_and_scripts(self):
        self.assert_suites(['.github/workflows/test-clients.yml'], ['windows', 'android', 'macos', 'scripts'])

    def test_gate_rejects_failed_cancelled_and_unexpectedly_skipped_jobs(self):
        states = ('success', 'failure', 'cancelled', 'skipped')
        for selected_values in itertools.product((False, True), repeat=len(JOBS)):
            selected = dict(zip(JOBS, selected_values))
            expected = {job: 'success' if selected[job] else 'skipped' for job in JOBS}
            for values in itertools.product(states, repeat=len(JOBS)):
                results = dict(zip(JOBS, values))
                for selection_result in ('success', 'failure', 'cancelled'):
                    self.assertEqual(results_pass(selection_result, selected, results),
                                     selection_result == 'success' and results == expected,
                                     (selection_result, selected, results))

    def test_promotion_pr_needs_only_guard_and_no_tests(self):
        skipped = dict.fromkeys(JOBS, 'skipped')
        self.assertTrue(results_pass('skipped', {}, skipped, 'success'))
        for guard in ('failure', 'cancelled', 'skipped'):
            self.assertFalse(results_pass('skipped', {}, skipped, guard))
            self.assertFalse(results_pass('success', dict.fromkeys(JOBS, False), skipped, guard))
        for job in JOBS:
            self.assertFalse(results_pass('skipped', {}, dict(skipped, **{job: 'success'}), 'success'))

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
            self.assertEqual(json.loads(values['selected']), {'backend': False, 'clients': True, 'scripts': False,
                                                         'frontend': False, 'builder': False})


if __name__ == '__main__':
    unittest.main()
