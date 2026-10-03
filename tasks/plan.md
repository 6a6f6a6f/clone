# Implementation Plan: Bound Git Diagnostic Delivery and Teardown

## Objective and confirmed cause

Remote-controlled Git diagnostics must not prevent process timeout/cancellation, staging cleanup, or CLI termination when a downstream output consumer stays open but stops draining. Runner currently invokes the synchronous output callback under the pipe-reader diagnostics lock and then unconditionally awaits both readers. Canceling reads cannot interrupt a callback already blocked in TextWriter.WriteLine.

Current evidence: Clone.Git/Runner.cs (Receive, ReadOutputAsync, teardown finally), Clone.Git/CloneService.cs (staging finally), and Clone.Console/CliApplication.cs (progress callback and terminal error writes). Existing tests cover throwing callbacks, bounded diagnostic history, timeout process-tree termination, and cancellation classification, but not stalled consumers.

## Architecture decisions

- Preserve RunAsync's public callback signature and existing timeout/cancellation distinctions. Use System.Threading.Channels from the existing runtime; introduce no package dependency.
- Pipe readers sanitize text, update the bounded diagnostic history under a short lock, and enqueue output without waiting for delivery. Never call external code while holding this lock. Diagnostic history remains the last 16 sanitized lines independently of delivery drops.
- Use a bounded channel with multiple producers and one consumer. Initial named policies: 64 queued messages, existing 8,192-character input-line limit, existing 1,024-character read buffer, existing 2-second reader shutdown grace, and a 250-millisecond output shutdown grace. Document units, rationale, and the derived memory bound, including sanitizer expansion and the in-flight message.
- On saturation, drop the oldest queued diagnostic to retain recent context. Count dropped messages using bounded/saturating state and coalesce notification into a fixed message when delivery resumes. Do not enqueue a marker for every drop or include untrusted text in it. This is best-effort progress; queued messages retain their stdout/stderr origin and enqueue order. Cross-stream chronological ordering is not guaranteed.
- Run exactly one delivery worker per invocation on a dedicated background thread, so a synchronous callback cannot occupy a pipe reader, block RunAsync before its first asynchronous yield, or exhaust the shared thread pool. A token stops future callbacks but cannot preempt an in-flight callback. Never spawn one task/thread per message or use Thread.Abort.
- On normal exit, reader failure, callback failure, timeout, or cancellation: stop/reap Git as required, finish/cancel readers within their grace, complete the queue, and bound delivery completion by its independent grace. At expiry, stop accepting output, discard pending messages, and detach the in-flight worker. Observe late faults and make detached state independent of disposed process/readers/token sources. Snapshot diagnostics under the history lock.
- Preserve existing behavior for callback exceptions observed before shutdown: terminate/reap Git and report a sanitized IOException. Caller cancellation takes precedence over timeout; output drops alone do not change Git's exit status. Late callback faults after bounded detachment cannot change an already returned result.
- CLI clone progress and its terminal messages must use one serialized bounded delivery lifecycle; otherwise a final WriteLineAsync can block behind the detached WriteLine and recreate the hang after staging cleanup. Cover clone preamble, cleanup warning, timeout/cancellation/failure messages, and success output with finite best-effort completion. Stop delivery after abandonment; never start a second writer against the stalled sink. Keep exit codes 124 and 130 independent of diagnostic delivery. Bound stdout completion as well so a successfully cleaned operation cannot hang publishing its destination text.
- Keep this scope on the clone execution path. Configuration/help/doctor also use the same CLI writer adapters to keep one output lifecycle and avoid a separate error path. Avoid nested workers and queues: internal method-group callbacks identify a shared OutputDelivery session; CLI or CloneService owns its completion, while standalone Runner owns its own session.

## Threat model and limitations

An attacker controls diagnostic volume/content; a stalled local consumer supplies backpressure. Protect availability through finite buffers, finite teardown, nonblocking producers, and bounded line parsing. Preserve control-character removal and credential redaction before history/queue storage. Output delivery has no authority to publish a checkout or weaken filesystem ownership controls.

A synchronous arbitrary callback cannot be forcibly canceled safely in-process. One in-flight background worker may survive until its sink resumes, retaining its callback and a bounded message. Tests must release synthetic sinks in finally blocks and verify that delivery does not resume queued work after abandonment. The CLI's single-operation lifecycle bounds this exposure; repeated library calls with permanently blocked callbacks can accumulate workers. Document this library limitation explicitly rather than claiming complete worker reclamation. If repeated-call resource containment is required, revisit the callback contract or isolate delivery in a subprocess before declaring that stronger guarantee.

OS process termination/reaping and filesystem cleanup retain their existing contracts; a diagnostic grace deadline is not a guarantee of a fixed total runtime for arbitrary kernel/filesystem failures. Define regression deadlines relative to successful Git reaping, with scheduling tolerance.

## Ordered work and checkpoints

Tasks and acceptance criteria are tracked in tasks/todo.md. Sequence: reproduce stalled output; implement bounded Runner delivery; integrate bounded CLI completion; validate native subprocess behavior and document security contracts. Implementation and validation evidence are recorded in docs/VALIDATION.md.

## Validation strategy

Use explicit synchronization to prove a callback has entered its blocked write before triggering cancellation; avoid sleep-only races. Each harness has an outer deadline and a finally path that releases its sink and terminates child processes.

Test timeout and caller cancellation separately, asserting exception/exit classification, Git and child termination, Runner return while the consumer remains open and blocked, no publication, and no .clone-staging-* residue. Include normal Git exit with a blocked callback, throwing callbacks, late exceptions after detachment, inherited open pipes, saturation, concurrent stdout/stderr, oversized lines, and sanitization/history preservation.

Run a macOS subprocess regression with the built CLI's stderr connected to a real pipe whose consumer stays alive without reading after initial setup. Emit enough fake-Git diagnostics to fill the pipe, and validate timeout plus SIGINT separately. Assert the CLI exits with 124/130 before releasing the consumer and staging is removed. Exercise stdout blockage on success separately. Use isolated fixtures and no external remote/network.

Run focused tests after each slice; final gates: make check and Native AOT publish for osx-arm64. Record managed, subprocess, and native evidence separately. Security changes must be explicit in commit/PR descriptions.
