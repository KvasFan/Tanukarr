# Documentation index

All maintained repository documentation is written in English. Proper names,
quoted releases, localization resources and verbatim legal notices retain their
original content.

## Using and contributing to this integration

| Document | Purpose |
| --- | --- |
| [Project README](../README.md) | Features, local Docker/Compose setup, updates and toolchain |
| [Fork design](../FORK.md) | Provider ownership, numbering, acquisition rules and trade-offs |
| [Library tools](library-tools.md) | Filters, pause, lists, retained versions, restore and companion keys |
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

The recorded application contains upstream Sonarr through `59c22e859`. The
initial integration report's `698bb1dbb` remains a historical checkpoint; later
upstream commits already present in the integrated Git history are identified
in the contributor register. A recorded snapshot is not a continuously updated
upstream-version promise.

The verified local image is `sonarr-weeb:local` on Linux amd64, with its digest,
export checksum and runtime version in the final validation report. Documentation
updates do not rebuild or redeploy that image. Publishing workflow configuration
and arm64 support are distinct from executed local tests and registry publication.

Validation artifacts are ignored local files under `_artifacts/security-2026-10-12`
and the host paths recorded in the reports. They are not included in a normal Git
clone. Missing private receipts or credentials are described as limits, not as
successful checks on this workstation.
