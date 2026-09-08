#!/usr/bin/env python3
"""Fail closed until the complete distribution acceptance matrix is reviewed."""
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
REQUIRED = {'arm64_current_macos', 'arm64_macos_14', 'intel_current_macos', 'intel_macos_14',
            'authenticated_clone', 'homebrew_install_upgrade_uninstall',
            'pkg_install_upgrade_uninstall', 'quarantined_signed_download'}


def check(data):
    if data.get('ready') is not True or set(data.get('checks', {})) != REQUIRED:
        raise ValueError('Release acceptance is outstanding; do not publish or enable CI/CD.')
    if any(result != 'passed' for result in data['checks'].values()) or not data.get('evidence'):
        raise ValueError('Every acceptance check needs passing evidence.')
    commit = data.get('reviewed_commit')
    if not isinstance(commit, str) or not re.fullmatch('[0-9a-f]{40}', commit):
        raise ValueError('A reviewed source commit is required.')
    subprocess.run(['git', 'merge-base', '--is-ancestor', commit, 'HEAD'], cwd=ROOT, check=True)
    changed = subprocess.run(['git', 'diff', '--name-only', commit, 'HEAD', '--', 'Clone.Console', 'Clone.Git',
                              'Clone.Tests', 'scripts', 'packaging', 'completions', '.github/workflows',
                              'global.json', 'Directory.Build.props', 'Directory.Build.targets',
                              'Makefile', 'NuGet.Config', 'NuGet.config', 'nuget.config', 'Clone.sln',
                              '.config', 'LICENSE'], cwd=ROOT, check=True, capture_output=True, text=True)
    if changed.stdout.strip():
        raise ValueError('Shipping or validation code changed after the reviewed commit.')


if __name__ == '__main__':
    try:
        check(json.loads((ROOT / '.github/release-acceptance.json').read_text()))
    except (ValueError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error)) from None
