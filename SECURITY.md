# Security policy

## Reporting the affected build

Sonarr Pro and this local integration are independent of upstream Sonarr. Report
fork vulnerabilities to the maintainer distributing the affected commit or image.
Where that repository enables GitHub private vulnerability reporting, use its
**Security -> Report a vulnerability** form. Otherwise, contact its maintainer
privately before publishing exploit details or credentials. The original fork is
[KakarottoCake/Sonarr-Pro](https://github.com/KakarottoCake/Sonarr-Pro).

This local integration has not been published to a registry or remote repository
by this work. Identify its Git commit and image digest rather than assuming public
Kakarotto, gaboy or ikatun images contain the same code.

For a defect confirmed to affect upstream Sonarr, follow the
[upstream security policy](https://github.com/Sonarr/Sonarr/blob/v5-develop/SECURITY.md).
Upstream maintainers do not support fork-only behavior. This fork does not promise
a response deadline or patch delivery on their behalf.

## Useful details

- Commit, image digest, runtime version and operating system.
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
future releases. Excluded ImageSharp attack paths do not mean the library was
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
