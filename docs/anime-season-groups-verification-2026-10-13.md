# Grouping, naming, list and search verification — October 13, 2026

This checkpoint covers the changes after Tanukarr 1.0.0: season/movie grouping,
story arc folders, configurable naming, interactive complete-series and season
pack searches, and the guided AniList list form. It does not revise the results
or binaries of the immutable `v1.0.0` release.

The application retains native provider IDs and episode numbering. Automatic
official relation discovery applies to AniList and MyAnimeList; manual grouping
is available for every metadata owner. See the [grouping guide](anime-season-groups.md)
for layouts, corrections and the limitations of existing file paths.

## Build and automated checks

The Windows solution build succeeded with zero warnings and errors. The latest
non-overlapping native suites passed **7,093 tests**: Core 6,750, API 190, HTTP 57
and selected Common security/runtime tests 96. Core reported 19 console skips;
the TRX adapter recorded 27 `NotExecuted` outcomes. Integration tests requiring
external configured services were excluded. Focused runs overlap these suites
and are not added to their totals.

The Linux Docker build passed **7,089 backend tests**: Core 6,746, API 190,
HTTP 57 and the same selected Common 96. The final frontend passed
**71 tests**, full lint, stylelint, type checking and the production Vite build.
The initial grouping image preceded the additional Tanuki placeholder check
and passed 70 frontend tests.

Early failures are retained in ignored local evidence: incomplete generated
fixtures, a v3 assertion for a v5-only field, and a runtime dependency-injection
cycle. The cycle was corrected by making group path checks depend on the series
repository. The first runtime fixture also required a valid import-list language
before its unsaved Test could reach the provider. A passing unit test alone was
not treated as evidence of a working container.
Full Docker lint subsequently caught an ambient PNG module declaration that
cannot have an initializer. Its narrow lint exception states that language
constraint; the repeated full lint and final image build passed.
The ambient PNG declaration was later removed with the unused asset imports.

## Container and browser evidence

Read-only previews against the actual public AniList service also resolved both
requested catalogue entries in a fresh disposable application: entry 195516
returned `The Apothecary Diaries/Season 03` in 6.556 seconds, and entry 178083
returned `Tokyo Revengers/War of the Three Titans Arc` in 8.332 seconds. These
previews added no series, configured no account and acquired no media.

The equivalent live MyAnimeList/Jikan check for entry 61987 did **not** pass:
after 100.009 seconds the application returned `status: unavailable` and
`isResolved: false`. The runner reported
`Live grouping preview did not resolve the requested native entry: myAnimeList/61987`.
An independent host connection also timed out. Offline Jikan response and graph
fixtures pass, but live Jikan availability and the second requested Jikan title
were not verified in this run. Unavailable automatic lookup leaves manual
grouping and the other catalogue owners available.

The first tested working image was `tanukarr:groups-working`, image ID
`sha256:78c11e03ad79d9b6929d660cd9da373f464c5584448b3498de2bf9f0d119ebec`,
with the explicit working-tree revision label
`groups-arcs-movies-lists-packs-working-tree-2026-10-13`. It reported Tanukarr
1.0.0, internal core 10.0.0.16302, .NET 10.0.12 and schema 247. This label does
not assert that an uncommitted image is a published Git revision.

The subsequent API-verified image was `tanukarr:groups-final`, image ID
`sha256:4a03be2f37f6352522d031ede7dbcf7324d4181a09bc5b6a19cc8497d1c813a1`.
Its working-tree revision label was
`groups-arcs-movies-lists-packs-tanuki-placeholders-2026-10-13`; it reports
internal core 10.0.0.17932 and schema 247. All 55 production C# source hashes
match the Semgrep checkpoint. The final image independently passed smoke 12,
functional/security 16, grouping/naming/AniList 21, reserved-title Nyaa 9 and
native multi-season pack 13 runtime checks. These checks use disposable data.

Additional browser checks then found that the imported placeholder PNGs were
emitted under `/assets/`, where the backend returned HTTP 404. Both existing
`/Content/Images/` routes returned HTTP 200 with bytes matching the generated
Tanuki assets. The poster/banner components now use those established routes,
including the configured URL base. The mounted component regression failed
before that correction and passed afterward; the first failed browser receipt
is retained rather than counted as a successful logo check.

