# CI/CD activation and release controls

The migration freeze ended with the owner's explicit authorization on September
8, 2026, after PRs #24–#27 merged and local Homebrew acceptance passed. The owner
removed macOS 14 from the support and acceptance scope. Support is now Apple
Silicon on macOS 26 (Tahoe); the local acceptance baseline is 26.6.2 (25G83).

CI runs on pushes to main, pull requests targeting main and manual dispatches.
The GitHub-hosted `macos-26` ARM64 runner records its actual OS version/build;
GitHub controls image updates, so it is not an exact pin to the local Mac build.
CI verifies formatting, builds, tests, packaging checks, Native AOT execution
and the installed Homebrew lifecycle. PR jobs have read-only repository access.
Actions use immutable SHA pins, with repository-level SHA pinning enabled.

Release automation is enabled but remains manually dispatched on a reviewed version tag.
The selected workflow ref must equal `v` followed by the version input. The acceptance record must pass
before packaging, provenance generation or publication. The protected
`release-publishing` environment still requires owner review. Enabling CI does
not mark pending authentication or final-artifact acceptance as passed and does
not create a release automatically. Apple certificates remain unnecessary for
the Homebrew channel; signed system packages are deferred separately.

Dependabot update proposals remain separately paused until maintenance
automation is enabled. To stop automation again, disable repository Actions
and both workflows; no source rollback or artifact replacement is necessary.
