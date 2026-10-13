# Documentation index

All maintained repository documentation is written in English. Proper names,
quoted releases, localization resources and verbatim legal notices retain their
original content.

Archive wording uses the current Tanukarr product terminology. Numeric test
results, source/image hashes, tags and published assets keep their recorded
identities. Historical release pages and manifests identify original asset
names; angle-bracket paths in historical commands are placeholders for the
actual existing local directories and files. This wording update does not
repeat past verification or rename existing releases, volumes or tools.

## Using and contributing to this integration

| Document | Purpose |
| --- | --- |
| [Project README](../README.md) | Shared TV/anime features, release links, Docker quick start and toolchain |
| [Docker installation](docker-installation.md) | GHCR/GitHub release installation, complete Compose YAML, volumes, authentication and updates |
| [Metadata providers](metadata-providers.md) | Per-title TV/anime catalogue selection, credentials, persistent provider ownership and separate episode ordering |
| [Quality sizes and video codecs](quality-and-codec-size.md) | Hourly size editors, mixed profile summaries, global-save scope, heuristic codec factors and unchanged API units |
| [Series, anime and movie groups](anime-season-groups.md) | 1.0.1 official relationships, manual overrides, story-arc/movie folder expressions, grouped library views and native-numbering limits |
| [Tanukarr 1.0.3](releases/1.0.3.md) | Quality/codec changes, fresh upstream review and release preparation scope |
| [1.0.3 release verification](releases/1.0.3-verification.md) | New checks and exact source/image/publication identities, recorded only when executed |
| [Tanukarr 1.0.2](releases/1.0.2.md) | Final list selection/exclusion behavior, provider-specific list capabilities, simplified controls and upgrade guidance |
| [1.0.2 release verification](releases/1.0.2-verification.md) | Newly executed source/image checks, provider coverage, remaining findings and publication checkpoint |
| [1.0.2 publication follow-up](releases/1.0.2-publication.md) | Completed hosted workflows, verified nine assets/registry digest, owner-data preservation and remaining live-provider/security limits |
| [Tanukarr 1.0.1](releases/1.0.1.md) | Historical group-slot collision recovery, grouping/search/list improvements, browser reliability and upgrade guidance |
| [1.0.1 release verification](releases/1.0.1-verification.md) | This release's executed checks, exact source/image identity, remaining findings and publication state |
| [Tanukarr 1.0.0](releases/1.0.0.md) | Rename, Tanuki artwork, retained features, upgrade guidance and release limits |
| [1.0.0 release verification](releases/1.0.0-verification.md) | Executed checks, exact source/image identity, remaining findings and publication |
| [Compatibility and backups](compatibility-and-backups.md) | v3/v5 APIs, preserved core/configuration names, safe migration and backup/restore limits |
| [historical 0.1.0](releases/0.1.0.md) | Historical Jellyfin/AniList release scope and upgrade guidance |
| [0.1.0 release verification](releases/0.1.0-verification.md) | Historical published source/image identity, executed checks, scan and deployment |
| [historical 0.0.1](releases/0.0.1.md) | Initial release scope, download assets, core/API version compatibility and known limits |
| [0.0.1 release verification](releases/0.0.1-verification.md) | Actual packaging tests, image identity and publication checkpoint |
| [Fork design](../FORK.md) | Provider ownership, numbering, acquisition rules and trade-offs |
| [Library tools](library-tools.md) | Filters, pause, lists, retained versions, restore and companion keys |
| [Jellyfin refresh](jellyfin.md) | Selected-library refresh on import, conditional ticks and persistent retries |
| [Tanuki repository artwork](../resources/branding/README.md) | Supplied logo provenance, README image and GitHub social-preview setup |
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
| [Fresh upstream audit](upstream-audit-2026-10-13.md) | October 13 public branch heads, accepted bases, optional quality/codec proposals and original commit credits |
| [Initial integration](integration-2026-10-12.md) | Source snapshots, v5 adaptation, migrations, AniList ownership and initial validation |
| [gaboy adaptation](gaboy-integration.md) | Adopted ranges/mapping/fake-filter work and deliberately omitted magnet retrieval |
| [ikatun adaptation](ikatun-integration-2026-10-12.md) | Complete-series search, pack priority, numbering, caches and source commit disposition |
| [Initial Docker verification](docker-validation-2026-10-12.md) | First executed Linux/WSL build, deployment and its retained historical export |
| [Final security and functional verification](security-functional-validation-2026-10-12.md) | Tested image, full suite results, fixes, SBOM/scanners, remaining risks and limits |
| [Documentation review](documentation-review-2026-10-12.md) | Tracked-file inventory, documentation checks and attribution methodology |
| [Jellyfin verification](jellyfin-verification-2026-10-12.md) | Post-release Jellyfin integration, queue/security checks and disposable-server tests |
| [AniList filter verification](anilist-filters-verification-2026-10-13.md) | Post-release inclusion/exclusion precedence, pagination, compatible settings and UI checks |
| [AniList request-limit verification](anilist-rate-limit-verification-2026-10-13.md) | Post-0.1.0 custom-list sync failure, shared request pacing, cooldown and bounded recovery |
| [AniList and browser reliability checkpoint](anilist-react-reliability-2026-10-13.md) | Post-1.0.0 partial-progress/resume, public cache and covered-group UI correction; executed local checks, exact deployment preservation and publication status |
| [AniList settings cleanup](anilist-settings-cleanup-2026-10-13.md) | Post-1.0.1 custom-list-only exclusions, legacy scalar transfer into tags, executed checks and final-image verification state |
| [AniList membership and inclusion priority](anilist-membership-priority-2026-10-13.md) | Later `main` inclusion priority, live response-shape investigation, bounded complete-collection recovery and checks scoped to the new image |
| [Nyaa verification](nyaa-verification-2026-10-13.md) | Native RSS searches, reserved-title encoding and search-only container verification |
| [Metadata validation](metadata-validation-2026-10-13.md) | Current TV/anime source selection, credentials, series-type behavior and ordering integration limits |
| [Anime grouping verification](anime-season-groups-verification-2026-10-13.md) | Post-1.0.0 grouping source/image checkpoint and executed checks; separate from the immutable release |