The corrected final image is `tanukarr:groups-final`, image ID
`sha256:b91adfb97b143e8ae8ec3c412b63c3ce39b45480f673a8bb4fd362454ebcc89c`,
with revision label
`groups-arcs-movies-lists-packs-static-tanuki-placeholders-2026-10-13`.
It reports internal core 10.0.0.18615 and schema 247. The five disposable
runtime scripts were repeated against this image and passed the same
12/16/21/9/13 checks. Chrome passed **91 actual UI checks**, plus ten fixture
checks; seven independent checks verified a configured `/tanukarr-fixture`
URL base. The actual browser decoded poster 168×247 and banner 379×70, and
received HTTP 200 PNG bytes with SHA256 equal to the generated source artwork.

One first Nyaa fixture startup returned
`PUT /api/v5/settings/general/1 returned HTTP 400`. A fresh startup then returned
GET 200/PUT 202, and an independent full Nyaa fixture passed all nine checks.
The initial failure remains unexplained and preserved. The harness now records
redacted ProblemDetails and guarded startup diagnostics; six controlled
formatting/redaction cases pass. No blind retry or production change was added
to hide the failed run.

A disposable Docker library with a trusted local TLS catalogue passed **21
runtime checks** after the fixture's required import-list language was supplied.
It verified:

- Three independent native entries create one root and Season 01/02/03 leaves.
- A TV/movie/TV chain keeps separate season and movie ordinals; movie-only chains
  create Film 01/02.
- The requested Apothecary layout uses `The Apothecary Diaries/Season 03`;
  the Tokyo layout uses `Tokyo Revengers/War of the Three Titans Arc`.
- Generated tiny videos import and rescan inside season and movie leaves without
  nested `Season 01` directories or changed native S01 episode identities.
- Manual grouping rejects occupied slots and folder collisions, sanitizes reserved
  names, and retains explicitly registered paths.
- Five folder expressions, group filename tokens and legacy naming PUTs preserve
  existing stored leaves and settings.

The initial Chrome run executed **83 actual UI checks** against an owned disposable Docker
fixture, including the three virtual library views, mixed seasons/movies,
selection, naming controls, and the native `seasonPacksOnly` request made by a
season button. Cancelled edits preserved stored metadata. This was not the user's
real media library.

The smoke script passed, and the isolated functional/security script passed
16 checks including authentication, host restrictions, path traversal, non-root
permissions, API compatibility and restart persistence.

## Deterministic native season-pack searches

The independent local-RSS Docker fixture passed **13 checks** against the same
working image. One synthetic Standard TV identity retained six native episode
records across Seasons 1, 2 and 3; this was not a live TVDB catalogue import.

Both v3 and v5 Complete Series requests returned exactly **four packs**: S01,
S02, S03 and S01-S03. Each of the six per-season `seasonPacksOnly` calls returned
exactly **two packs**, the requested single-season pack and S01-S03, with no
individual-episode decoys. The multi-season pack mapped all three native season
boundaries, and all six native episode identities/counts remained unchanged.

Each of the **eight actual search calls issued one RSS query**, with observed
elapsed times from **1.032 to 2.420 seconds**. These timings describe this
deterministic local response, rather than public indexer performance. No
episode-query fanout, acquisition commands, download clients, queue/history,
torrent requests or media files were created. Owned containers/network cleanup
was also verified. The initial fixture checked a UI provider spelling against
the API enum; that fixture assertion was corrected before the successful rerun,
with its original failure receipt preserved.

