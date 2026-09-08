# Reporting security issues

Report vulnerabilities privately through the repository's GitHub security
advisory channel when available. If private reporting is unavailable, contact
the maintainer through their GitHub profile to arrange a private channel before
sharing exploit details. Do not include real credentials, personal data, or
sensitive repository URLs in reports or public issues.

Provide the Clone version, macOS version and architecture, expected and actual
behavior, and a minimal reproduction using synthetic data. See
[the security model](docs/SECURITY_MODEL.md) for supported trust boundaries.

The migration branch addresses known unsafe behavior in the historical
v0.1.0 release. No updated signed release is available until the migration's
acceptance gates are satisfied. Git, the .NET build toolchain, native platform
libraries, and developer/test packages all require ongoing security updates.
