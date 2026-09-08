#!/usr/bin/env python3
"""Prepare an isolated CI keychain without logging credentials or changing user search lists."""
import base64
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import tempfile


def sensitive_run(*args):
    result = subprocess.run(list(map(str, args)), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if result.returncode:
        raise RuntimeError('Signing credential setup failed; inspect certificate validity and protected environment settings.')


def main():
    runner = Path(os.environ['RUNNER_TEMP']).resolve()
    state = runner / 'clone-signing-state.json'
    if sys.argv[1:] == ['cleanup']:
        if state.exists():
            directory = Path(json.loads(state.read_text())['directory'])
            if directory.parent != runner or not directory.name.startswith('clone-signing-'):
                raise ValueError('Invalid signing state directory.')
            subprocess.run(['/usr/bin/security', 'delete-keychain', str(directory / 'signing.keychain-db')], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            shutil.rmtree(directory)
            state.unlink()
        return
    if sys.argv[1:] != ['setup'] or state.exists():
        raise ValueError('Use setup or cleanup with a fresh signing state.')
    names = ['APPLE_APP_CERT_BASE64', 'APPLE_INSTALLER_CERT_BASE64', 'APPLE_CERT_PASSWORD',
             'APPLE_NOTARY_KEY_BASE64', 'APPLE_NOTARY_KEY_ID', 'APPLE_NOTARY_ISSUER_ID']
    if any(not os.environ.get(name) for name in names):
        raise ValueError('Required signing secrets are missing; no credentials were printed.')
    directory = Path(tempfile.mkdtemp(prefix='clone-signing-', dir=runner))
    state.write_text(json.dumps({'directory': str(directory)}))
    state.chmod(0o600)
    keychain = directory / 'signing.keychain-db'
    password = secrets.token_urlsafe(32)
    sensitive_run('/usr/bin/security', 'create-keychain', '-p', password, keychain)
    sensitive_run('/usr/bin/security', 'set-keychain-settings', '-lut', '3600', keychain)
    sensitive_run('/usr/bin/security', 'unlock-keychain', '-p', password, keychain)
    for name in ['APPLE_APP_CERT_BASE64', 'APPLE_INSTALLER_CERT_BASE64']:
        certificate = directory / (name + '.p12')
        certificate.write_bytes(base64.b64decode(os.environ[name], validate=True))
        certificate.chmod(0o600)
        sensitive_run('/usr/bin/security', 'import', certificate, '-k', keychain, '-P', os.environ['APPLE_CERT_PASSWORD'],
                      '-T', '/usr/bin/codesign', '-T', '/usr/bin/productsign', '-T', '/usr/bin/pkgbuild')
        certificate.unlink()
    sensitive_run('/usr/bin/security', 'set-key-partition-list', '-S', 'apple-tool:,apple:,codesign:', '-s', '-k', password, keychain)
    key = directory / 'notary.p8'
    key.write_bytes(base64.b64decode(os.environ['APPLE_NOTARY_KEY_BASE64'], validate=True))
    key.chmod(0o600)
    sensitive_run('/usr/bin/xcrun', 'notarytool', 'store-credentials', 'clone-release', '--key', key,
                  '--key-id', os.environ['APPLE_NOTARY_KEY_ID'], '--issuer', os.environ['APPLE_NOTARY_ISSUER_ID'], '--keychain', keychain)
    key.unlink()
    with Path(os.environ['GITHUB_ENV']).open('a') as environment:
        environment.write(f'CLONE_SIGNING_KEYCHAIN={keychain}\nCLONE_NOTARY_PROFILE=clone-release\n')


if __name__ == '__main__':
    try:
        main()
    except Exception:
        raise SystemExit('Signing setup/cleanup failed. No credential details were logged; cleanup is required.') from None