Tanukarr 1.0.1 includes the grouping, search, list-import and browser reliability
work that followed the initial Tanukarr 1.0.0 rename. It retains selected-library
Jellyfin refresh, AniList inclusion/exclusion and Nyaa query encoding. Its release
verification report records only checks actually executed for the new source and
image; older release and feature reports retain their original identities and
results. Original tags and assets remain unchanged.

Tanukarr 1.0.2 adds the simplified current list controls, final exclusion-veto
rule and guarded complete AniList membership recovery. Service-specific list
capabilities are documented rather than assuming every provider supports AniList
custom lists. Its own verification report records only newly executed checks;
the intermediate inclusion-first development checkpoint remains historical.

Tanukarr 1.0.3 adds readable hourly quality limits, mixed-profile summaries,
guarded partial global edits and fixed codec-aware size comparisons. Its source
and runtime verification belong to the new release report. Publication remains
pending until the gated workflows complete successfully; historical counts are
not reused to verify the new image.

The release contains upstream Sonarr `develop` through `57816c149`. The
initial integration report's `698bb1dbb` remains a historical checkpoint; later
upstream checkpoints, including `59c22e859`, already present in Git are identified
in the contributor register. A recorded snapshot is not a continuously updated
upstream-version promise.

The October 12 security-validation checkpoint records its Linux amd64
`<recorded-local-image-reference>` image, digest, export checksum and runtime version in the
final validation report. The historical 0.0.1 release uses
[the historical 0.0.1 image](https://github.com/KvasExert/Tanukarr/releases/tag/v0.0.1) and a release manifest with its own artifact
identity. Earlier test results do not imply every suite was repeated for release
packaging. Only Linux amd64 is included in the initial release.

Validation artifacts are ignored local files under `_artifacts/security-2026-10-12`
and the host paths recorded in the reports. They are not included in a normal Git
clone. Missing private receipts or credentials are described as limits, not as
successful checks on this workstation.
