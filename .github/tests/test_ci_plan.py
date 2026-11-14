"""Push build selection, promotion guard and notification formatting."""
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / '.github/scripts'))
import ci_notify  # noqa: E402
from ci_plan import SERVICES, plan  # noqa: E402
import promote_backend  # noqa: E402


def built(branch, paths):
    return sorted(s['name'] for s in plan(branch, paths)['backend'])


class CiPlanTests(unittest.TestCase):
    def test_catalog_matches_repository(self):
        for service in SERVICES:
            with self.subTest(service=service['name']):
                self.assertTrue((ROOT / service['project']).is_file())
                self.assertTrue((ROOT / service['dockerfile']).is_file())
                if service['node_dir']:
                    self.assertTrue((ROOT / service['node_dir'] / 'package-lock.json').is_file())
        self.assertEqual(len({s['image'] for s in SERVICES}), len(SERVICES))

    def test_service_change_builds_only_that_service(self):
        self.assertEqual(built('nightly', ['Backend/BarkFluff.Messages/Host/Api.cs']), ['messages'])
        self.assertEqual(built('nightly', ['Backend/BarkFluff.Identity/Program.cs']), ['fast-auth', 'identity'])
        self.assertEqual(built('nightly', ['Frontend/Developers/src/App.tsx']), ['developers'])

    def test_shared_inputs_rebuild_only_images_that_build_from_them(self):
        everything = sorted(s['name'] for s in SERVICES)
        without_bft = [name for name in everything if name != 'bftproxy']
        self.assertEqual(built('nightly', ['Backend/BarkFluff.GrpcServer/Interceptor.cs']), without_bft)
        self.assertEqual(built('nightly', ['Docker/Lognara/agent/SHA256SUMS']), without_bft)
        self.assertEqual(built('nightly', ['Shared/BarkFluff.Bft/Frame.cs']), ['bftproxy'])
        self.assertEqual(built('nightly', ['Shared/BarkFluff.Proto/calls_api.proto']),
                         ['calls', 'developers', 'files', 'web'])
        queue = built('nightly', ['Shared/BarkFluff.Shared.Queue/Event.cs'])
        self.assertIn('messages', queue)
        self.assertNotIn('beacon', queue)
        self.assertEqual(built('nightly', None), everything)

    def test_tests_and_docs_do_not_publish_images(self):
        for path in ['Tests/BarkFluff.Messages.Tests/Test.cs', 'Tests/BarkFluff.GrpcServer.Tests/Test.cs',
                     '.github/tests/test_ci_plan.py', '.github/workflows/test-backend.yml',
                     '.github/workflows/ci.yml', '.github/scripts/ci_notify.py',
                     'Obsidian/ClaudeVault/Index.md', 'README.md']:
            with self.subTest(path=path):
                result = plan('nightly', [path])
                self.assertEqual(result['backend'], [])
                self.assertFalse(any(result[client] for client in ('android', 'macos', 'winui', 'builder')))

    def test_clients_follow_their_sources(self):
        cases = {
            'Android/core/src/Example.kt': {'android'},
            'Backend/Barkfluff.WebServer/html/legal/terms.md': {'android'},
            'Mac/Barkfluff/App.swift': {'macos'},
            'Windows/BarkFluff.Client.Core/Client.cs': {'winui'},
            'Shared/BarkFluff.Proto/users_api.proto': {'winui'},
            'Shared/BarkFluff.Shared.Queue/Event.cs': set(),
            'Shared/BarkFluff.Bft/Frame.cs': set(),
            'Tools/BarkFluff.Builder/Model.cs': {'builder'},
            'Directory.Build.props': {'builder'},
            'Tools/BarkFluff.Builder/Directory.Build.targets': {'builder'},
            '.github/scripts/client-version.sh': {'android', 'macos', 'winui'},
        }
        for path, expected in cases.items():
            with self.subTest(path=path):
                result = plan('nightly', [path])
                self.assertEqual({c for c in ('android', 'macos', 'winui', 'builder') if result[c]}, expected)

    def test_promotion_branches_copy_images_instead_of_building(self):
        for branch in ['dev', 'master']:
            result = plan(branch, ['Backend/BarkFluff.Messages/Host/Api.cs', 'Android/core/A.kt'])
            self.assertEqual(result['backend'], [])
            self.assertTrue(result['promote'])
            self.assertTrue(result['android'])
        self.assertFalse(plan('nightly', None)['promote'])


class PromotionGuardTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git('init', '-b', 'nightly')
        self.commit('Backend/BarkFluff.Messages/A.cs', 'v1')
        self.built = self.head()
        self.old_root = promote_backend.ROOT
        promote_backend.ROOT = self.repo
        self.addCleanup(setattr, promote_backend, 'ROOT', self.old_root)
        self.sources = next(s for s in SERVICES if s['name'] == 'messages')['sources']

    def git(self, *args):
        return subprocess.check_output(['git', '-c', 'user.name=T', '-c', 'user.email=t@example.invalid',
                                        '-c', 'commit.gpgsign=false', *args], cwd=self.repo, text=True)

    def commit(self, path, text):
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text)
        self.git('add', path)
        self.git('commit', '-m', text)

    def head(self):
        return self.git('rev-parse', 'HEAD').strip()

    def test_image_is_valid_while_its_sources_are_unchanged(self):
        self.commit('Backend/BarkFluff.Users/B.cs', 'other service')
        self.commit('Tests/BarkFluff.Messages.Tests/T.cs', 'tests only')
        self.commit('Shared/BarkFluff.Bft/F.cs', 'unrelated shared library')
        promote_backend.check_revision(self.built, self.sources)

    def test_newer_sources_reject_stale_image(self):
        for path in ['Backend/BarkFluff.Messages/A.cs', 'Shared/BarkFluff.Proto/messages_api.proto',
                     'Backend/BarkFluff.GrpcServer/G.cs', 'global.json']:
            with self.subTest(path=path):
                self.commit(path, 'changed ' + path)
                with self.assertRaisesRegex(promote_backend.PromotionError, 'исходники изменились'):
                    promote_backend.check_revision(self.built, self.sources)

    def test_image_from_commit_outside_branch_or_unlabelled_is_rejected(self):
        self.git('checkout', '-q', '-b', 'side')
        self.commit('Backend/BarkFluff.Messages/A.cs', 'side')
        side = self.head()
        self.git('checkout', '-q', 'nightly')
        with self.assertRaisesRegex(promote_backend.PromotionError, 'нет в этой ветке'):
            promote_backend.check_revision(side, self.sources)
        with self.assertRaisesRegex(promote_backend.PromotionError, 'нет метки'):
            promote_backend.check_revision('', self.sources)
        with self.assertRaisesRegex(promote_backend.PromotionError, 'не найден'):
            promote_backend.check_revision('0' * 40, self.sources)


class NotifyTests(unittest.TestCase):
    ENV = {'GITHUB_REF_NAME': 'nightly', 'GITHUB_SHA': 'a' * 40, 'GITHUB_ACTOR': 'some_user'}

    def test_push_summary_lists_components_and_failures(self):
        results = [('success', '🐳 messages', '1.0.5'), ('failure', '📱 Android (nightly)', '')]
        message = ci_notify.push_message(self.ENV, results, 'success')
        self.assertIn('❌ *Сборка с ошибками*', message)
        self.assertIn('✅ 🐳 messages `1.0.5`', message)
        self.assertIn('❌ 📱 Android (nightly)', message)
        self.assertIn('`some_user`', message)

    def test_jobs_that_died_without_result_file_are_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, 'backend-messages.txt').write_text('failure|🐳 messages|\n', encoding='utf-8')
            Path(directory, 'android.txt').write_text('success|📱 Android|1.2.3\n', encoding='utf-8')
            results, failed_jobs = ci_notify.read_results(directory)
        needs = {'backend': {'result': 'failure'}, 'promote': {'result': 'skipped'},
                 'android': {'result': 'success'}, 'winui': {'result': 'cancelled'}}
        extra = ci_notify.missing_failures(needs, failed_jobs)
        self.assertEqual(failed_jobs, {'backend'})
        self.assertEqual(extra, [('cancelled', '🪟 WinUI', '')])

    def test_pr_message_escapes_untrusted_title_and_branches(self):
        env = dict(self.ENV, REQUIRED='failure', PR_NUMBER='7', PR_TITLE='fix *bold* _x_ `code` [l](u)',
                   HEAD_REF='feat/`x`', BASE_REF='nightly', GUARD='success', BACKEND='failure')
        message = ci_notify.pr_message(env)
        title_line = next(line for line in message.splitlines() if line.startswith('📝'))
        self.assertIsNone(re.search(r'[*_`\[\]]', title_line))
        self.assertIn("`feat/'x'` → `nightly`", message)
        self.assertIn('❌ Backend тесты', message)
        self.assertIn('✅ Ветка-источник', message)


if __name__ == '__main__':
    unittest.main()
