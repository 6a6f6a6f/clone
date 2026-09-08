# macOS installation and release operations

## Availability

The migration artifacts are under review. The historical v0.1.0 release does
not contain these changes. Unsigned files with `-development` in their names
are local validation artifacts and cannot pass the production release gate.
Do not distribute them as signed/notarized releases.

The target is Apple Silicon Macs running macOS 14 or later (`osx-arm64`).
Intel Macs are not supported. Build checks do not replace acceptance on the
minimum OS. The authoritative outstanding matrix is
[release-acceptance.json](../.github/release-acceptance.json).

## Homebrew channel

After a signed release is published and its generated Cask metadata PR is
merged, this repository itself is the project-owned tap:

```sh
brew tap 6a6f6a6f/clone https://github.com/6a6f6a6f/clone.git
brew install --cask 6a6f6a6f/clone/clone
clone --version
clone doctor
brew upgrade --cask 6a6f6a6f/clone/clone
brew reinstall --cask 6a6f6a6f/clone/clone
brew uninstall --cask 6a6f6a6f/clone/clone
```

The Cask selects a versioned native archive and a fixed SHA-256 for Apple Silicon,
links the CLI and zsh completion, requires Apple Silicon, and declares Git as a dependency. It does not
compile Clone or require the .NET SDK. Cask delivery was selected because a
formula installation unnecessarily entered Homebrew's source-build toolchain
checks on the reviewed host. It uses Homebrew's normal quarantine handling.

Uninstall removes only Homebrew-managed application files and links. No `zap`
stanza removes configuration or repositories. Existing unrelated commands
must not be overwritten with `--force`; resolve an installation collision
explicitly. Homebrew owns its own install/upgrade rollback behavior; test failed
upgrades in the release acceptance environment as well as successful upgrades.

## Standalone package channel

Choose the Apple Silicon `.pkg` from a validated release. Verify its
published SHA-256, GitHub provenance, and Developer ID signature before use.
Open the package in Installer, or run:

```sh
sudo installer -pkg ./clone-VERSION-osx-arm64.pkg -target /
```

Replace `VERSION` with the exact release version. The installer rejects other architectures.
The package installs into `/Library/Application Support/Clone`, with package
identifier `io.github.6a6f6a6f.clone`. A file under `/private/etc/paths.d` makes
its `bin` directory visible to a new login shell. Normal cloning requires no
administrator privileges. Start a new Terminal session after installation.

To enable the package's zsh completion, add its completion directory to `$fpath`
in your own shell configuration before calling `compinit`:

```sh
fpath=("/Library/Application Support/Clone/share/zsh/site-functions" $fpath)
autoload -Uz compinit && compinit
```

The package never edits shell profiles, overwrites Homebrew-owned files, or
performs privileged network downloads. Install a newer signed package for an
update, or an earlier verified package for an explicit rollback. Both preserve
user configuration and cloned repositories. If both channels are installed,
normal PATH order decides which CLI runs; use `command -v clone` to inspect it.

Remove the package with its guarded helper:

```sh
sudo "/Library/Application Support/Clone/uninstall.sh"
```

The helper requires the package receipt, private root-owned directories, the
fixed expected payload, and matching installed-file hashes before deletion.
Unexpected or modified files cause a stop for manual inspection. Unknown extra
files are preserved, and nonempty directories are not recursively deleted.
The helper never deletes `~/Projects` or the user's configuration. Missing Git
must be resolved separately using an approved Git/Command Line Tools install.

## Local artifact creation

```sh
make check
make packaging-check
python3 scripts/check_sdk.py
make package-dev RID=osx-arm64 VERSION=0.2.0
python3 scripts/release.py verify \
  artifacts/release/0.2.0/osx-arm64/manifest.json --development
```

Outputs are never overwritten. Preserve or explicitly remove an earlier local
output before reusing its version. All packaging requires a native Apple Silicon host. No local development
command creates a Git tag, signs with an invented identity, or publishes assets.