Reproduce this proof using the
[multi-season pack fixture](../docker/tests/README.md#deterministic-native-multi-season-pack-searches).

## Public Nyaa search

An isolated native Nyaa indexer queried public RSS for **Baki-dou**, with the
catalogue title and the known `Baki dou` alias. A synthetic native 13-episode
entry was used to exercise coverage checks; this does not establish a complete
catalogue import or real library numbering.
This live RSS run used the first `78c11` grouping image; the final image's C#
source is identical and its deterministic RSS/pack checks were repeated.

| Search | Parsed releases | Elapsed time |
| --- | ---: | ---: |
| v5 complete series | 12 | 3.109 seconds |
| v5 absolute episode | 17 | 2.196 seconds |
| v3 complete series | 12 | 2.131 seconds |

These timings describe this run and network response. RSS metadata was read;
no torrent or media was downloaded. Partial `Part 2` releases cannot satisfy a
complete series request; supported absolute ranges include `01~13`.

## AniList investigation

The list form keeps account/OAuth, all viewing and release statuses, custom-list
inclusions and exclusions, legacy settings and advanced controls. Loading custom
list names uses the unsaved account settings and does not save or import entries.

Default object membership remains authoritative. The typed
`customLists(asArray: true)` alias can supply membership when the object is null,
provided every entry has a valid name and boolean. Missing or malformed evidence
still retains the previous list; unknown membership is not assumed to be empty.

The first real unsaved Test calls with the saved criteria passed three times,
and the read-only names action returned valid options. Adding the requested
custom-list exclusion then reproduced the membership error, while direct
official API probes returned complete membership on three pages.

Later observed application requests passed the exclusion Test with the current
one-status selection: three pages, 105 entries. An explicitly unsaved six-status
comparison also passed: nine pages, 412 entries, each with both membership
representations. The observer forwarded only to the official HTTPS service,
validated its certificate and retained only aggregate evidence. Authorization,
username, requested statuses and query tokens matched the intended request;
the query's only textual difference from the Python probe was CRLF versus LF.
No personal configuration, account credentials, list sync or media was changed.

The saved viewing-status selection changed from two states to one before these
later checks. Their success does **not** identify the cause of the earlier
failure. The change adds verified typed-membership compatibility and clearer
list controls; it does not claim to have established or repaired an unobserved
upstream defect. Unknown membership continues to reject the Test and retain
the previous import list.

A subsequent direct, unproxied Test with the original Watching/Planning pair
and the custom exclusion passed HTTP 204. The following direct six-status
comparison encountered AniList's request limit, reported as a rate-limit error
instead of missing membership. No further live retries were made. Thus the
forwarded full-list check passed, while the final unproxied full-list check was
incomplete because of external throttling; these outcomes are reported separately.

## Security scope and remaining limitations

The local Compose instance at `http://localhost:6970` was upgraded after a
consistent private configuration backup. The first upgrade migrated schema
243→247 while preserving all 88 existing catalogue entries, their IDs and
registered paths, authentication, provider IDs and existing naming/media
preferences. The corrected final image was then deployed with the same checks.
No list sync, acquisition or automatic media relocation was initiated by this
deployment. The user's two named examples were not found in this local set;
their layout was verified using public AniList previews and generated fixtures.
This deployment proof does not establish real-media imports or a complete
reorganization of the user's existing directories.

Semgrep 1.180.0 scanned all 55 changed production C# files with the C#,
security-audit and secrets packs: zero scan errors and two informational
authorization findings. Source review confirmed the application-wide fallback
policy, and both preview routes returned 401 for missing and invalid API keys
in the actual container.

Trivy 0.75 scanned the image with the existing local vulnerability database.
There were **three distinct HIGH advisories** for inherited
`SixLabors.ImageSharp` 3.1.12: CVE-2026-106110, CVE-2026-106113 and
CVE-2026-106115, with 4.1.2 listed as the fix. The scanner repeated those
advisories across seven assembly manifests; that produces 21 HIGH records,
not 21 distinct vulnerabilities. Existing restore/security limitations remain
documented in the [1.0.0 verification report](releases/1.0.0-verification.md).
This checkpoint does not claim zero vulnerabilities or a penetration test.

Reproduce the disposable checks using [docker/tests/README.md](../docker/tests/README.md).
Local machine receipts are kept under ignored `_artifacts` and contain source
hashes, suite results and image identities; private account values are not
published. The work used OpenAI Codex based on GPT-6. Original source authors
remain credited in [CONTRIBUTORS.md](../CONTRIBUTORS.md).
