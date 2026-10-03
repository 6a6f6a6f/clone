# Security model

Clone supports normal-user operation on Apple Silicon Macs with macOS 14 or later. It is not a sandbox
for a compromised user account, an explicitly selected malicious Git binary,
or trusted user Git/SSH configuration. Running as root is rejected. Intel Macs are not supported.
Minimum-OS runtime acceptance must be completed before advertising release
support; source/build support alone is insufficient.

## Inputs and remote execution

Only HTTPS and SSH remotes with DNS names or SSH aliases and an owner/namespace
are accepted. IPv6 literals, local paths, HTTP, git://, helpers, credentials in
HTTPS URLs, query strings and fragments are not supported. Remote names are
bounded, normalized to NFC, and reject encoded separators, dot segments and
control characters. Git receives separate arguments and an option terminator.

Git is resolved from absolute PATH entries in order or an explicit absolute
--git path. The invocation directory is never implicitly searched. Git/SSH
credential helpers and SSH configuration remain user trust inputs. Transport
restrictions are also enforced through GIT_ALLOW_PROTOCOL after URL rewriting.
Inherited repository-location and injected command-configuration environment
variables are removed. TLS and host-key verification are never disabled.
Hooks and templates are disabled for the clone invocation; no commands from a
new checkout are launched. User-configured filters/helpers can still run as
part of Git's documented configuration model.

In noninteractive mode, Git and SSH askpass executables are replaced with
`/usr/bin/false`, credential-manager prompts are disabled, and SSH runs
with BatchMode=yes. A custom GIT_SSH_COMMAND is retained with that option
appended; wrappers must support OpenSSH options. Otherwise normal SSH user
configuration and agents remain available. A finite --timeout is recommended
for unattended jobs; there is no arbitrary default deadline for large clones.

## Filesystem ownership

The project/config roots must be owned by the invoking user. Ancestors must be
owned by that user or root and must not be group/other writable, except trusted
root-owned sticky temporary directories. Symlink path components are rejected,
including /tmp and /var aliases: use their physical /private/... paths when
applicable. Existing symlinks or dangling links at the destination are conflicts.

Directory traversal, creation, configuration writes, cleanup and publication
use descriptors, no-follow opens and *at operations. Clone staging is private
(mode 0700), on the same filesystem as the destination. Publication uses
renameatx_np with RENAME_EXCL and never overwrites an existing entry. Parent
and staging locations are checked again before publication. Cleanup unlinks
entries relative to held directory descriptors, never follows repository
symlinks, and only descends into the invocation's exclusive staging directory.
Empty namespace directories may remain after failure. Cleanup failures leave
an explicit warning rather than expanding deletion scope.

Git requires a pathname for its working directory. Protection against a process
running as the same user maliciously relocating private ancestors during Git
execution is outside this local trust model. Shared writable roots are rejected
rather than claiming pathname-based Git execution is safe there. Concurrent
ordinary clones are supported by exclusive staging and no-replace publication.

The C interop adapter is built from local source with warnings as errors. It
keeps Darwin variadic calls and stat/dirent layout handling in C. Managed tests
use a development dylib; Native AOT links the same adapter statically, so the
released CLI does not require that development library or an installed .NET.

## Diagnostics and validation

Control sequences, URL credentials and recognized secret parameters are
redacted before output or retention. Lines longer than 8192 characters are
omitted, and only the last 16 diagnostic lines are retained. There is no
telemetry, credential database, remote code execution feature, or automatic
updater. Arbitrary server text is not a safe place to place secrets even with
redaction; report reproducible leaks using synthetic values only.

Regression tests use Microsoft-maintained MSTest (MIT) and the Microsoft test
SDK. No runtime application package was added. Restore auditing checks the
resolved dependency graph; packages and the Native AOT runtime still require
servicing. Tests deliberately enabling local transport do so in explicit test
Git wrappers, never by weakening the production policy.

## Stalled diagnostic consumers

Git pipe readers never call external output code while holding the diagnostic
history lock. They sanitize and bound text before retaining the last 16 lines
or submitting best-effort output to a 64-message channel. Saturation drops the
oldest queued messages and coalesces notification into a fixed omission message.
Queued messages are capped at 8,192 UTF-16 characters after sanitization, as
well as the existing input-line cap. Queue text payload is at most 1 MiB per
delivery session, excluding object overhead and the worker stack; history adds at most 256 KiB and
one in-flight message adds at most 16 KiB. Reader buffers and sanitization
intermediates are also bounded by the input-line limit. No dependency was added.

A single background thread invokes each session's synchronous sink. After Git
exits or is killed and reaped, pipe readers have a two-second shutdown grace;
output delivery has a separate 250-millisecond grace. These limits bound output
teardown, not arbitrary kernel process-reaping or filesystem operations. Timeout
and caller cancellation retain their distinct results. A callback failure
observed before return stops Git and is reported without exposing exception data.
Late callback exceptions are caught by the worker.

The CLI shares Runner's delivery session for progress, cleanup warnings, and
terminal stdout/stderr messages, so final writes cannot reintroduce an unbounded
wait. Messages may be omitted when a sink is stalled; timeout/cancellation exit
codes and staging cleanup do not require successful delivery. Configuration,
help, and doctor use the same writer adapters for a consistent bounded lifecycle.
Output failures stop a running Git command when observed by Runner. The CLI
checks for worker failures after its bounded delivery grace and changes an
otherwise successful exit to code 1. Timeout/cancellation and other existing
failure codes retain precedence. A writer failure does not undo a checkout
already published successfully. Failures arriving after abandonment cannot
change the returned status; stalled writers still allow bounded completion.
Only errors signaled by the supplied writer are observable; a writer that
silently ignores an OS output error provides no failure signal.

Cancellation cannot interrupt an arbitrary synchronous callback already inside
a blocked write. The background worker and its bounded state can outlive the
operation until that write returns. After abandonment it delivers no remaining
queued messages; an in-flight write may still finish. Caller-owned writers must
therefore tolerate an outstanding write after return. Repeated library calls
with permanently blocked callbacks can accumulate background workers; this
change does not claim full reclamation of arbitrary hostile callback code.
Filesystem ownership, symlink resistance, and publication checks are unchanged.
