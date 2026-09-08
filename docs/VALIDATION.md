# Migration validation record

Recorded September 8, 2026, for the stacked migration branches. These results
describe local development artifacts. They do not authorize a production
release or CI/CD reactivation. The acceptance record remains `ready: false`.
Support is limited to Apple Silicon; Intel is excluded from the release and
acceptance scope.

## Local environment and evidence

Host: Apple Silicon, macOS 26.6.2 (25G83), .NET SDK 10.0.400, Apple Command Line
Tools. No valid Developer ID signing identity was available locally; no
repository signing secrets were provisioned.

| Check | Result | Scope and limitations |
| --- | --- | --- |
| `make check` | Passed | Format verification, Release build with zero warnings/errors, 59 managed tests and 9 packaging tests |
| `make packaging-check` | Passed | Packaging tests, ShellCheck, zsh syntax and actionlint; only the intentional constant-false freeze warning is suppressed |
| SDK servicing and NuGet audit | Passed | Microsoft metadata matched SDK 10.0.400; no known vulnerable resolved test packages were reported at review time |
| ARM64 Native AOT | Passed | Standalone help/version, configuration save/load, preview and an actual public HTTPS clone; no development dylib required |
| Architecture restrictions | Passed | MSBuild rejected `osx-x64` and an `x86_64` adapter override; packaging rejected Intel; the Apple Silicon bottle lifecycle passed |
| Native dependency inspection | Passed locally | `otool -L` on the ARM64 binary listed only Apple system libraries/frameworks; no Homebrew or development library paths |
| Development archives and packages | Passed locally | The ARM64 package built; archive modes/payload and expanded `.pkg` contents inspected; system package installation was not performed |
| Isolated Homebrew lifecycle | Passed | Install, tampered-upgrade rejection with old-version preservation, upgrade 0.2.0 to 0.2.1, reinstall and uninstall; actual native execution without .NET on PATH; dependency changes skipped; fixture keg-only |
| Historical Cask approach | Replaced | The unsigned quarantined Cask did not execute; the active channel now uses source-built Homebrew bottles |
| Homebrew provenance | Pending hosted release | The paused workflow attests source, bottle and formula; publication verifies remote digests |
| Signed system package | Deferred | Requires future Apple credentials and independent package acceptance |
| Hosted CI/CD | Paused | Repository Actions disabled; release workflow manually disabled; source workflows manual-only with unconditional false guards |

Homebrew bottle execution does not require an Apple Developer certificate.
The optional `brew test` runner was blocked by the locally installed Xcode 16.4
minimum-version check; direct installed-binary verification is recorded
separately. No Xcode installation or Gatekeeper setting was changed.

Native startup observations below use ten sequential `--version` subprocess
invocations after a warm-up. They include process launch overhead and are not
a cold-start benchmark or a release performance guarantee.

| Binary | Size | Median elapsed time | Execution |
| --- | --- | --- | --- |
| `osx-arm64` | 3,396,920 bytes | 4.20 ms | Native Apple Silicon |

## Installed-experience acceptance still required

The owner narrowed support to macOS 26 on Apple Silicon on September 8, 2026.
macOS 14 and 15 acceptance is no longer required. Local installed acceptance on
26.6.2 (25G83) is recorded in issue #22. Use the hosted macOS 26 environment for
additional Homebrew bottle coverage; record its exact OS build separately. Track the signed system-package channel separately. Record OS/build, architecture, source commit, Git version, artifact
SHA-256, signature identity, commands and outcomes without credentials.

1. Verify each downloaded bottle's checksum, provenance and ad hoc integrity
   signature. Tampered downloads must be rejected before replacing an installed
   version. Apple Developer ID identity is not part of this channel.
2. Exercise Homebrew install/upgrade/reinstall/uninstall. Confirm command discovery in a fresh
   login shell and completion setup. Check existing command collisions and normal shell command/completion links.
3. Start without Git and verify an actionable diagnostic; install an approved
   Git and run `clone doctor`. Exercise first use without saved configuration,
   then saved configuration, explicit overrides and legacy layout selection.
4. Clone public HTTPS and a controlled authenticated HTTPS/SSH repository using
   test credentials or an agent. Confirm noninteractive failures never prompt
   and diagnostics disclose no credentials. Do not record credential values.
5. Exercise spaces/Unicode, existing matching/conflicting destinations, offline
   failure, cancellation and timeout. Confirm the previous destination and
   unrelated files survive failures and staging cleanup stays confined.
6. Interrupt an upgrade in a disposable environment. Confirm the previous
   installed version remains usable or document and resolve the recovery
   defect before acceptance. Verify failed hashes/signatures do not replace it.
7. Confirm upgrade, rollback and uninstall preserve user configuration and
   cloned repositories. Verify unrelated paths and symlink collisions are not overwritten.

Attach the results to issue #22, then review every entry in
`.github/release-acceptance.json`. Keep pending entries pending until the actual
platform/channel evidence exists. Publishing code, executing development tests,
or obtaining a successful cross-build does not satisfy these gates.
