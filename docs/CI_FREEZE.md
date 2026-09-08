# CI/CD migration freeze

GitHub Actions was disabled repository-wide on September 8, 2026. The existing
release workflow is also `disabled_manually`. No runs were active at the pause.
The previous repository setting was `enabled: true`, `allowed_actions: all`,
`sha_pinning_required: false`.

All migration PRs use local validation. Keep new workflows manual-only with an
unconditional false job guard. The owner authorized review and sequential
merges of the migration PRs into main before activation. Mark a reviewed PR
ready and integrate it after its local checks pass; keep release acceptance
separate. Do not trigger workflows, create a release tag, publish a release,
or enable Actions as an incidental merge or validation step.

After the migration has been reviewed and the documented release gates pass,
reactivation is a separate explicit operation: restore repository Actions
permissions with SHA pinning, enable the workflows, remove the false guards,
and add reviewed PR/tag triggers. Verify the exact commit before dispatching.
Until then, report hosted validation as deferred, never as passing.
