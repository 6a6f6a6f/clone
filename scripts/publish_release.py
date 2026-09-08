#!/usr/bin/env python3
"""Publish a complete draft only after acceptance, provenance and hashes pass."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

from check_release_gate import check
from homebrew import ROOT, validate as validate_manifest
from release import digest, verify_tag, version

REPOSITORY = '6a6f6a6f/clone'


def gh(*args, capture=False):
    result = subprocess.run(['gh', *map(str, args)], cwd=ROOT, check=True, text=True,
                            stdout=subprocess.PIPE if capture else None)
    return result.stdout if capture else None


def publish(manifests):
    check(json.loads((ROOT / '.github/release-acceptance.json').read_text()))
    entries = [validate_manifest(Path(path)) for path in manifests]
    if len(entries) != 1 or entries[0]['rid'] != 'osx-arm64':
        raise ValueError('Exactly one validated Apple Silicon manifest is required.')
    identities = {(entry['version'], entry['commit']) for entry in entries}
    if len(identities) != 1:
        raise ValueError('Release artifact identities disagree.')
    release_version, commit = identities.pop()
    version(release_version)
    if verify_tag(release_version) != commit:
        raise ValueError('Artifacts do not match the checked-out release tag.')
    tag = 'v' + release_version
    assets = []
    for path, entry in zip(map(Path, manifests), entries):
        for name in entry['files']:
            asset = path.parent / name
            gh('attestation', 'verify', asset, '--repo', REPOSITORY, '--signer-workflow',
               REPOSITORY + '/.github/workflows/release.yml', '--source-digest', commit, '--deny-self-hosted-runners')
            assets.append(asset)
    with tempfile.TemporaryDirectory(prefix='clone-release-') as temporary:
        temporary = Path(temporary)
        sums = temporary / 'SHA256SUMS'
        sums.write_text(''.join(f'{checksum}  {name}\n' for entry in entries for name, checksum in entry['files'].items()))
        assets.append(sums)
        notes = temporary / 'notes.md'
        notes.write_text(f'Clone {release_version} for Apple Silicon Macs running macOS 14 and later.\n\n'
                         'Includes the secure clone core, first-use configuration, Homebrew metadata, '
                         'and source-built Homebrew bottles. No Apple certificate or installed .NET runtime is required for bottled installation.\n\n'
                         'Verify SHA256SUMS and GitHub artifact attestations before direct use. '
                         'This release does not contain the deferred signed system package. See the installation and rollback documentation in the repository.\n')
        # No --clobber and no reuse of an existing release: retries must inspect a partial draft.
        gh('release', 'create', tag, '--repo', REPOSITORY, '--draft', '--verify-tag', '--title',
           'feat(release): ship Clone ' + release_version + ' for macOS', '--notes-file', notes)
        gh('release', 'upload', tag, *assets, '--repo', REPOSITORY)
        remote = json.loads(gh('api', f'repos/{REPOSITORY}/releases/tags/{tag}', capture=True))
        expected = {asset.name: (asset.stat().st_size, 'sha256:' + digest(asset)) for asset in assets}
        if not remote['draft'] or {asset['name']: (asset['size'], asset.get('digest')) for asset in remote['assets']} != expected:
            raise ValueError('Draft upload is incomplete; the draft was not published.')
        gh('release', 'edit', tag, '--repo', REPOSITORY, '--draft=false', '--latest')
        print('Release published. Promote Formula/clone.rb to the tap through a reviewed metadata PR.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifests', nargs=1)
    args = parser.parse_args()
    try:
        publish(args.manifests)
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'Release publication stopped: {error}\n')
