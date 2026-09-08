# Migration merge review

The owner authorized sequential integration of PRs #24, #25, #26 and #27 into
main before any CI/CD activation or modernization release. This record covers
code review and local checks, not independent approval or hosted CI results.

## Scope reviewed

- **#24:** SDK/framework consistency, restore auditing, removal of obsolete
  formatting configuration, and preservation of the workflow freeze. The
  isolated baseline passed format/build checks; it predates the regression suite.
- **#25:** input parsing and argument boundaries, executable resolution,
  subprocess failure/cancellation, output bounds/redaction, filesystem ownership,
  no-follow traversal, exclusive publication and owned staging cleanup. The
  isolated suite passed 46 tests after correcting a startup-sensitive timeout
  fixture from 300 ms to 5 seconds. Trusted user Git/SSH configuration remains
  an explicit boundary; hostile shared writable roots are rejected.
- **#26:** help/version independence, configuration validation and precedence,
  legacy layout compatibility, diagnostic/output contracts, completion and
  noninteractive askpass suppression. Its regression suite has 59 tests; the
  core timeout-fixture correction is included before integration.
- **#27:** ARM64-only source/bottle production, offline audited build inputs,
  checksums and ad hoc signatures, constrained metadata generation, complete
  draft publication with remote digest verification, privilege boundaries and
  manual/false workflow guards. Apple signing/package delivery is deferred
  separately from the Homebrew channel.

The local Brew bottle fixture passed install and native execution, rejected a
tampered upgrade while retaining a usable previous version, then passed upgrade,
reinstall and uninstall. It uses a keg-only name and omits Git dependency changes
to avoid changing unrelated installations. Normal command links/dependency
resolution still need clean-host acceptance. The Brew developer test runner
was blocked by the installed Xcode 16.4; equivalent direct version/preview
checks passed without .NET on PATH. Xcode/Gatekeeper were not modified.

## Integration procedure

Merge #24 first. Retarget the next PR to main, incorporate the latest base with
a signed merge commit, inspect the resulting diff and validate relevant checks
before each merge. Preserve signed implementation commits with merge commits.
Do not enable Actions, dispatch a workflow, create a release tag or publish an
artifact as a side effect of integration.

## Remaining release gates

The Homebrew acceptance record remains pending for the full clean-host and
minimum-OS matrix, controlled authenticated cloning and hosted provenance.
The local fixture is useful evidence, not a substitute for every platform gate.
The signed system package remains deferred until Apple credentials and its own
acceptance are available. These release gates do not block reviewed source
integration under the owner's explicit instruction.
