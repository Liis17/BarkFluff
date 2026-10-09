"""Offline notification checks, including macOS Bash 3.2 and Windows curl paths."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'scripts/send-telegram.sh'


class TelegramTests(unittest.TestCase):
    def send(self, response='{"ok":true}', exit_code=0, download='', windows=False, script=SCRIPT):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            capture = directory / 'capture'
            curl = directory / 'curl'
            curl.write_text('''#!/usr/bin/env python3
import json, os, pathlib, sys
args = sys.argv[1:]
forms = {}
for index, arg in enumerate(args):
    if arg == '--data-urlencode':
        form = args[index + 1]
        if '@' in form and '=' not in form:
            key, path = form.split('@', 1)
            forms[key] = pathlib.Path(path).read_text()
        else:
            key, value = form.split('=', 1)
            forms[key] = value
pathlib.Path(os.environ['CAPTURE']).write_text(json.dumps({'args': args, 'forms': forms}))
print(os.environ['RESPONSE'])
sys.exit(int(os.environ['CURL_EXIT']))
''')
            curl.chmod(0o755)
            if windows:
                cygpath = directory / 'cygpath'
                cygpath.write_text('#!/bin/sh\nprintf "%s" "$2"\n')
                cygpath.chmod(0o755)
            env = dict(os.environ, PATH=f'{directory}{os.pathsep}{os.environ["PATH"]}',
                       TG_TOKEN='offline-token', TG_CHAT='-100123',
                       TG_MESSAGE='✅ Сборка успешна\n\nWinUI 1.2.3',
                       TG_ACTION_URL='https://github.example/actions/123', TG_DOWNLOAD_URL=download,
                       RESPONSE=response, CURL_EXIT=str(exit_code), CAPTURE=str(capture))
            result = subprocess.run(['/bin/bash', str(script)], env=env, text=True, capture_output=True)
            return result, json.loads(capture.read_text()) if capture.exists() else None

    def test_success_message_and_action_button_on_bash_32(self):
        result, capture = self.send()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(capture['forms']['chat_id'], '-100123')
        self.assertEqual(capture['forms']['text'], '✅ Сборка успешна\n\nWinUI 1.2.3')
        button = json.loads(capture['forms']['reply_markup'])['inline_keyboard'][0][0]
        self.assertEqual(button['url'], 'https://github.example/actions/123')
        self.assertNotIn('--ssl-revoke-best-effort', capture['args'])
        self.assertIn('--fail', capture['args'])

    def test_download_button_and_json_escaping(self):
        url = 'https://storage.example/app?test="quoted"'
        result, capture = self.send(download=url)
        self.assertEqual(result.returncode, 0, result.stderr)
        button = json.loads(capture['forms']['reply_markup'])['inline_keyboard'][0][0]
        self.assertEqual(button['text'], 'Скачать последнюю версию')
        self.assertEqual(button['url'], url)

    def test_windows_schannel_branch(self):
        result, capture = self.send(windows=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('--ssl-revoke-best-effort', capture['args'])
        self.assertEqual(capture['forms']['text'], '✅ Сборка успешна\n\nWinUI 1.2.3')

    def test_http_and_network_errors_preserve_exit_status(self):
        for code in [22, 28, 60]:
            with self.subTest(code=code):
                result, _ = self.send(exit_code=code)
                self.assertEqual(result.returncode, code, result.stderr)

    def test_rejected_or_malformed_response_is_an_error(self):
        for response in ['{"ok":false,"description":"Bad Request"}', '{"ok":"true"}',
                         '{}', 'null', '[]', '', '{']:
            with self.subTest(response=response):
                result, _ = self.send(response=response)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('Уведомление не отправлено', result.stderr)


if __name__ == '__main__':
    unittest.main()
