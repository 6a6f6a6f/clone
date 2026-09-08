#!/usr/bin/env python3
"""Exercise Cask lifecycle with uniquely named local development fixtures."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tarfile

from release import ROOT, validate_manifest, version


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--from-version', type=version, default='0.2.0')
    parser.add_argument('--to-version', type=version, default='0.2.1')
    args = parser.parse_args()
    brew = shutil.which('brew')
    if not brew or platform.system() != 'Darwin':
        parser.error('An existing macOS Homebrew installation is required.')
    if platform.machine() != 'arm64':
        parser.error('Only native Apple Silicon is supported.')
    rid = 'osx-arm64'
    environment = os.environ.copy()
    environment.update(HOMEBREW_NO_AUTO_UPDATE='1', HOMEBREW_NO_ANALYTICS='1', HOMEBREW_NO_INSTALL_CLEANUP='1')

    def run(*command, check=True):
        result = subprocess.run(list(map(str, command)), env=environment, capture_output=True, text=True, timeout=180)
        if check and result.returncode:
            raise RuntimeError(result.stdout + result.stderr)
        return result

    fixtures = {}
    for release_version in [args.from_version, args.to_version]:
        directory = ROOT / 'artifacts/release' / release_version / rid
        data = validate_manifest(directory / 'manifest.json', production=False)
        if not data['development']:
            raise ValueError('This fixture runner expects explicitly generated development packages.')
        archive = directory / next(name for name in data['files'] if name.endswith('.tar.gz'))
        fixtures[release_version] = (archive, data)
    token = 'clone-migration-check'
    tap_name = '6a6f6a6f/clone-migration-check'
    qualified = tap_name + '/' + token
    prefix = Path(run(brew, '--prefix').stdout.strip())
    tap = Path(run(brew, '--repository').stdout.strip()) / 'Library/Taps/6a6f6a6f/homebrew-clone-migration-check'
    linked = prefix / 'bin' / token
    completion = prefix / 'share/zsh/site-functions/_clone_migration_check'
    if any(path.exists() or path.is_symlink() for path in [tap, linked, completion]):
        raise ValueError('Refusing to touch an existing test tap or link.')
    if run(brew, 'list', '--cask', '--versions', token, check=False).stdout.strip():
        raise ValueError('Refusing to touch an existing test installation.')
    tap.mkdir(parents=True)
    (tap / 'Casks').mkdir()
    run('git', 'init', '-b', 'main', tap)

    def write_cask(release_version):
        archive, data = fixtures[release_version]
        (tap / 'Casks/clone-migration-check.rb').write_text(f'''cask "clone-migration-check" do
  version "{release_version}"
  sha256 "{data['files'][archive.name]}"
  url "{archive.as_uri()}"
  name "Clone migration fixture"
  desc "Temporary local packaging acceptance"
  homepage "https://github.com/6a6f6a6f/clone"
  depends_on arch: :arm64
  depends_on macos: ">= :sonoma"
  depends_on formula: "git"
  binary "bin/clone", target: "clone-migration-check"
  zsh_completion "share/zsh/site-functions/_clone", target: "_clone_migration_check"
end
''')

    def verify_payload(release_version):
        archive, _ = fixtures[release_version]
        with tarfile.open(archive) as contents:
            expected = hashlib.sha256(contents.extractfile('bin/clone').read()).hexdigest()
        if hashlib.sha256(linked.read_bytes()).hexdigest() != expected or release_version not in str(linked.resolve()):
            raise ValueError('Installed binary does not match the requested fixture version.')
        if completion.read_bytes() != (ROOT / 'completions/_clone').read_bytes():
            raise ValueError('Installed completion does not match the source.')
        # Preserve quarantine. Execution after a downloaded install needs a signed release.

    try:
        write_cask(args.from_version)
        run(brew, 'install', '--cask', qualified)
        verify_payload(args.from_version)
        print('Cask install payload verified.', flush=True)
        write_cask(args.to_version)
        run(brew, 'upgrade', '--cask', qualified)
        verify_payload(args.to_version)
        print('Cask upgrade payload verified.', flush=True)
        run(brew, 'reinstall', '--cask', qualified)
        verify_payload(args.to_version)
        print('Cask reinstall payload verified.', flush=True)
    finally:
        removal = run(brew, 'uninstall', '--cask', qualified, check=False)
        untap = run(brew, 'untap', '--force', tap_name, check=False)
        if removal.returncode or untap.returncode:
            raise RuntimeError('Fixture cleanup needs inspection; no unrelated installation was removed.')
    if any(path.exists() or path.is_symlink() for path in [tap, linked, completion]):
        raise ValueError('Fixture links or tap remain after cleanup.')
    print('Cask uninstall and fixture cleanup verified; signed runtime acceptance remains separate.', flush=True)


if __name__ == '__main__':
    main()
