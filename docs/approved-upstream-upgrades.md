# Approved upstream additions: historical ledger

> Historical checkpoint: this project is now **Tanukarr**. See the [current documentation](README.md) and [Tanukarr 1.0.0 verification](releases/1.0.0-verification.md). Original SonarrWeeb names, versions, image references and test results below describe the recorded build and remain unchanged.

This ledger records the feature set approved on 2026-10-06. Its requirements were
to preserve the existing library, quality preferences, metadata providers,
torrent reuse, multi-season import, compression worker, mobile controls and path
picker, and to add settings with compatible defaults. Validation was required to
avoid starting downloads, moving media or deleting files in an existing library.

Historical implementation baseline:
`5a756d49f4a630a37355f0fa77665c9185eaf548` (2026-10-06,
`Use H.264 for software compression presets`). This is a feature-adoption record,
not the current upstream base or proof that an upstream pull request was merged
unchanged. The issue/PR numbers below are the original review references.

See [Library tools](library-tools.md) for current controls,
[CONTRIBUTORS.md](../CONTRIBUTORS.md) for source projects, contributors and AI
attribution, and the [documentation index](README.md) for later integration
reports.

## Delivery checklist

- [x] Consistent source-release scores after import (#5598).
- [x] Expired search/cache/download cleanup (#9023, #9028, #9029).
- [x] Mobile popovers, scroll locks, suggestions and language sorting (#9016, #8924, #9018).
- [x] Configurable stalled-download recovery (#8859).
- [x] Confirmed folder moves and collision-safe renames (#8799, #8835).
- [x] Search explanations, score breakdown and provider diagnostics (#8523, #6960, #6836).
- [x] Conservative anime packs and efficient season searches (#9007, #6495).
- [x] Persistent manual episode overrides (#6147).
- [x] Hardlink-only imports, copy explanations and queued-space estimates (#6260, #1906, #2233).
- [x] Database, backup and library health (#8975, #1118).
- [x] Combinable library filters (#4987).
- [x] Manual renaming independent of automatic naming (#6484, #926).
- [x] Subtitle language preservation and subfolder imports (#8505, #6821, #4931).
- [x] Recycle-bin browser and guarded restore (#1984).
- [x] Calendar missing-episode search (#8831).
- [x] Configurable external-ID mismatch protection (#8939).
- [x] Import-list preview and additional list providers (#5481, #8997, #9027, #7603).
- [x] Season folder titles/year naming (#6007, #7667).
- [x] Original file dates retained on upgrades (#8838).
- [x] Flexible format logic and episode-title exclusions (#7806, #7456).
- [x] Pause/resume automation (#1194).
- [x] Companion-app keys with limited permissions (#7549).
- [x] Multiple retained episode versions (#4551).

## Current validation and deployment evidence

The [2026-10-12 functional and security report](security-functional-validation-2026-10-12.md)
supersedes the older test counts and environment failures recorded in this file.
It records all eleven Windows .NET suites, the applicable Linux suites, frontend
checks, generated-video worker tests, isolated API/browser checks, security
findings, fixes and remaining limits. It identifies the exact tested code,
runtime image and export checksum. Follow the commands in
[Container verification](../docker/tests/README.md) to repeat isolated checks.

The current validation used disposable databases and generated media. It did not
use a real library, a configured download client, private AniList credentials or
VAAPI hardware. The former October 6 notes referred to private deployment
receipts and `../_ops/` review files that are not present in this checkout; those
claims are not presented as repeatable evidence for the current deployment.

The October 12 local Docker deployment preserved its existing configuration and
media mounts. The [Docker validation report](docker-validation-2026-10-12.md)
describes the earlier deployment stage; use the security report for the final
image after hardening. Changes adapted from upstream are reviewed against this
fork's provider identities and import rules rather than assumed to work
unchanged.
