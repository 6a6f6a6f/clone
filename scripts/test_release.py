import copy
import json
from pathlib import Path
import tempfile
import unittest

import release
from check_release_gate import check


class ReleaseTests(unittest.TestCase):
    def test_rejects_untrusted_versions(self):
        for value in ['v1.0.0', '../1.0', '1.0.0;touch x', '01.2.3', '1.0.0\n', '1.0', '1.0.0-beta']:
            with self.subTest(value=value), self.assertRaises(ValueError):
                release.version(value)

    def fixture(self, root, rid='osx-arm64', development=False):
        directory = root / rid
        directory.mkdir()
        suffix = '-development' if development else ''
        names = [f'clone-0.2.0-{rid}{suffix}{extension}' for extension in ['.tar.gz', '.pkg']]
        for name in names:
            (directory / name).write_bytes(b'controlled fixture')
        data = {'version': '0.2.0', 'rid': rid, 'commit': 'a' * 40, 'development': development,
                'native_smoke': True, 'notarized': not development,
                'files': {name: release.digest(directory / name) for name in names}}
        path = directory / 'manifest.json'
        path.write_text(json.dumps(data))
        return path, data

    def test_rejects_tampered_and_symlinked_artifacts(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path, data = self.fixture(root)
            release.validate_manifest(path)
            asset = path.parent / next(iter(data['files']))
            asset.write_bytes(b'tampered')
            with self.assertRaises(ValueError):
                release.validate_manifest(path)
            asset.unlink()
            outside = root / 'outside'
            outside.write_bytes(b'controlled fixture')
            asset.symlink_to(outside)
            with self.assertRaises(ValueError):
                release.validate_manifest(path)

    def test_development_assets_cannot_be_published(self):
        with tempfile.TemporaryDirectory() as directory:
            path, _ = self.fixture(Path(directory), development=True)
            release.validate_manifest(path, production=False)
            with self.assertRaises(ValueError):
                release.validate_manifest(path)

    def test_manifest_states_are_typed(self):
        with tempfile.TemporaryDirectory() as directory:
            path, data = self.fixture(Path(directory))
            data['notarized'] = 'false'
            path.write_text(json.dumps(data))
            with self.assertRaises(ValueError):
                release.validate_manifest(path)

    def test_cask_requires_matching_architecture_identities(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            arm, _ = self.fixture(root)
            intel, data = self.fixture(root, 'osx-x64')
            release.homebrew_cask([arm, intel], root / 'clone.rb')
            text = (root / 'clone.rb').read_text()
            self.assertIn('on_arm do', text)
            self.assertIn('on_intel do', text)
            self.assertNotIn('latest/download', text)
            data['commit'] = 'b' * 40
            intel.write_text(json.dumps(data))
            with self.assertRaises(ValueError):
                release.homebrew_cask([arm, intel], root / 'bad.rb')

    def test_payload_hashes_and_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            binary = root / 'clone'
            binary.write_bytes(b'fixture binary')
            prefix = release.make_payload(root / 'payload', binary)
            for line in (prefix / 'installed.sha256').read_text().splitlines():
                expected, name = line.split('  ', 1)
                self.assertEqual(expected, release.digest(prefix / name))
            path_entry = root / 'payload/private/etc/paths.d' / release.IDENTIFIER
            self.assertEqual('/Library/Application Support/Clone/bin\n', path_entry.read_text())
            self.assertFalse((root / 'payload/opt/homebrew').exists())
            self.assertFalse((root / 'payload/usr/local/bin').exists())

    def test_release_gate_stays_closed(self):
        data = json.loads((release.ROOT / '.github/release-acceptance.json').read_text())
        data['ready'] = False
        with self.assertRaises(ValueError):
            check(data)
        incomplete = copy.deepcopy(data)
        incomplete['ready'] = True
        incomplete['checks']['authenticated_clone'] = 'pending'
        with self.assertRaises(ValueError):
            check(incomplete)


if __name__ == '__main__':
    unittest.main()
