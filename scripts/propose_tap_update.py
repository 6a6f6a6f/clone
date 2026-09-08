#!/usr/bin/env python3
"""Open a formula metadata PR only after the corresponding verified release is public."""
import argparse
import base64
import json
from pathlib import Path
import subprocess
import tempfile

from homebrew import validate as validate_manifest

REPOSITORY = '6a6f6a6f/clone'


def api(endpoint, payload=None):
    args = ['gh', 'api', endpoint]
    if payload is not None:
        args += ['--method', 'POST', '--input', '-']
    result = subprocess.run(args, input=json.dumps(payload) if payload is not None else None,
                            capture_output=True, text=True, check=True)
    return json.loads(result.stdout)


def propose(manifests):
    with tempfile.TemporaryDirectory(prefix='clone-tap-') as temporary:
        temporary = Path(temporary)
        generated = temporary / 'clone.rb'
        if len(manifests) != 1:
            raise ValueError('Exactly one verified Homebrew manifest is required.')
        entries = [validate_manifest(Path(path)) for path in manifests]
        generated.write_bytes((Path(manifests[0]).parent / 'clone.rb').read_bytes())
        release_version = entries[0]['version']
        published = api(f'repos/{REPOSITORY}/releases/tags/v{release_version}')
        if published['draft'] or not published['published_at']:
            raise ValueError('The release must be public before tap metadata is proposed.')
        assets = {asset['name']: asset.get('digest') for asset in published['assets']}
        for entry in entries:
            for name, checksum in entry['files'].items():
                if assets.get(name) != 'sha256:' + checksum:
                    raise ValueError('Published asset digests do not match the validated manifests.')
        branch = 'build/homebrew-v' + release_version
        base = api(f'repos/{REPOSITORY}/git/ref/heads/main')['object']['sha']
        api(f'repos/{REPOSITORY}/git/refs', {'ref': 'refs/heads/' + branch, 'sha': base})
        existing = subprocess.run(['gh', 'api', f'repos/{REPOSITORY}/contents/Formula/clone.rb?ref=main'], capture_output=True, text=True)
        payload = {'message': 'build(homebrew): publish Clone ' + release_version + ' formula',
                   'branch': branch, 'content': base64.b64encode(generated.read_bytes()).decode()}
        if existing.returncode == 0:
            payload['sha'] = json.loads(existing.stdout)['sha']
        elif 'HTTP 404' not in existing.stderr:
            raise ValueError('Could not inspect existing tap metadata.')
        subprocess.run(['gh', 'api', '--method', 'PUT', f'repos/{REPOSITORY}/contents/Formula/clone.rb', '--input', '-'],
                       input=json.dumps(payload), text=True, check=True, stdout=subprocess.DEVNULL)
        body = temporary / 'body.md'
        body.write_text(f'''## Summary

Publish the verified Homebrew formula for Clone {release_version}. Addresses #20.

## Motivation

Make the public Apple Silicon binaries installable and upgradable through the project-owned tap.

## Changes

- Select immutable Apple Silicon release URLs and SHA-256 values.
- Install the CLI and zsh completion through Homebrew without a .NET runtime.

## Validation

- The Apple Silicon manifest passed integrity and production-state validation.
- Public release asset digests match the validated artifacts.
- Formula installs the source-built CLI and zsh completion without running downloaded setup scripts.
- Review the release acceptance evidence before merging this metadata update.

## Risks

This changes the version installed by new Homebrew installations and upgrades. User clones and configuration are preserved.

## Security Considerations

Uses versioned URLs and fixed hashes; production publication rejects development manifests. Bottle provenance binds artifacts to the reviewed source commit.

## Migration Notes

After merging, users can tap this repository and install or upgrade 6a6f6a6f/clone/clone.

## Rollback Plan

Revert the formula metadata commit to restore the previous install target; preserve the immutable release assets.
''')
        subprocess.run(['gh', 'pr', 'create', '--repo', REPOSITORY, '--base', 'main', '--head', branch,
                        '--title', 'build(homebrew): publish Clone ' + release_version + ' formula', '--body-file', str(body)], check=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifests', nargs=1)
    args = parser.parse_args()
    try:
        propose(args.manifests)
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError):
        parser.exit(1, 'Tap proposal stopped. Inspect the public release and any partially created metadata branch before retrying.\n')
