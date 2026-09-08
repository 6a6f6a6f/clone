# Apple Silicon Homebrew distribution

## Availability and scope

The first modernization release uses a project-owned Homebrew formula and
source-built ARM64 bottles. Intel is unsupported. No Apple Developer ID
certificate, notarization credential or installed .NET runtime is needed to
install a compatible bottle. The historical v0.1.0 release does not contain
these changes; no new release is published merely by merging migration PRs.

The application targets macOS 26 (Tahoe), Apple Silicon only. Local acceptance
uses macOS 26.6.2 (25G83); hosted CI uses `macos-26` and records its image build.
Older OS versions are unsupported. Bottles carry `arm64_tahoe`; never label a newer-host
build as an older OS. No macOS 14/15 acceptance is required. Record the actual
host build for each remaining acceptance check.
The project owns this source-to-bottle pipeline and its review; it is not an
official Homebrew/core package or an assertion of Homebrew maintainer review.

## Install, upgrade and remove

After a verified release and its generated `Formula/clone.rb` metadata PR are
published and merged:

```sh
brew tap 6a6f6a6f/clone https://github.com/6a6f6a6f/clone.git
brew install --formula 6a6f6a6f/clone/clone
clone --version
clone doctor
brew upgrade --formula 6a6f6a6f/clone/clone
brew reinstall --formula 6a6f6a6f/clone/clone
brew uninstall --formula 6a6f6a6f/clone/clone
```

Homebrew manages command/completion links and declares Git as a runtime
prerequisite. It installs a compatible bottle automatically. Source fallback
needs the SDK selected by `global.json` and supported Apple Command Line Tools.
The formula declares Homebrew `dotnet` as a build-only dependency; if that SDK
no longer satisfies the pinned feature band, service the formula/source inputs
before advertising source fallback. The SDK is not needed for bottled execution.

Installation does not edit shell profiles or require running Clone with sudo.
Do not overwrite unrelated command collisions with `--force`. Uninstall never
removes user configuration or repositories. Homebrew may update dependencies;
review its proposed changes. Keep an older verified bottle and formula metadata
for a reviewed rollback, without replacing immutable release assets.

## Source and bottle production

```sh
make check
make packaging-check
make bottle-dev VERSION=0.2.0
make bottle-dev VERSION=0.2.1
python3 scripts/homebrew.py verify \
  artifacts/homebrew/0.2.0/arm64_tahoe/manifest.json --development
python3 scripts/test_homebrew.py \
  artifacts/homebrew/0.2.0/arm64_tahoe/manifest.json \
  artifacts/homebrew/0.2.1/arm64_tahoe/manifest.json
```

Use the tag matching the actual host. Outputs are not overwritten. Development
manifests cannot pass production publication. Production packaging requires a
clean checkout at the matching existing version tag and the pinned SDK.

`homebrew.py` snapshots the shipping source, restores/audits its exact NuGet
build inputs into a private cache, then builds offline against that cache.
The source archive includes those packages and disables network package feeds;
its formula build disables online audit only because audit already occurred
when the checksummed source snapshot was prepared. SDK/runtime/package updates
require a new source archive, bottle and validation. No secrets or user caches
are copied into the archive.

The native executable receives an ad hoc integrity signature, which requires
no Apple certificate and does not identify an Apple-verified publisher. The
builder checks architecture, signature and native version output, then creates
Homebrew's documented keg/receipt/formula bottle layout. Downloads use immutable
version URLs and SHA-256. Source archive, bottle and generated formula are all
bound to the reviewed commit through GitHub artifact attestations at release.
The formula is regenerated and compared during manifest validation.

The lifecycle harness creates an isolated keg-only test formula, verifies
installed binary bytes, version and preview, rejects a tampered upgrade while preserving the previous executable,
upgrades/reinstalls, and removes
its own fixture. It skips dependency changes to avoid upgrading unrelated local
packages. Dependency resolution and normal command linking still need clean-host
acceptance. `--brew-test` additionally runs Homebrew's developer test command on
a host with supported Xcode/CLT. The direct native checks need no .NET on PATH.

## Publication and acceptance

The manually dispatched `release.yml` runs only the Homebrew channel. Its package job builds
from the tag and generates provenance; the publishing job requires the protected
`release-publishing` environment. That environment requires owner approval and
accepts `v*` tags. Self-review is allowed for the sole maintainer; this is an
explicit approval checkpoint, not independent two-person review.

Publication requires `.github/release-acceptance.json`, matching source/tag
identity, valid manifests and artifact attestations. It creates a draft, uploads
the complete asset set, and checks remote names, sizes and SHA-256 digests before
publication. It never replaces existing assets. A failure leaves any partial
draft unpublished for inspection. A separate PR then updates `Formula/clone.rb`
only after public release asset digests match. A maintainer opens the metadata PR using `scripts/propose_tap_update.py` and
the verified release manifest after publication. The workflow does not request
permission to create or approve PRs.
Dispatch it on the reviewed `v<version>` tag with the matching numeric version input.

Merge review and release acceptance are separate. CI/CD reactivation is
authorized for macOS 26. The release acceptance record and protected environment
still gate publication. See [CI_FREEZE.md](CI_FREEZE.md) and
[VALIDATION.md](VALIDATION.md). Do not mark clean-host, authenticated clone,
hosted-artifact or tampered-download acceptance passed from a local build alone.

## Deferred signed system package

The signed `.pkg` channel is deferred until Apple Developer ID Application and
Installer identities plus notarization credentials are available. Its status is
out of the current scope; #18/#21 were closed as not planned. Reopen them or
create a new issue if Apple-signed distribution is requested later. It does not
block Homebrew.
The optional `scripts/release.py prepare` package builder and guarded payload
remain available for future validation. They are not called by the Brew release
workflow and an unsigned development package is not a supported user download.

That package uses `/Library/Application Support/Clone`, a private
`/private/etc/paths.d/io.github.6a6f6a6f.clone` entry and receipt
`io.github.6a6f6a6f.clone`. It never overwrites Homebrew files. The uninstall helper
checks ownership and expected payload hashes and removes only its own files.
GUI/privileged install, fresh-shell discovery, interrupted-update recovery,
quarantine, signing and notarization remain unvalidated production gates.
