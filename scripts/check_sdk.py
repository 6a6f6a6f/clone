#!/usr/bin/env python3
"""Compare the pinned SDK with Microsoft's current .NET 10 release metadata."""
import json
from pathlib import Path
import re
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
URL = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'


def parts(value):
    if not isinstance(value, str) or not re.fullmatch(r'10\.0\.[0-9]+', value):
        raise ValueError('Expected a stable .NET 10 SDK version.')
    return tuple(map(int, value.split('.')))


def main():
    pinned = json.loads((ROOT / 'global.json').read_text())['sdk']['version']
    with urllib.request.urlopen(URL, timeout=15) as response:
        data = response.read(4 * 1024 * 1024 + 1)
    if len(data) > 4 * 1024 * 1024:
        raise ValueError('Release metadata exceeds the size limit.')
    latest = json.loads(data)['latest-sdk']
    print(f'Pinned SDK: {pinned}; Microsoft latest .NET 10 SDK: {latest}')
    if parts(pinned) < parts(latest):
        raise SystemExit('A serviced SDK update is available. Propose an update and rerun local validation.')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError):
        raise SystemExit('Could not verify SDK servicing metadata; do not treat this check as passing.') from None
