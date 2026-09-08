#!/usr/bin/env python3
"""Build verifiable macOS artifacts. Signing is required unless development is explicit."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import tarfile
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PREFIX = Path('Library/Application Support/Clone')
IDENTIFIER = 'io.github.6a6f6a6f.clone'
ARCHITECTURES = {'osx-arm64': 'arm64'}


def run(*args, cwd=ROOT, capture=False):
    result = subprocess.run([str(a) for a in args], cwd=cwd, check=True,
                            text=True, stdout=subprocess.PIPE if capture else None)
    return result.stdout.strip() if capture else None


def version(value):
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', value) or len(value) > 32:
        raise ValueError('Version must be a numeric SemVer release, for example 0.2.0.')
    return value


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def validate_manifest(path, production=True):
    data = json.loads(path.read_text())
    version(data['version'])
    if data['rid'] not in ARCHITECTURES or not re.fullmatch('[0-9a-f]{40}', data['commit']):
        raise ValueError('Invalid artifact identity.')
    if any(type(data.get(key)) is not bool for key in ['development', 'native_smoke', 'notarized']):
        raise ValueError('Manifest validation states must be boolean.')
    if production and (data['development'] or not data['native_smoke'] or not data['notarized']):
        raise ValueError('Unsigned or unvalidated development artifacts cannot be released.')
    expected_suffix = '-development' if data['development'] else ''
    expected = {f"clone-{data['version']}-{data['rid']}{expected_suffix}{extension}" for extension in ('.tar.gz', '.pkg')}
    if set(data['files']) != expected:
        raise ValueError('Unexpected release asset set.')
    for name, checksum in data['files'].items():
        if Path(name).name != name or not re.fullmatch('[0-9a-f]{64}', checksum):
            raise ValueError('Invalid asset name or checksum.')
        asset = path.parent / name
        if asset.is_symlink() or not asset.is_file() or digest(asset) != checksum:
            raise ValueError('Artifact integrity verification failed.')
    return data


def verify_tag(release_version):
    if run('git', 'status', '--porcelain', capture=True):
        raise ValueError('Production packaging requires a clean checkout.')
    commit = run('git', 'rev-parse', 'HEAD', capture=True)
    if run('git', 'rev-parse', f'v{release_version}^{{commit}}', capture=True) != commit:
        raise ValueError('The version tag must identify the checked-out commit.')
    return commit


def make_payload(directory, binary):
    prefix = directory / PREFIX
    (prefix / 'bin').mkdir(parents=True)
    (prefix / 'share/zsh/site-functions').mkdir(parents=True)
    shutil.copy2(binary, prefix / 'bin/clone')
    shutil.copy2(ROOT / 'completions/_clone', prefix / 'share/zsh/site-functions/_clone')
    shutil.copy2(ROOT / 'LICENSE', prefix / 'LICENSE')
    shutil.copy2(ROOT / 'packaging/macos/uninstall.sh', prefix / 'uninstall.sh')
    (prefix / 'bin/clone').chmod(0o755)
    (prefix / 'uninstall.sh').chmod(0o755)
    files = ['bin/clone', 'share/zsh/site-functions/_clone', 'LICENSE', 'uninstall.sh']
    (prefix / 'installed.sha256').write_text(''.join(f'{digest(prefix / name)}  {name}\n' for name in files))
    paths = directory / 'private/etc/paths.d'
    paths.mkdir(parents=True)
    (paths / IDENTIFIER).write_text('/' + str(PREFIX / 'bin') + '\n')
    for entry in directory.rglob('*'):
        if entry.is_dir():
            entry.chmod(0o755)
    return prefix


def prepare(release_version, rid, development):
    version(release_version)
    if rid not in ARCHITECTURES:
        raise ValueError('Only osx-arm64 (Apple Silicon) is supported.')
    if platform.system() != 'Darwin':
        raise ValueError('macOS packaging requires a Mac.')
    native = platform.machine() == ARCHITECTURES[rid]
    if not native:
        raise ValueError('Packaging requires a native Apple Silicon host.')
    app_identity = os.environ.get('CLONE_APP_IDENTITY')
    installer_identity = os.environ.get('CLONE_INSTALLER_IDENTITY')
    notary_profile = os.environ.get('CLONE_NOTARY_PROFILE')
    keychain = os.environ.get('CLONE_SIGNING_KEYCHAIN')
    keychain_args = ['--keychain', keychain] if keychain else []
    if not development and not all([app_identity, installer_identity, notary_profile]):
        raise ValueError('Developer ID Application, Installer, and a notarytool keychain profile are required.')
    commit = run('git', 'rev-parse', 'HEAD', capture=True) if development else verify_tag(release_version)
    output = ROOT / 'artifacts/release' / release_version / rid
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError('Output already exists. Preserve it or choose another version; assets are never overwritten.')
    with tempfile.TemporaryDirectory(prefix='clone-package-') as work:
        work = Path(work)
        publish = work / 'publish'
        run('dotnet', 'publish', ROOT / 'Clone.Console/Clone.Console.csproj', '-c', 'Release', '-r', rid,
            '-p:Version=' + release_version, '-o', publish)
        binary = publish / 'clone'
        run('/usr/bin/lipo', binary, '-verify_arch', ARCHITECTURES[rid])
        if not development:
            run('/usr/bin/codesign', '--force', '--options', 'runtime', '--timestamp', '--sign', app_identity, *keychain_args, binary)
            run('/usr/bin/codesign', '--verify', '--strict', binary)
        if native and run(binary, '--version', capture=True) != 'clone ' + release_version:
            raise ValueError('Native binary version mismatch.')
        payload = work / 'payload'
        prefix = make_payload(payload, binary)
        scripts = work / 'scripts'
        scripts.mkdir()
        preinstall = scripts / 'preinstall'
        preinstall.write_text((ROOT / 'packaging/macos/preinstall').read_text() +
            f'\n[ "$(/usr/bin/uname -m)" = "{ARCHITECTURES[rid]}" ] || {{ echo "Wrong package architecture." >&2; exit 1; }}\n' +
            '[ "$(/usr/bin/sw_vers -productVersion | /usr/bin/cut -d. -f1)" -ge 14 ] || { echo "macOS 14 or later is required." >&2; exit 1; }\n')
        preinstall.chmod(0o755)
        suffix = '-development' if development else ''
        stem = f'clone-{release_version}-{rid}{suffix}'
        package = output / (stem + '.pkg')
        command = ['/usr/bin/pkgbuild', '--root', payload, '--identifier', IDENTIFIER, '--version', release_version,
                   '--install-location', '/', '--ownership', 'recommended', '--scripts', scripts]
        if not development:
            command += ['--sign', installer_identity, '--timestamp', *keychain_args]
        run(*command, package)
        archive = output / (stem + '.tar.gz')
        with tarfile.open(archive, 'w:gz') as tar:
            for name in ['bin/clone', 'share/zsh/site-functions/_clone', 'LICENSE']:
                tar.add(prefix / name, arcname=name, recursive=False)
        if not development:
            # The archive and package contain the identical signed executable.
            notarization_zip = work / 'signed-binary.zip'
            with zipfile.ZipFile(notarization_zip, 'w') as zipped:
                zipped.write(binary, 'clone')
            for asset in [notarization_zip, package]:
                run('/usr/bin/xcrun', 'notarytool', 'submit', asset, '--keychain-profile', notary_profile, *keychain_args, '--wait', '--timeout', '30m')
            run('/usr/bin/xcrun', 'stapler', 'staple', package)
            run('/usr/bin/xcrun', 'stapler', 'validate', package)
            run('/usr/sbin/pkgutil', '--check-signature', package)
            run('/usr/sbin/spctl', '--assess', '--type', 'install', package)
        data = {'version': release_version, 'rid': rid, 'commit': commit, 'minimum_macos': '14.0',
                'development': development, 'native_smoke': native, 'notarized': not development,
                'files': {asset.name: digest(asset) for asset in [archive, package]}}
        manifest = output / 'manifest.json'
        manifest.write_text(json.dumps(data, indent=2) + '\n')
        (output / 'SHA256SUMS').write_text(''.join(f'{value}  {name}\n' for name, value in data['files'].items()))
        validate_manifest(manifest, production=not development)
        print(manifest)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    build = commands.add_parser('prepare')
    build.add_argument('--version', required=True, type=version)
    build.add_argument('--rid', required=True, choices=ARCHITECTURES)
    build.add_argument('--development', action='store_true', help='Explicitly create unsigned artifacts that cannot be released')
    verify = commands.add_parser('verify')
    verify.add_argument('manifest', type=Path)
    verify.add_argument('--development', action='store_true')
    args = parser.parse_args()
    try:
        if args.command == 'prepare': prepare(args.version, args.rid, args.development)
        elif args.command == 'verify': validate_manifest(args.manifest, production=not args.development)
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'Packaging failed: {error}\n')


if __name__ == '__main__':
    main()
