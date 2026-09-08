# Maintenance and servicing

Run `python3 scripts/check_sdk.py` to compare the pinned SDK with Microsoft's
bounded HTTPS .NET 10 release metadata. CI detected the September 8, 2026 security servicing release; the current
pin is SDK 10.0.401. A failed lookup is not a passing servicing check. Review SDK updates
alongside runtime support policy and rerun native validation after updating.

GitHub Actions are pinned to upstream commit SHAs, with their reviewed release
tags as comments. Dependabot proposes weekly Actions and NuGet updates, with at most five open
PRs per ecosystem. Updates must pass CI and maintainer review; they are not
automatically merged. For action updates, inspect the upstream revision and any
transitive action pins, add only the reviewed exact SHAs to the repository
allowlist, then rerun CI. Remove obsolete entries after the update is merged.
Never enable arbitrary actions to unblock an update. SDK servicing is checked
on every CI run; pin the reviewed official SDK update and rerun native builds.

NuGet auditing covers direct and transitive packages, and restore warnings are
errors. Run `dotnet list Clone.Tests/Clone.Tests.csproj package --vulnerable
--include-transitive` for the detailed report. The app has no external runtime
NuGet package; MSTest and the Microsoft test SDK are development dependencies.
The Native AOT runtime and local C adapter still belong in the build inventory.

Git is an external runtime dependency. Review Git security advisories and
update Homebrew Git with `brew upgrade git`, or service Apple Git through the
supported Apple Command Line Tools/Xcode update route. Clone does not silently
replace Git or weaken its authentication checks to work around failures.

Before each release, review authentication and transport behavior, destination
ownership, diagnostics, cancellation/resource bounds, package integrity, and
installer/uninstaller ownership. Preserve the published artifacts and their
provenance; never overwrite a tag or binary to hide a regression.

The repository maintainer owns dependency triage: assess new advisories, record
affected shipping versions in an issue or private security advisory as
appropriate, upgrade or mitigate the affected input, and rerun the relevant
checks before publishing a replacement version. Disable promotion of affected
artifacts while a release-blocking vulnerability is unresolved.

The initial native size/startup measurements and dependency inspection are
recorded in [VALIDATION.md](VALIDATION.md). Compare equivalent hardware and
build settings before treating those measurements as performance regressions.
