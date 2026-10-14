# Security policy

## Reporting the affected build

Tanukarr is independent of upstream Sonarr and the credited Sonarr Pro forks.
Report Tanukarr vulnerabilities to the maintainer of
[KvasExpert/Tanukarr](https://github.com/KvasExpert/Tanukarr).
Private vulnerability reporting is enabled for this repository. Use
**Security -> Report a vulnerability**, or the
[private reporting form](https://github.com/KvasExpert/Tanukarr/security/advisories/new),
before publishing exploit details or credentials.

The published 1.0.3 release uses Git tag `v1.0.3` and GHCR image
`ghcr.io/kvasexpert/tanukarr:1.0.3`. The
[publication follow-up](docs/releases/1.0.3-publication.md) records the verified
image, executed scans and remaining findings. Identify the exact Git commit and image digest
in reports. The [release notes](docs/releases/1.0.3.md)
and release manifest record
the artifact being distributed. Source Kakarotto, gaboy or ikatun images can
contain different code and require reports to their own maintainers.

For a defect confirmed to affect upstream Sonarr, follow the
[upstream security policy](https://github.com/Sonarr/Sonarr/blob/develop/SECURITY.md).
Upstream maintainers do not support fork-only behavior. This fork does not promise
a response deadline or patch delivery on their behalf.

## Useful details

- Commit, image digest, runtime version and operating system.
- Tanukarr release version, reported separately from the inherited core version.
- Affected feature/provider and required permissions or configuration.
- Minimal reproduction with disposable files and a local fixture service.
- Expected/observed behavior, impact and sanitized logs.
- Whether the same behavior occurs in upstream Sonarr.

Remove API keys, passwords, tokens, private account data and sensitive paths from
public reports. Do not test against third-party services, another user's instance
or a real media library without authorization.

## Recorded verification

The [1.0.3 verification report](docs/releases/1.0.3-verification.md) records new
checks when executed. Earlier scan results do not verify the new package or
establish that its dependencies have been patched.

The [1.0.2 verification report](docs/releases/1.0.2-verification.md) records the
new source and local image checkpoint. Its final image scan found zero Critical
advisories, three distinct High ImageSharp advisories repeated across 21 package
occurrences, and zero detected secrets. ImageSharp remains at 3.1.12; the
advertised 4.1.2 fixes are not included. The C# scan found no flags on the 50
changed/new paths, while 87 existing findings were triaged and three historical
SQL migrations remain hardening candidates. Remaining parser limits and runtime
scope are documented rather than claiming a clean repository or complete audit.
This local evidence is separate from any subsequently published registry image
and its actual release manifests.

The [October 12 security and functional report](docs/security-functional-validation-2026-10-12.md)
records confirmed fixes, tested image identity, tool versions and residual
dependency/legacy-migration findings. Its SBOM and scans describe that build, not
future releases. The [1.0.0 verification report](docs/releases/1.0.0-verification.md)
records the checks and remaining findings for Tanukarr. Historical Tanukarr
0.0.1 and 0.1.0 reports keep their original identities. Excluded
ImageSharp attack paths do not mean the library was
patched; build-only braces and unpatched Ubuntu advisories remain recorded.

The corrected local Tanukarr 1.0.0 image's offline Trivy scan records **15 distinct
CVE IDs**, including three high-severity advisories. Counting package/version/fix
pairs instead yields 19 findings; repeated dependency manifests contain 49 raw
occurrences. The CVE-ID set matches the earlier AniList checkpoint. Zero detected
secrets is limited to the executed image scan, whose database update was skipped.
See the release verification for the exact image, deduplication, severities and
SBOM. The changed-C# Semgrep scan retains two INFO calendar-authorization findings;
missing/wrong-key runtime checks confirm the tested feeds require authentication.

The container initializes volume ownership as root and runs Sonarr through gosu
as PUID/PGID. Shipped application files and the startup script are not writable
by that user. Compose publishes on 127.0.0.1 by default; changing the bind address
changes who can reach the service. Configure authentication and AllowedHosts for
the deployment, especially when permitting local authentication bypass.

Companion keys have endpoint and add-payload restrictions. They do not replace
the master API key. Their registered-root/no-custom-path/no-search rules are in
[Library tools](docs/library-tools.md).

Torrent filtering is heuristic. Magnet links, unreadable torrent metadata,
archives and native media parsers have the limits in the validation report.
The audit does not establish that every possible vulnerability has been found.

Configuration and SQLite ZIP backups contain unencrypted credentials and exclude
media. Keep them private. The inherited raw-XML restore path remains unsafe:
uploading a standalone XML file can replace the database on restart. Use only
the application's standard ZIP backups and retain a separate stopped-container
configuration backup. PostgreSQL requires its own database backup/restore.
See [compatibility and backups](docs/compatibility-and-backups.md).
