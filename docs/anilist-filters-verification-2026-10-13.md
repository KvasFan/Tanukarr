# AniList inclusion and exclusion verification - October 13, 2026

> Historical checkpoint: this project is now **Tanukarr**. See the [current documentation](README.md) and [Tanukarr 1.0.0 verification](releases/1.0.0-verification.md). Current product terminology is used in this archive. Numeric results, hashes, tags and original published assets retain their recorded identities; historical commands use placeholders for their original local names. This wording update does not repeat verification.

This checkpoint covers the multiple custom-list inclusion/exclusion controls
and explicit watching-state exclusions added to development main after release
0.0.1. The immutable release remains unchanged. Configuration is described in
the [AniList section of the library tools guide](library-tools.md#anilist-lists).
Original contributors and the OpenAI Codex based on GPT-6 adaptation are
credited in [CONTRIBUTORS.md](../CONTRIBUTORS.md).

## Source and images

The application source verified here is
[`2f0dcbc653481091028300dd377843c4cb1a075b`](https://github.com/KvasExert/Tanukarr/commit/2f0dcbc653481091028300dd377843c4cb1a075b).
The later browser-selector correction and documentation updates do not alter
the application's filtering or frontend behavior. Verification finished on
October 13 in Europe/Rome; the final deployment receipt is October 12 in UTC.

The local Linux amd64 build produced `<recorded-local-image-reference>`, image ID
`sha256:ee5ae6a44cd781430fda88f2128cc3a602b62d8693ff73890eecd3e4eb971678`.
It was built from the clean source checkpoint above. The local command did not
set the optional OCI revision argument, so that label is empty; the source
identity comes from the recorded build checkpoint rather than that label.

GitHub independently built and published
the original image reference for [commit `2f0dcbc653481091028300dd377843c4cb1a075b`](https://github.com/KvasExert/Tanukarr/commit/2f0dcbc653481091028300dd377843c4cb1a075b) in the linked publishing receipt,
with immutable digest
`sha256:3620cbd0b6e7d28d4291ea0549d79ea4a14a56394bb13391aa2361e2be4221d1`.
Its OCI revision correctly identifies the source commit. An anonymous pull with
a fresh Docker configuration succeeded. The published image was tested and
deployed by digest. The immutable `v0.0.1` release remains unchanged and predates
these filters; development main and `edge` contain them.

Both the [CI run](https://github.com/KvasExert/Tanukarr/actions/runs/38316134346)
and [Docker publishing run](https://github.com/KvasExert/Tanukarr/actions/runs/38316134695)
completed successfully. The publishing workflow verifies its image before
pushing it.

## Behavior

Any matching excluded custom list or excluded watching state vetoes an entry,
even when Watching or another included list matches. Included custom lists
and the retained legacy single-list setting form an OR selection, combined
with the existing membership-state and anime release-state filters. Exclusions
also veto duplicate media IDs across response pages. The connection test checks
every page when exclusions are configured.

Actual `true` custom-list membership is required. Exact spelling takes priority;
a case-insensitive fallback is accepted only when unique. An ambiguous excluded
name or missing exclusion metadata fails the fetch and preserves the previous
cached list. The editor accepts complete names with spaces and commas using
Enter. Defaults retain the previous inclusion choices and enable no exclusions.

Filters are scoped to this saved AniList connection. They do not add a global
blacklist or directly remove existing series; the existing Clean Library policy
continues to apply independently.

## Executed checks

The complete Windows Debug solution compiled with zero warnings and errors.
All **202 import-list tests** passed with zero skips or failures, including
**58 new regression cases** for custom-list/status precedence, both duplicate
orders and later-page vetoes, missing/ambiguous membership, local status checks,
connection-test pagination, settings validation and legacy JSON compatibility.
Fixtures parse real JSON response bodies without private AniList credentials.

A fresh run of all eleven Windows .NET suites passed **7,426 tests with zero
failures**. The 202 focused tests above overlap these suite results:

| Suite | Passed | TRX results marked NotExecuted |
| --- | ---: | ---: |
| Core | 6,357 | 28 |
| Common | 778 | 26 |
| API | 56 | 0 |
| HTTP | 57 | 0 |
| Host | 21 | 0 |
| Update | 17 | 0 |
| Libraries | 2 | 0 |
| Windows | 28 | 3 |
| Mono | 12 | 46 |
| Integration | 92 | 12 |
| Automation | 6 | 0 |
| Total | 7,426 | 115 |

The console reports 54 skips; TRX also records platform-specific and excluded
cases as `NotExecuted`. These 115 non-executed results are not passes. Windows
automation used Chrome 154 and its matching external driver; the existing
process tests had Python available.

All 16 frontend tests, lint, stylelint, typecheck and the Vite production build
passed. The build retains the existing large-chunk warning.

The final local Linux Docker build passed **6,513 backend tests**: Core 6,304,
API 56, HTTP 57 and the focused Common subset 96. Core reported 27 skips. The
build recovered from 13 `MSB3026` copy-retry warnings and finished without
errors. Its first attempt failed resolving/reaching the Microsoft container
registry; retrying after DNS recovered succeeded without source changes.
The local image passed all 12 disposable startup smoke checks.

On the exact published image, the functional/security harness passed all
**19 checks**, including **six real Chromium scenarios**. These cover
authentication, masking, cookies, lockout, host/CORS/traversal validation,
companion scopes, public AniList/native-series lookup and addition, duplicate
rejection, search dry runs, restart persistence and non-root permissions.

The new editor scenario verifies both custom-list controls, all six initially
disabled watching-state exclusions, complete names containing spaces/commas,
an overlap between included/excluded names, and simultaneous Watching inclusion
and exclusion. It inspects the actual submitted settings JSON. Test deliberately
returns HTTP 400 for missing OAuth credentials before contacting AniList;
cancelling leaves saved connections unchanged. This verifies the editor and
submission, while JSON/backend tests verify exclusion behavior. It does not
verify private-account synchronization.

The first local browser run failed because the test clicked Sonarr's native
checkbox underneath its drawn control. The helper was corrected to click the
visible label and assert the checked state. All six scenarios then passed on
the published image. Earlier failed reports remain separate from the final
successful report.

## Security and deployment

Semgrep 1.180 ran 484 applicable rules on eight changed production/helper files:
zero findings, zero parser errors and approximately 100% parsed lines. This is
a scoped static scan, not a repository-wide security guarantee.

Trivy 0.75 separately scanned both immutable local and published images for
vulnerabilities and secrets. Each reported **15 distinct CVEs: zero critical,
three high, nine medium and three low**, and **zero secrets**. The CVE sets and
affected package versions match. The high findings concern inherited
ImageSharp 3.1.12; the others concern Ubuntu packages. The cache's vulnerability
database was updated October 12 and remained within its validity period. No
scanner errors occurred. These filters do not patch those dependencies.

The published digest above is running in the existing `sonarr-pro` Compose
container at `http://localhost:6970`. Deployment created a private configuration
backup and verified the running image, product version 0.0.1, internal version
`10.0.0.40264`, .NET 10.0.12, migration 243, the new AniList schema fields and the
existing Jellyfin provider. Authentication/API credentials were preserved.
Both before and after deployment the user database contained zero series;
this does not establish migration of a populated user library. No AniList
connection using the user's credentials was created or changed.

Reports, TRX files, scanner results, logs and browser artifacts remain in the
ignored local `_artifacts/anilist-filters-2026-10-13/` directory. Final published
container/browser results are under its `published/` subdirectory. These are
local verification artifacts, not release assets. Reproduction commands are in
[docker/tests/README.md](../docker/tests/README.md).

## Limits

No user-managed AniList account or OAuth credentials were supplied. JSON
fixtures and public metadata checks are distinct from private-account list
synchronization. Downloads through real indexers/download clients, imports of
existing user media, hardware VAAPI and arm64 runtime were not exercised.
Inherited dependency vulnerabilities have not been patched by these filter
changes; prior findings are recorded in the
[Jellyfin checkpoint](jellyfin-verification-2026-10-12.md).
