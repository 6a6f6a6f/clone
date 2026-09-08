#!/usr/bin/env python3
"""Build source-based Apple Silicon bottles for the project-owned Homebrew tap."""
import argparse
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import tarfile
import tempfile

from release import ROOT, digest, run, verify_tag, version

REPOSITORY = '6a6f6a6f/clone'
TAGS = {'26': 'arm64_tahoe'}


def formula(release_version, source_sha, bottle_sha=None, tag=None, *, name='clone', root_url=None):
    version(release_version)
    if not re.fullmatch(r'[a-z][a-z0-9-]*', name) or not re.fullmatch('[0-9a-f]{64}', source_sha):
        raise ValueError('Invalid formula identity.')
    root_url = root_url or f'https://github.com/{REPOSITORY}/releases/download/v{release_version}'
    if any(c in root_url for c in ('"', '\\', '\n', '\r', '#')):
        raise ValueError('Unsafe artifact URL.')
    klass = ''.join(part.capitalize() for part in name.split('-'))
    text = f'''class {klass} < Formula
  desc "Organize Git checkouts safely"
  homepage "https://github.com/{REPOSITORY}"
  url "{root_url}/clone-{release_version}-source.tar.gz"
  version "{release_version}"
  sha256 "{source_sha}"
  license "WTFPL"

'''
    if bottle_sha is not None:
        if tag not in TAGS.values() or not re.fullmatch('[0-9a-f]{64}', bottle_sha):
            raise ValueError('Invalid bottle metadata.')
        text += f'''  bottle do
    root_url "{root_url}"
    sha256 cellar: :any_skip_relocation, {tag}: "{bottle_sha}"
  end

'''
    text += '''  depends_on "dotnet" => :build
  depends_on "git"
  depends_on arch: :arm64
  depends_on macos: :tahoe

  def install
    ENV["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    ENV["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    ENV["NUGET_PACKAGES"] = buildpath/"vendor/nuget"
    system "dotnet", "publish", "Clone.Console/Clone.Console.csproj",
           "-c", "Release", "-r", "osx-arm64", "-p:Version=#{version}",
           "-p:NuGetAudit=false", "-o", "publish"
    bin.install "publish/clone"
    zsh_completion.install "completions/_clone"
  end

  test do
    assert_equal "clone #{version}", shell_output("#{bin}/clone --version").strip
    assert_match "example.com/team/repo", shell_output(
      "#{bin}/clone --dry-run --root #{testpath}/projects --config-dir #{testpath}/config https://example.com/team/repo"
    )
  end
end
'''
    return text


def validate(path, production=True):
    path = Path(path)
    data = json.loads(path.read_text())
    version(data['version'])
    if data.get('channel') != 'homebrew' or data.get('rid') != 'osx-arm64' or data.get('bottle_tag') not in TAGS.values():
        raise ValueError('Unsupported Homebrew artifact identity.')
    if not re.fullmatch('[0-9a-f]{40}', data['commit']):
        raise ValueError('Invalid source commit.')
    if any(type(data.get(k)) is not bool for k in ('development', 'native_smoke')):
        raise ValueError('Validation states must be boolean.')
    if production and (data['development'] or not data['native_smoke']):
        raise ValueError('Development or unvalidated bottles cannot be released.')
    expected = {f"clone-{data['version']}.{data['bottle_tag']}.bottle.tar.gz",
                f"clone-{data['version']}-source.tar.gz", 'clone.rb'}
    if set(data['files']) != expected:
        raise ValueError('Unexpected Homebrew asset set.')
    for name, checksum in data['files'].items():
        asset = path.parent / name
        if not re.fullmatch('[0-9a-f]{64}', checksum) or asset.is_symlink() or not asset.is_file() or digest(asset) != checksum:
            raise ValueError('Homebrew artifact integrity check failed.')
    expected_formula = formula(data['version'], data['files'][f"clone-{data['version']}-source.tar.gz"],
                               data['files'][f"clone-{data['version']}.{data['bottle_tag']}.bottle.tar.gz"], data['bottle_tag'])
    if (path.parent / 'clone.rb').read_text() != expected_formula:
        raise ValueError('Formula does not match the verified release metadata.')
    return data


