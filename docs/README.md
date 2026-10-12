# Documentation index

All maintained repository documentation is written in English. Proper names,
quoted releases, localization resources and verbatim legal notices retain their
original content.

## Using and contributing to this integration

| Document | Purpose |
| --- | --- |
| [Project README](../README.md) | SonarrWeeb features, release links, Docker quick start and toolchain |
| [Docker installation](docker-installation.md) | GHCR/GitHub release installation, complete Compose YAML, volumes, authentication and updates |
| [SonarrWeeb 0.0.1](releases/0.0.1.md) | Release scope, download assets, core/API version compatibility and known limits |
| [0.0.1 release verification](releases/0.0.1-verification.md) | Actual packaging tests, image identity and publication checkpoint |
| [Fork design](../FORK.md) | Provider ownership, numbering, acquisition rules and trade-offs |
| [Library tools](library-tools.md) | Filters, pause, lists, retained versions, restore and companion keys |
| [Jellyfin refresh](jellyfin.md) | Selected-library refresh on import, conditional ticks and persistent retries on development main |
| [Compression worker](../docker/compression/README.md) | Optional host worker, presets, dependencies and filesystem safeguards |
| [Container checks](../docker/tests/README.md) | Disposable smoke, API/security and optional Chromium verification |
| [Contributing](../CONTRIBUTING.md) | Build/test commands, English documentation and source/model attribution |
| [Agent guidance](../AGENTS.md) | Repository rules and implementation traps |
| [Contributors](../CONTRIBUTORS.md) | Feature-to-author mapping, source commits, AI labels and full Git identity register |
| [Security policy](../SECURITY.md) | Reporting to the maintainer of the affected build |
| [Copyright](../COPYRIGHT.md) and [GPL license](../LICENSE.md) | Preserved legal notices |

## Integration and verification checkpoints

| Report | Scope |
| --- | --- |
| [Approved feature ledger](approved-upstream-upgrades.md) | Historical October 6 design checklist; private receipts are not reproducible evidence here |
| [Initial integration](integration-2026-10-12.md) | Source snapshots, v5 adaptation, migrations, AniList ownership and initial validation |
| [gaboy adaptation](gaboy-integration.md) | Adopted ranges/mapping/fake-filter work and deliberately omitted magnet retrieval |
| [ikatun adaptation](ikatun-integration-2026-10-12.md) | Complete-series search, pack priority, numbering, caches and source commit disposition |
| [Initial Docker verification](docker-validation-2026-10-12.md) | First executed Linux/WSL build, deployment and its retained historical export |
| [Final security and functional verification](security-functional-validation-2026-10-12.md) | Tested image, full suite results, fixes, SBOM/scanners, remaining risks and limits |
| [Documentation review](documentation-review-2026-10-12.md) | Tracked-file inventory, documentation checks and attribution methodology |
| [Jellyfin verification](jellyfin-verification-2026-10-12.md) | Post-release Jellyfin integration, queue/security checks and disposable-server tests |
| [AniList filter verification](anilist-filters-verification-2026-10-13.md) | Post-release inclusion/exclusion precedence, pagination, compatible settings and UI checks |

The release contains upstream Sonarr `develop` through `57816c149`. The
initial integration report's `698bb1dbb` remains a historical checkpoint; later
upstream checkpoints, including `59c22e859`, already present in Git are identified
in the contributor register. A recorded snapshot is not a continuously updated
upstream-version promise.

The verified local image is `sonarr-weeb:local` on Linux amd64, with its digest,
export checksum and runtime version in the final validation report. This is the
historical security-validation checkpoint. The SonarrWeeb 0.0.1 release uses
`ghcr.io/kvasexert/sonarrweeb:0.0.1` and a release manifest with its own artifact
identity. Earlier test results do not imply every suite was repeated for release
packaging. Only Linux amd64 is included in the initial release.

Validation artifacts are ignored local files under `_artifacts/security-2026-10-12`
and the host paths recorded in the reports. They are not included in a normal Git
clone. Missing private receipts or credentials are described as limits, not as
successful checks on this workstation.
