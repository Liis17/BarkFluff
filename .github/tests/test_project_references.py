"""Catch project-reference casing errors before building on Linux."""
import posixpath
from pathlib import Path
import subprocess
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


class ProjectReferenceTests(unittest.TestCase):
    def test_test_projects_reference_tracked_paths_with_exact_case(self):
        tracked = set(subprocess.check_output(
            ['git', 'ls-files'], cwd=ROOT, text=True).splitlines())
        projects = sorted(path for path in tracked
                          if path.startswith('Tests/') and path.endswith('.csproj'))
        self.assertTrue(projects, 'No test projects found')
        for project in projects:
            for reference in ET.parse(ROOT / project).iter('ProjectReference'):
                target = posixpath.normpath(posixpath.join(
                    posixpath.dirname(project), reference.attrib['Include'].replace('\\', '/')))
                with self.subTest(project=project, target=target):
                    self.assertTrue(target in tracked, f'Project reference casing mismatch: {target}')


if __name__ == '__main__':
    unittest.main()
