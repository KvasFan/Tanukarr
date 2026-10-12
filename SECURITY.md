# Security policy

## Reporting the affected build

SonarrWeeb is independent of upstream Sonarr and the credited Sonarr Pro forks.
Report SonarrWeeb vulnerabilities to the maintainer of
[KvasExert/SonarrWeeb](https://github.com/KvasExert/SonarrWeeb).
Private vulnerability reporting is enabled for this repository. Use
**Security -> Report a vulnerability**, or the
[private reporting form](https://github.com/KvasExert/SonarrWeeb/security/advisories/new),
before publishing exploit details or credentials.

Release 0.0.1 is identified by Git tag `v0.0.1` and the GHCR image
`ghcr.io/kvasexert/sonarrweeb:0.0.1`. Identify the exact Git commit and image digest
in reports. The [release notes](docs/releases/0.0.1.md) and release manifest record
the artifact being distributed. Source Kakarotto, gaboy or ikatun images can
contain different code and require reports to their own maintainers.

For a defect confirmed to affect upstream Sonarr, follow the
[upstream security policy](https://github.com/Sonarr/Sonarr/blob/v5-develop/SECURITY.md).
Upstream maintainers do not support fork-only behavior. This fork does not promise
a response deadline or patch delivery on their behalf.

## Useful details

- Commit, image digest, runtime version and operating system.
- SonarrWeeb release version, reported separately from the inherited core version.
- Affected feature/provider and required permissions or configuration.
- Minimal reproduction with disposable files and a local fixture service.
- Expected/observed behavior, impact and sanitized logs.
- Whether the same behavior occurs in upstream Sonarr.

Remove API keys, passwords, tokens, private account data and sensitive paths from
public reports. Do not test against third-party services, another user's instance
or a real media library without authorization.

## Recorded verification

The [October 12 security and functional report](docs/security-functional-validation-2026-10-12.md)
records confirmed fixes, tested image identity, tool versions and residual
dependency/legacy-migration findings. Its SBOM and scans describe that build, not
future releases. The [0.0.1 release notes](docs/releases/0.0.1.md) describe the
release packaging and its own published verification artifacts. Excluded
ImageSharp attack paths do not mean the library was
patched; build-only braces and unpatched Ubuntu advisories remain recorded.

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
