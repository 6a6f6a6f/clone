#!/usr/bin/env python3
"""Exercise real bottle installation and execution in an isolated Homebrew tap."""
import argparse
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tarfile
import tempfile

from homebrew import ROOT, archive, bottle_payload, digest, formula, validate, version


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifests', nargs=2, type=Path)
    parser.add_argument('--brew-test', action='store_true', help='Also use the Brew developer test runner; requires supported Xcode/CLT')
    args = parser.parse_args()
    if platform.system() != 'Darwin' or platform.machine() != 'arm64' or not shutil.which('brew'):
        parser.error('A native Apple Silicon Homebrew installation is required.')
    entries = [(p.resolve(), validate(p, production=False)) for p in args.manifests]
    if entries[0][1]['version'] == entries[1][1]['version']:
        parser.error('Two different versions are required.')
    environment = os.environ.copy()
    environment.update(HOMEBREW_NO_AUTO_UPDATE='1', HOMEBREW_NO_ANALYTICS='1', HOMEBREW_NO_INSTALL_CLEANUP='1')

    def run(*command, check=True):
        result = subprocess.run(list(map(str, command)), env=environment, capture_output=True, text=True, timeout=180)
        if check and result.returncode:
            raise RuntimeError(result.stdout + result.stderr)
        return result

    name = 'clone-migration-check'
    tap_name = '6a6f6a6f/clone-migration-check'
    qualified = tap_name + '/' + name
    tap = Path(run('brew', '--repository').stdout.strip()) / 'Library/Taps/6a6f6a6f/homebrew-clone-migration-check'
    rack = Path(run('brew', '--cellar').stdout.strip()) / name
    if any(p.exists() or p.is_symlink() for p in (tap, rack)):
        raise ValueError('Refusing to touch an existing fixture tap or installation.')
    with tempfile.TemporaryDirectory(prefix='clone-brew-test-') as temporary:
        work = Path(temporary)
        tap.mkdir(parents=True)
        (tap / 'Formula').mkdir()
        run('git', 'init', '-b', 'main', tap)
        installed = False
        try:
            fixtures = []
            for path, data in entries:
                release_version, tag = data['version'], data['bottle_tag']
                folder = work / release_version
                folder.mkdir()
                original = path.parent / f'clone-{release_version}.{tag}.bottle.tar.gz'
                with tarfile.open(original) as contents:
                    # Extract only the three expected regular files, never arbitrary archive paths.
                    for relative in ('bin/clone', 'share/zsh/site-functions/_clone', 'LICENSE'):
                        member = contents.getmember(f'clone/{release_version}/{relative}')
                        if not member.isfile(): raise ValueError('Unexpected bottle payload type.')
                        destination = folder / Path(relative).name
                        destination.write_bytes(contents.extractfile(member).read())
                source_sha = data['files'][f'clone-{release_version}-source.tar.gz']
                payload = folder / 'payload'
                bottle_payload(payload, folder / 'clone', folder / '_clone', folder / 'LICENSE', release_version, source_sha, tag, name=name)
                bottle = folder / f'{name}-{release_version}.{tag}.bottle.tar.gz'
                archive(payload, bottle)
                text = formula(release_version, source_sha, digest(bottle), tag, name=name, root_url=folder.as_uri())
                text = text.replace('  depends_on \"git\"\n', '')
                text = text.replace('  def install', '  keg_only :versioned_formula\n\n  def install')
                fixtures.append((release_version, text, digest(folder / 'clone'), bottle))

            def install_metadata(fixture):
                (tap / 'Formula' / (name + '.rb')).write_text(fixture[1])

            def verify(fixture):
                binary = rack / fixture[0] / 'bin/clone'
                if digest(binary) != fixture[2]: raise ValueError('Installed binary mismatch.')
                clean_environment = environment.copy()
                clean_environment.update(PATH='/usr/bin:/bin:/usr/sbin:/sbin', DOTNET_ROOT='/nonexistent-clone-test-runtime')
                result = subprocess.run([str(binary), '--version'], env=clean_environment, check=True, capture_output=True, text=True)
                if result.stdout.strip() != 'clone ' + fixture[0]: raise ValueError('Installed version mismatch.')
                preview = run(binary, '--dry-run', '--root', work.resolve() / 'projects', '--config-dir', work.resolve() / 'config', 'https://example.com/team/repo')
                if not preview.stdout.strip().endswith('/example.com/team/repo'): raise ValueError('Installed preview failed.')
                if args.brew_test: run('brew', 'test', qualified)
                if (rack / fixture[0] / 'share/zsh/site-functions/_clone').read_bytes() != (ROOT / 'completions/_clone').read_bytes():
                    raise ValueError('Installed completion mismatch.')

            install_metadata(fixtures[0])
            run('brew', 'install', '--formula', '--force-bottle', qualified)
            installed = True
            verify(fixtures[0])
            print('Bottle install and native execution passed.', flush=True)
            install_metadata(fixtures[1])
            next_bottle = fixtures[1][3]
            original_bytes = next_bottle.read_bytes()
            next_bottle.write_bytes(original_bytes + b'tampered')
            rejected = run('brew', 'upgrade', '--formula', qualified, check=False)
            if rejected.returncode == 0 or not any(word in (rejected.stdout + rejected.stderr).lower() for word in ('sha256 mismatch', 'sha-256 mismatch', 'checksum mismatch', 'different checksum')):
                raise ValueError('Tamper rejection needs inspection: ' + rejected.stdout + rejected.stderr)
            verify(fixtures[0])
            next_bottle.write_bytes(original_bytes)
            print('Tampered upgrade rejected; previous installation remains usable.', flush=True)
            run('brew', 'upgrade', '--formula', qualified)
            verify(fixtures[1])
            print('Bottle upgrade and native execution passed.', flush=True)
            run('brew', 'reinstall', '--formula', '--force-bottle', qualified)
            verify(fixtures[1])
            print('Bottle reinstall and native execution passed.', flush=True)
        finally:
            # Preserve any unrelated installations, credentials, clones and configuration.
            removal = run('brew', 'uninstall', '--force', '--formula', qualified, check=False) if installed or rack.exists() else None
            untap = run('brew', 'untap', '--force', tap_name, check=False)
            if (removal and removal.returncode) or untap.returncode:
                raise RuntimeError('Fixture cleanup needs inspection; unrelated data was not removed.')
        if tap.exists() or rack.exists(): raise ValueError('Fixture data remains.')
        print('Bottle uninstall and fixture cleanup passed.', flush=True)


if __name__ == '__main__':
    main()
