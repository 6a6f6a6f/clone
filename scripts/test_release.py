import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import publish_release

import release
import homebrew
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

    def homebrew_fixture(self, root):
        data = {'channel': 'homebrew', 'version': '0.2.0', 'rid': 'osx-arm64',
                'bottle_tag': 'arm64_tahoe', 'commit': 'a' * 40, 'development': False,
                'native_smoke': True, 'files': {}}
        for name in ['clone-0.2.0-source.tar.gz', 'clone-0.2.0.arm64_tahoe.bottle.tar.gz']:
            (root / name).write_bytes(b'fixture')
            data['files'][name] = release.digest(root / name)
        (root / 'clone.rb').write_text(homebrew.formula('0.2.0', data['files']['clone-0.2.0-source.tar.gz'],
            data['files']['clone-0.2.0.arm64_tahoe.bottle.tar.gz'], 'arm64_tahoe'))
        data['files']['clone.rb'] = release.digest(root / 'clone.rb')
        path = root / 'manifest.json'
        path.write_text(json.dumps(data))
        return path, data

    def test_homebrew_does_not_require_apple_signing(self):
        with tempfile.TemporaryDirectory() as directory:
            path, _ = self.homebrew_fixture(Path(directory))
            homebrew.validate(path)
            text = (path.parent / 'clone.rb').read_text()
            self.assertIn('depends_on arch: :arm64', text)
            self.assertIn('bottle do', text)
            self.assertNotIn('cask ', text)
            self.assertNotIn('latest/download', text)

    def test_homebrew_rejects_development_intel_and_wrong_channel(self):
        with tempfile.TemporaryDirectory() as directory:
            path, data = self.homebrew_fixture(Path(directory))
            for key, value in [('development', True), ('native_smoke', 'true'), ('rid', 'osx-x64'),
                               ('bottle_tag', 'arm64_sonoma'), ('bottle_tag', 'arm64_sequoia'), ('bottle_tag', 'arm64_fake'), ('channel', 'signed-pkg')]:
                altered = copy.deepcopy(data)
                altered[key] = value
                path.write_text(json.dumps(altered))
                with self.subTest(key=key), self.assertRaises(ValueError): homebrew.validate(path)

    def test_homebrew_rejects_tampered_assets_and_formula(self):
        with tempfile.TemporaryDirectory() as directory:
            path, data = self.homebrew_fixture(Path(directory))
            formula = path.parent / 'clone.rb'
            formula.write_text('malicious formula')
            with self.assertRaises(ValueError): homebrew.validate(path)
            data['files']['clone.rb'] = release.digest(formula)
            path.write_text(json.dumps(data))
            with self.assertRaises(ValueError): homebrew.validate(path)

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

    def test_draft_promotion_uses_id_and_checks_complete_assets(self):
        for scenario in ('valid', 'wrong-tag', 'missing-asset', 'bad-digest'):
            with self.subTest(scenario=scenario), tempfile.TemporaryDirectory() as directory:
                manifest, data = self.homebrew_fixture(Path(directory))
                calls, uploaded = [], []

                def fake_gh(*args, capture=False):
                    calls.append(args)
                    if args[:2] == ('release', 'upload'):
                        for path in args[3:args.index('--repo')]:
                            path = Path(path)
                            uploaded.append({'name': path.name, 'size': path.stat().st_size,
                                             'digest': 'sha256:' + release.digest(path)})
                    if args[:2] == ('release', 'view'):
                        self.assertIn('databaseId', args)
                        return '123\n'
                    if args[0] == 'api':
                        self.assertEqual('repos/6a6f6a6f/clone/releases/123', args[1])
                        assets = copy.deepcopy(uploaded)
                        if scenario == 'missing-asset': assets.pop()
                        if scenario == 'bad-digest': assets[0]['digest'] = 'sha256:' + '0' * 64
                        return json.dumps({'draft': True, 'tag_name': 'v9.9.9' if scenario == 'wrong-tag' else 'v0.2.0', 'assets': assets})
                    return ''

                with patch.object(publish_release, 'check'), patch.object(publish_release, 'verify_tag', return_value=data['commit']), patch.object(publish_release, 'gh', side_effect=fake_gh):
                    if scenario == 'valid':
                        publish_release.publish([manifest])
                    else:
                        with self.assertRaises(ValueError): publish_release.publish([manifest])
                self.assertEqual(scenario == 'valid', any(call[:2] == ('release', 'edit') for call in calls))

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