def bottle_payload(root, binary, completion, license_file, release_version, source_sha, tag, *, name='clone'):
    """Write Homebrew's documented keg layout; the tap owns this build pipeline."""
    formula_text = formula(release_version, source_sha, name=name)
    keg = root / name / release_version
    (keg / 'bin').mkdir(parents=True)
    (keg / 'share/zsh/site-functions').mkdir(parents=True)
    (keg / '.brew').mkdir()
    shutil.copy2(binary, keg / 'bin/clone')
    shutil.copy2(completion, keg / 'share/zsh/site-functions/_clone')
    shutil.copy2(license_file, keg / 'LICENSE')
    (keg / 'bin/clone').chmod(0o755)
    (keg / '.brew' / (name + '.rb')).write_text(formula_text)
    receipt = {'homebrew_version': run('brew', '--version', capture=True).splitlines()[0].removeprefix('Homebrew '),
               'used_options': [], 'unused_options': [], 'built_as_bottle': True, 'poured_from_bottle': False,
               'compiler': 'clang', 'arch': 'arm64', 'runtime_dependencies': [],
               'source': {'spec': 'stable', 'versions': {'stable': release_version, 'head': None, 'version_scheme': 0},
                          'tap': REPOSITORY, 'path': f'Formula/{name}.rb'},
               'built_on': {'os': 'Macintosh', 'os_version': 'macOS ' + next(k for k, v in TAGS.items() if v == tag),
                            'cpu_family': 'arm', 'xcode': '', 'clt': ''}}
    (keg / 'INSTALL_RECEIPT.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return keg


def archive(directory, destination):
    with tarfile.open(destination, 'w:gz') as output:
        for path in sorted(directory.iterdir()):
            output.add(path, arcname=path.name)


def prepare(release_version, development=False):
    version(release_version)
    if platform.system() != 'Darwin' or platform.machine() != 'arm64':
        raise ValueError('Homebrew builds require native Apple Silicon.')
    tag = TAGS.get(platform.mac_ver()[0].split('.')[0])
    if tag is None:
        raise ValueError('Review bottle support for this macOS version before building.')
    commit = run('git', 'rev-parse', 'HEAD', capture=True) if development else verify_tag(release_version)
    output = ROOT / 'artifacts/homebrew' / release_version / tag
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError('Output exists; never overwrite release artifacts.')
    with tempfile.TemporaryDirectory(prefix='clone-bottle-') as temporary:
        work = Path(temporary)
        source = work / 'source'
        source.mkdir()
        for name in ['Clone.Console', 'Clone.Git', 'completions']:
            if (ROOT / name).is_symlink() or any(p.is_symlink() for p in (ROOT / name).rglob('*') if 'bin' not in p.parts and 'obj' not in p.parts):
                raise ValueError('Source snapshots must not follow symlinks.')
            shutil.copytree(ROOT / name, source / name, ignore=shutil.ignore_patterns('bin', 'obj'))
        for name in ['global.json', 'Directory.Build.props', 'Directory.Build.targets', 'LICENSE']:
            shutil.copy2(ROOT / name, source / name)
        cache = source / 'vendor/nuget'
        config = source / 'NuGet.Config'
        config.write_text('<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>\n')
        run('dotnet', 'restore', source / 'Clone.Console/Clone.Console.csproj', '-r', 'osx-arm64', '--packages', cache, '--configfile', config)
        # Audit occurs before archiving. Build restoration is offline, using these exact packages.
        (source / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>\n')
        environment = os.environ.copy()
        environment['NUGET_PACKAGES'] = str(cache)
        subprocess.run(['dotnet', 'publish', str(source / 'Clone.Console/Clone.Console.csproj'), '-c', 'Release',
                        '-r', 'osx-arm64', '-p:Version=' + release_version, '-p:NuGetAudit=false', '-o', str(work / 'publish')],
                       cwd=source, env=environment, check=True)
        binary = work / 'publish/clone'
        run('/usr/bin/lipo', binary, '-verify_arch', 'arm64')
        run('/usr/bin/codesign', '--force', '--sign', '-', binary)
        run('/usr/bin/codesign', '--verify', '--strict', binary)
        if run(binary, '--version', capture=True) != 'clone ' + release_version:
            raise ValueError('Native smoke validation failed.')
        for name in ['Clone.Console', 'Clone.Git']:
            for artifact in ('bin', 'obj'):
                shutil.rmtree(source / name / artifact, ignore_errors=True)
        shutil.rmtree(source / 'artifacts', ignore_errors=True)
        source_archive = output / f'clone-{release_version}-source.tar.gz'
        archive(source, source_archive)
        source_sha = digest(source_archive)
        payload = work / 'bottle'
        bottle_payload(payload, binary, source / 'completions/_clone', source / 'LICENSE', release_version, source_sha, tag)
        bottle = output / f'clone-{release_version}.{tag}.bottle.tar.gz'
        archive(payload, bottle)
        (output / 'clone.rb').write_text(formula(release_version, source_sha, digest(bottle), tag))
        data = {'channel': 'homebrew', 'version': release_version, 'rid': 'osx-arm64', 'bottle_tag': tag,
                'commit': commit, 'development': development, 'native_smoke': True,
                'files': {p.name: digest(p) for p in (source_archive, bottle, output / 'clone.rb')}}
        manifest = output / 'manifest.json'
        manifest.write_text(json.dumps(data, indent=2) + '\n')
        validate(manifest, production=not development)
        print(manifest)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    build = commands.add_parser('prepare')
    build.add_argument('--version', required=True, type=version)
    build.add_argument('--development', action='store_true')
    verify = commands.add_parser('verify')
    verify.add_argument('manifest', type=Path)
    verify.add_argument('--development', action='store_true')
    args = parser.parse_args()
    try:
        if args.command == 'prepare': prepare(args.version, args.development)
        else: validate(args.manifest, production=not args.development)
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'Homebrew packaging stopped: {error}\n')
