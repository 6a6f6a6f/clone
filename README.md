# Clone

A small macOS command-line tool that keeps Git checkouts organized. Clone is
written in C# and distributed as a Native AOT executable: an installed .NET
runtime is not required. Git is required.

The .NET 10 migration is merged. CI validates pushes and pull requests on macOS 26; **the old v0.1.0
release does not contain these fixes**. New distribution artifacts must pass the
[release gates](docs/CI_FREEZE.md) before publication. See the
[modernization tracker](https://github.com/6a6f6a6f/clone/issues/1).

## Installation

See [macOS distribution](docs/MACOS_DISTRIBUTION.md) for the Homebrew formula/bottle channel and
deferred signed package channel, update/uninstall commands, and their current acceptance
gates. The migration has not published a new modernization release yet.

## Quickstart

```sh
clone --help
clone https://github.com/owner/repository.git
clone git@github.com:owner/repository.git
clone --dry-run ssh://git@example.com/team/subgroup/repository.git
```

The default destination is `~/Projects/host/namespace/repository`. Missing
roots are created on the first clone. No shell profile changes are required.
HTTPS and SSH with DNS hosts or SSH aliases are supported, including nested
namespaces and explicit ports. IPv6 literals and local paths are not currently
supported. For authenticated repositories, configure Git/SSH credentials first;
never embed tokens in URLs or turn off TLS/SSH verification.

## Configuration

```sh
clone config set-root "$HOME/Work Projects"
clone config set-layout host
clone config set-timeout 600
clone config show
clone doctor
```

Settings live in `~/Library/Application Support/clone/config.json`.
`--config-dir` selects another configuration directory for isolated usage.
The root is chosen in this order: `--root`, `CLONE_PROJECT_FOLDER`, saved
configuration, then `~/Projects`. An explicit `--layout` wins over saved layout.
When `CLONE_PROJECT_FOLDER` is present and no layout is selected, Clone preserves
the legacy `root/namespace/repository` arrangement. It never moves existing
clones automatically. Use `--dry-run --layout host` to preview a new layout.

Use `clone config set-git /absolute/path/to/git` for an explicit installation,
`clone config set-timeout none` to remove a deadline, or `clone config reset`
to reset saved settings (including malformed configuration). These commands
never delete cloned repositories. Symlink configuration directories remain
rejected even during reset.

Project and config directories must be privately writable by their owner.
Symlink ancestors and shared writable roots are rejected. On macOS use
`/private/tmp` rather than the `/tmp` symlink when selecting a temporary root.
See the precise [security model](docs/SECURITY_MODEL.md).

## Automation and recovery

```sh
destination="$(clone --quiet --non-interactive --timeout 600 \
  https://github.com/owner/repository.git)" || exit "$?"
printf '%s\n' "$destination"
```

Only a successful destination is written to stdout. Progress and errors go to
stderr, without ANSI decorations (including redirected output and `NO_COLOR`
environments). `--quiet` suppresses progress. Ctrl-C stops the owned Git process
and cleans its staging files. A failed clone never replaces an existing clone.
There is no default timeout; unattended jobs should specify one.

| Exit code | Meaning |
| --- | --- |
| 0 | Success, help, version, or preview |
| 1 | Git, configuration, platform, or storage error |
| 2 | Invalid usage or input |
| 124 | Configured timeout expired |
| 130 | Canceled |

If a destination exists, Clone reports its path and, when readable, whether its
origin matches. It does not pull, reset, or overwrite it. Missing Git or Command
Line Tools is an actionable setup error; Clone does not install prerequisites
silently. `doctor` checks local setup without network or credential access.

## Build and test

Install Apple's Command Line Tools and the SDK from `global.json`.

```sh
make build
make test
make check
make publish RID=osx-arm64
artifacts/publish/osx-arm64/clone --version
```

The maintained source targets Apple Silicon Macs running macOS 26 (Tahoe)
(`osx-arm64`). Intel Macs are not supported. Local acceptance uses macOS 26.6.2 (25G83); hosted CI records its own macOS 26 image build.
Older macOS versions and Intel Macs are outside the support scope. The C interop adapter is compiled
with Clang and linked statically into Native AOT; its development dylib is only
used by managed builds/tests. No third-party runtime NuGet package is required.

For zsh completion, place `completions/_clone` in a directory on `$fpath` and
initialize completion with `autoload -Uz compinit && compinit`. Packaging
installs this file into its documented completion location.

## Project information

- [Security model](docs/SECURITY_MODEL.md)
- [Migration plan](docs/MODERNIZATION_PLAN.md)
- [CI/CD pause](docs/CI_FREEZE.md)
- [Original Portuguese introduction (historical)](docs/README.pt-BR.md)

Licensed under the WTFPL, as originally stated by this project. See [LICENSE](LICENSE).
