# Tasks: Bound Git Diagnostic Delivery and Teardown

## Task 1: Reproduce stalled output safely

Add synchronized blocked-callback and staging-cleanup regressions in Clone.Tests/CoreTests.cs; reuse fixtures and release blocked sinks in finally.

- [x] Prove callback entry before timeout/cancellation; operation has an outer test deadline.
- [x] Assert Git termination, correct exception, and staging removal while sink remains blocked.
- [x] Verify regressions fail against the original Runner without hanging the test host.

Verification: focused RunnerTests and StorageTests via dotnet test Clone.sln -c Release --filter 'FullyQualifiedName~RunnerTests|FullyQualifiedName~StorageTests'. Dependencies: none. Scope: small, 1-2 files.

## Task 2: Decouple Runner delivery

Implement the bounded channel, isolated delivery worker, named limits, bounded shutdown, and safe detached-worker lifetime in Clone.Git/Runner.cs and focused tests. Use a small internal helper only if ownership is clearer.

- [x] Neither reader waits on a callback or queue capacity; history and queued text remain bounded and sanitized.
- [x] Timeout, cancellation, normal exit, and observed callback failure return after the documented teardown grace; late faults are observed safely.
- [x] Saturation/drop behavior, recent history, inherited pipes, and callback failure semantics are covered.

Verification: focused Runner tests and Release build. Dependencies: Task 1. Scope: medium, 2-4 files.

## Checkpoint: Runner

- [x] Focused tests pass; review locking, process lifecycle, detached state, and calculated payload and tested queue bounds against the threat model.

## Task 3: Bound CLI completion and cleanup warnings

Integrate a single serialized output lifecycle in Clone.Console/CliApplication.cs and Clone.Git/CloneService.cs where needed; document callback ownership and cover CLI behavior in Clone.Tests/CliTests.cs.

- [x] Terminal errors and cleanup warnings cannot reblock the caller after Runner abandons delivery.
- [x] Timeout/cancellation returns 124/130 with staging removed while stderr remains stalled; success stdout delivery is also bounded.
- [x] Responsive sinks retain expected messages; no concurrent writes or queued writes start after abandonment.

Verification: focused CLI/storage tests and Release build. Dependencies: Task 2. Scope: medium, 3-5 files.

## Task 4: Prove real-pipe behavior and document the contract

Add an isolated macOS subprocess harness using existing test/validation conventions, and update docs/SECURITY_MODEL.md and docs/VALIDATION.md.

- [x] Real stalled stderr pipe tests prove timeout and SIGINT exit, child termination, and staging cleanup before consumer release; stalled success stdout is covered.
- [x] Document drop policy, finite grace, best-effort delivery, and the uninterruptible in-flight callback/repeated-library-call limitation.
- [x] make check and osx-arm64 Native AOT publish pass; distinguish managed and native subprocess evidence.

Verification: subprocess harness, make check, make publish RID=osx-arm64. Dependencies: Task 3. Scope: medium, 3-5 files.

## Checkpoint: Complete

- [x] Review denial-of-service, sanitization, ownership/publication, memory bounds, late exceptions, and detached-worker resource implications.
- [x] All acceptance criteria have evidence; security change is explicitly described for review.
