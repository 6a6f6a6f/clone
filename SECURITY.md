# Security policy

## Supported versions

Security fixes target the latest published 1.x release on macOS 26 (Tahoe),
Apple Silicon. Upgrade to the latest patch release before reporting an issue.
Historical 0.x releases, Intel Macs and older operating systems are unsupported.
The main branch is development code, not a supported release channel.

Homebrew is the supported installer channel. Apple-signed `.pkg` distribution
is outside the current scope. An ad hoc binary signature checks integrity; it
does not provide Apple Developer ID publisher identity.

## Report a vulnerability privately

Use [GitHub private vulnerability reporting](https://github.com/6a6f6a6f/clone/security/advisories/new).
Do not publish exploitable details, credentials or private repository contents
in a public issue. For ordinary bugs and feature requests, use the issue forms.

Include the affected release/commit, macOS version/build, architecture, install
method, impact and the smallest safe reproduction. Use synthetic repositories,
paths and credentials. Redact tokens, private hostnames and personal data from
logs. Reports involving argument/path injection, symlink or ownership bypass,
credential disclosure, process cleanup or release integrity are in scope.

The maintainer will investigate privately, coordinate a fix and disclosure with
the reporter, and publish an advisory when appropriate. Response times are not
guaranteed. Avoid testing against other people's repositories, accounts or
systems, or damaging data to demonstrate an issue.

## Trust boundaries and safe operation

Clone delegates authentication to Git/SSH and does not store credentials.
User-configured Git helpers, SSH configuration and executable overrides remain
trusted local configuration. The tool does not sandbox a compromised user
account or malicious Git installation. Keep Git, macOS and your credential
helpers updated. See the [security model](docs/SECURITY_MODEL.md).

Never embed credentials in clone URLs, run normal cloning with sudo, disable
TLS/SSH host verification or bypass Gatekeeper to make a download work. Use
versioned Homebrew release artifacts and retain checksum/provenance verification.
Dependency and action updates require CI and maintainer review; publication
uses a protected environment and immutable artifacts.
