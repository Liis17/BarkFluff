import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "actions/docker-version/compute-version.sh"
IMAGE = "barkfluff-messages"


class DockerVersionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.output = self.directory / "output"
        self.requests = self.directory / "requests"
        self.output.touch()
        curl = self.directory / "curl"
        curl.write_text("""#!/usr/bin/env python3
import json, os, pathlib, sys
args = sys.argv[1:]
url = args[-1]
with open(os.environ['MOCK_REQUESTS'], 'a') as log:
    log.write(url + '\\n')
response = json.loads(os.environ['MOCK_RESPONSES'])[url]
if response.get('exit', 0):
    sys.exit(response['exit'])
body = response['body']
if '-o' in args:
    pathlib.Path(args[args.index('-o') + 1]).write_text(body)
else:
    sys.stdout.write(body)
if '-w' in args:
    sys.stdout.write(str(response['status']))
if '-D' in args:
    pathlib.Path(args[args.index('-D') + 1]).write_text(
        'HTTP/1.1 ' + str(response['status']) + '\\r\\n'
        'Docker-Content-Digest: ' + response.get('digest', '') + '\\r\\n'
        + ('Link: <' + response['link'] + '>; rel="next"\\r\\n' if response.get('link') else '')
    )
""")
        curl.chmod(0o755)
        self.responses = {}
        for repository, tags in {
            f"{IMAGE}-nightly": ["1.0.9", "latest", "1.0.12"],
            f"{IMAGE}-dev": ["7.0.0", "latest"],
            IMAGE: ["99.0.0", "latest"],
        }.items():
            self.respond(repository, {"name": repository, "tags": tags})
            numeric_tags = [tag for tag in tags if tag != "latest"]
            for tag in numeric_tags:
                digest = "sha256:" + hashlib.sha256(f"{repository}:{tag}".encode()).hexdigest()
                self.respond_manifest(repository, tag, digest)
            self.respond_manifest(repository, "latest", digest)

    def respond(self, repository, body, status=200, exit_code=0):
        url = f"https://docker.barkfluff.com/v2/{repository}/tags/list"
        self.responses[url] = {
            "body": body if isinstance(body, str) else json.dumps(body),
            "status": status,
            "exit": exit_code,
        }

    def respond_manifest(self, repository, tag, digest, status=200, exit_code=0):
        url = f"https://docker.barkfluff.com/v2/{repository}/manifests/{tag}"
        self.responses[url] = {
            "body": "", "status": status, "exit": exit_code, "digest": digest,
        }

    def run_version(self, branch, ref_type="branch"):
        self.output.write_text("")
        self.requests.write_text("")
        env = dict(
            os.environ,
            PATH=f"{self.directory}{os.pathsep}{os.environ['PATH']}",
            IMAGE=IMAGE,
            REG_USER="test",
            REG_PASS="test",
            GITHUB_REF_NAME=branch,
            GITHUB_REF_TYPE=ref_type,
            GITHUB_OUTPUT=str(self.output),
            MOCK_REQUESTS=str(self.requests),
            MOCK_RESPONSES=json.dumps(self.responses),
        )
        result = subprocess.run(
            ["bash", str(SCRIPT)], env=env, capture_output=True, text=True
        )
        outputs = dict(line.split("=", 1) for line in self.output.read_text().splitlines())
        return result, outputs

    def assert_version(self, branch, source_repository, target_repository, version):
        result, outputs = self.run_version(branch)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(outputs, {
            "version": version,
            "tags": f"docker.barkfluff.com/{target_repository}:{version},docker.barkfluff.com/{target_repository}:latest",
            "tag_version": f"docker.barkfluff.com/{target_repository}:{version}",
        })
        requests = self.requests.read_text().splitlines()
        prefix = f"https://docker.barkfluff.com/v2/{source_repository}/"
        self.assertEqual(requests[0], prefix + "tags/list")
        self.assertTrue(all(url.startswith(prefix) for url in requests))
        if branch == "nightly":
            self.assertTrue(all("/tags/list" in url for url in requests))
        else:
            self.assertIn(prefix + "manifests/latest", requests)
            self.assertIn(prefix + f"manifests/{version}", requests)

    def assert_rejected(self, branch, ref_type="branch"):
        result, outputs = self.run_version(branch, ref_type)
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual(outputs, {})

    def test_nightly_increments_max_numeric_version(self):
        self.assert_version("nightly", f"{IMAGE}-nightly", f"{IMAGE}-nightly", "1.0.13")

    def test_dev_inherits_nightly_not_its_own_version(self):
        self.assert_version("dev", f"{IMAGE}-nightly", f"{IMAGE}-dev", "1.0.12")

    def test_master_inherits_dev_not_nightly_or_release(self):
        self.assert_version("master", f"{IMAGE}-dev", IMAGE, "7.0.0")

    def test_repeated_promotion_does_not_increment(self):
        for _ in range(2):
            self.assert_version("dev", f"{IMAGE}-nightly", f"{IMAGE}-dev", "1.0.12")

    def test_master_uses_current_dev_even_with_higher_legacy_tag(self):
        repository = f"{IMAGE}-dev"
        self.respond(repository, {"name": repository, "tags": ["7.0.0", "1.0.12", "latest"]})
        digest = "sha256:" + "a" * 64
        self.respond_manifest(repository, "1.0.12", digest)
        self.respond_manifest(repository, "latest", digest)
        self.assert_version("master", repository, IMAGE, "1.0.12")

    def test_promotion_requires_version_tag_matching_latest(self):
        self.respond_manifest(f"{IMAGE}-nightly", "latest", "sha256:" + "b" * 64)
        self.assert_rejected("dev")

    def test_manifest_errors_stop_promotion(self):
        for tag in ["latest", "1.0.12"]:
            for status in [404, 401, 500]:
                with self.subTest(tag=tag, status=status):
                    original = self.responses.copy()
                    self.respond_manifest(f"{IMAGE}-nightly", tag, "", status)
                    self.assert_rejected("dev")
                    self.responses = original

    def test_missing_digest_stops_promotion(self):
        self.respond_manifest(f"{IMAGE}-nightly", "latest", "")
        self.assert_rejected("dev")

    def paginate_nightly(self, status=200):
        repository = f"{IMAGE}-nightly"
        self.respond(repository, {"name": repository, "tags": ["1.0.9"]})
        url = f"https://docker.barkfluff.com/v2/{repository}/tags/list"
        next_path = f"/v2/{repository}/tags/list?n=1&last=1.0.9"
        self.responses[url]["link"] = next_path
        next_url = "https://docker.barkfluff.com" + next_path
        self.responses[next_url] = {
            "status": status,
            "body": json.dumps({"name": repository, "tags": ["1.0.12"]}),
        }
        return next_url

    def test_nightly_reads_maximum_across_all_pages(self):
        self.paginate_nightly()
        self.assert_version("nightly", f"{IMAGE}-nightly", f"{IMAGE}-nightly", "1.0.13")

    def test_promotion_finds_latest_version_on_later_page(self):
        self.paginate_nightly()
        self.assert_version("dev", f"{IMAGE}-nightly", f"{IMAGE}-dev", "1.0.12")

    def test_later_page_error_cannot_use_partial_tag_list(self):
        for status in [401, 404, 500]:
            with self.subTest(status=status):
                self.paginate_nightly(status)
                self.assert_rejected("nightly")

    def test_pagination_cannot_send_credentials_to_another_host(self):
        url = f"https://docker.barkfluff.com/v2/{IMAGE}-nightly/tags/list"
        self.responses[url]["link"] = "https://example.invalid/tags/list?n=1"
        self.assert_rejected("nightly")
        self.assertEqual(self.requests.read_text().splitlines(), [url])

    def test_pagination_cycle_is_rejected(self):
        next_url = self.paginate_nightly()
        self.responses[next_url]["link"] = next_url
        self.assert_rejected("nightly")

    def test_new_nightly_repository_starts_at_one(self):
        self.respond(f"{IMAGE}-nightly", {"errors": [{"code": "NAME_UNKNOWN"}]}, 404)
        self.assert_version("nightly", f"{IMAGE}-nightly", f"{IMAGE}-nightly", "1.0.0")

    def test_empty_nightly_tags_start_at_one(self):
        for tags in [None, [], ["latest"]]:
            with self.subTest(tags=tags):
                self.respond(f"{IMAGE}-nightly", {"name": f"{IMAGE}-nightly", "tags": tags})
                self.assert_version("nightly", f"{IMAGE}-nightly", f"{IMAGE}-nightly", "1.0.0")

    def test_promotion_requires_source_repository(self):
        for branch, repository in [("dev", f"{IMAGE}-nightly"), ("master", f"{IMAGE}-dev")]:
            with self.subTest(branch=branch):
                self.respond(repository, {"errors": [{"code": "NAME_UNKNOWN"}]}, 404)
                self.assert_rejected(branch)

    def test_promotion_requires_numeric_source_version(self):
        for branch, repository in [("dev", f"{IMAGE}-nightly"), ("master", f"{IMAGE}-dev")]:
            with self.subTest(branch=branch):
                self.respond(repository, {"name": repository, "tags": ["latest", "invalid"]})
                self.assert_rejected(branch)

    def test_http_errors_cannot_reset_version(self):
        for branch, repository in [("nightly", f"{IMAGE}-nightly"), ("dev", f"{IMAGE}-nightly"), ("master", f"{IMAGE}-dev")]:
            for status in [401, 403, 429, 500]:
                with self.subTest(branch=branch, status=status):
                    self.respond(repository, {"errors": [{"code": "UNAUTHORIZED"}]}, status)
                    self.assert_rejected(branch)

    def test_unexpected_404_cannot_bootstrap_nightly(self):
        self.respond(f"{IMAGE}-nightly", "<html>not found</html>", 404)
        self.assert_rejected("nightly")

    def test_malformed_success_response_is_rejected(self):
        for body in ["invalid JSON", {}, {"errors": []}, {"name": "wrong", "tags": []}, {"name": f"{IMAGE}-nightly", "tags": [42]}]:
            with self.subTest(body=body):
                self.respond(f"{IMAGE}-nightly", body)
                self.assert_rejected("nightly")

    def test_network_error_cannot_reset_version(self):
        self.respond(f"{IMAGE}-nightly", "", exit_code=28)
        self.assert_rejected("nightly")

    def test_unknown_branch_is_rejected_before_registry_request(self):
        self.assert_rejected("feature/test")
        self.assertEqual(self.requests.read_text(), "")

    def test_tag_named_master_is_not_a_release_branch(self):
        self.assert_rejected("master", "tag")
        self.assertEqual(self.requests.read_text(), "")


if __name__ == "__main__":
    unittest.main()