The package builder validates the version, architecture, native version output,
asset set, checksums, and manifest types. Test archive extraction and package
payload inspection separately from actual Installer execution on a clean VM.
Package installation, interrupted-update recovery, fresh-shell PATH discovery,
and privileged uninstall require the clean-machine acceptance gate.

## Production signing and publication

Production packaging requires a clean checkout at the matching existing tag,
Developer ID Application and Installer identities, and a `notarytool` keychain
profile. `CLONE_APP_IDENTITY`, `CLONE_INSTALLER_IDENTITY`, and
`CLONE_NOTARY_PROFILE` select them. `CLONE_SIGNING_KEYCHAIN` optionally selects
an isolated keychain without modifying the user's keychain search list.

The paused release workflow references `release-signing` and
`release-publishing` environments. Both were configured on September 8, 2026
with `6a6f6a6f` as required reviewer and a `v*` tag-only deployment policy.
Self-review remains allowed for the sole maintainer; this is an explicit
approval checkpoint, not independent two-person review. Recheck these settings
before reactivation and configure the following secrets through GitHub's
secret settings:

- `APPLE_APP_CERT_BASE64`, `APPLE_INSTALLER_CERT_BASE64`, `APPLE_CERT_PASSWORD`
- `APPLE_NOTARY_KEY_BASE64`, `APPLE_NOTARY_KEY_ID`, `APPLE_NOTARY_ISSUER_ID`
- `APPLE_APP_IDENTITY`, `APPLE_INSTALLER_IDENTITY`

Never paste those values into an issue or log. `signing_keychain.py` uses an
isolated ephemeral keychain, suppresses credential-bearing subprocess output,
and has an always-run cleanup step. Review certificate expiry and permissions.

The workflow builds and tests on Apple Silicon, signs the executable and
package, submits a ZIP of the signed executable and the package to Apple,
staples the package, and verifies the final files. The public tar archive
contains that same signed executable; a bare executable/tar archive cannot be
stapled like a package. Test the real downloaded/quarantined artifact.

Build jobs produce provenance. Publication verifies artifact hashes and GitHub
attestations for this repository, workflow and source commit. It creates a draft,
uploads the entire asset set without `--clobber`, verifies the remote inventory,
and only then publishes. A partial draft stays unpublished for inspection;
retries do not silently replace existing assets.

The publication workflow then proposes `Casks/clone.rb` through a separate PR,
using only a public release whose server-side digests match the validated
manifests. No tap metadata or release is published while migration gates remain
outstanding. The generated metadata file is also attached to the release.
The tap proposal requires GitHub Actions to be permitted to create PRs; configure
that separately at reactivation, rather than broadening permissions mid-migration.

## Release acceptance

Record evidence for every check in `release-acceptance.json` and set a reviewed
source commit only after the results are assessed. `check_release_gate.py`
rejects incomplete evidence and shipping/validation changes after that commit.
A review-only change to the acceptance record may follow the tested commit.
Do not claim clean-machine, authenticated or notarized results from managed
tests or development packages.
See [VALIDATION.md](VALIDATION.md) for the local evidence and the remaining
installed-experience procedure.

Keep repository Actions disabled, workflows manual-only and false-guarded,
and Dependabot version-update PR limits at zero until the migration is ready.
See [CI_FREEZE.md](CI_FREEZE.md). Reactivation is separate from code publication.

For a local Homebrew lifecycle fixture, first build two distinct development
versions for the current architecture, then run:

```sh
python3 scripts/test_homebrew.py --from-version 0.2.0 --to-version 0.2.1
```

The test refuses existing fixtures, uses separate command/completion names,
checks exact installed payloads across install/upgrade/reinstall, then removes
the Cask and tap. It preserves quarantine and does not execute the quarantined
unsigned fixture. Native execution after installation remains a signed-release
acceptance test, not a reason to remove platform protections.
