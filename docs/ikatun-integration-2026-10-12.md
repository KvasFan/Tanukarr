# Integration of ikatun features — October 12, 2026

> Historical checkpoint: this project is now **Tanukarr**. See the [current documentation](README.md) and [Tanukarr 1.0.0 verification](releases/1.0.0-verification.md). Original SonarrWeeb names, versions, image references and test results below describe the recorded build and remain unchanged.

Requested comparison:
[KakarottoCake main…ikatun main](https://github.com/KakarottoCake/Sonarr-Pro/compare/main...ikatun:Sonarr-Pro:main).

This report records the initial ikatun integration checkpoint. Later build,
deployment and security results are in the
[initial Docker report](docker-validation-2026-10-12.md) and
[final validation report](security-functional-validation-2026-10-12.md).
See the [documentation index](README.md) for current instructions.

## Sources and integration criteria

- Kakarotto `main`: `c104bf96ffd2ebaa18999db29196160e4a7210b0`.
- ikatun `main`: `d2e06dc0552db509db43ab347e30b4e10736680d`.
- Common comparison base: `72cc4e559ff7de6fa04de9a5bcac62c77b7c1a8a`.
- Local base before integration: `c4bd6a625`, preserved as
  `source/integration-before-ikatun`.
- Working branch: `integration/sonarr-latest-anilist`, with the initial Sonarr
  `v5-develop@698bb1dbb` rebase, Kakarotto/gaboy features and AniList corrections.
- Integration checkpoint: `e12e717e259919ca23fdc6a8ba4a9f2aa45f6abf`.

Compatible behavior from all 28 comparison commits is integrated, with APIs,
UI and models adapted to the local base. The initial checkpoint commit has no
file changes. SDK pinning, Vite, image destinations and CI organization follow
this local integration rather than reverting to older versions or accounts.

The source Git authors distinguish **Codex** for the first 23 commits and
**Ivo Katunaric / ikatun** for the final five commits. The former do not record a
specific model; the latter include recorded Claude coauthor attribution.
**OpenAI Codex (GPT-6)** performed the local adaptation and validation.
**KakarottoCake**, **Matt Flowers / gaboy498-byte** and the **upstream Sonarr
contributors** retain credit for the features this work builds on. Exact
historical AI labels and full contributor provenance are in
[CONTRIBUTORS.md](../CONTRIBUTORS.md).

## Commit coverage

The author column records the source commit author, separately from the local
integration author.

| Commit | Source author | Feature / treatment |
| --- | --- | --- |
| `029e6d42e` | Codex | Checkpoint without file changes |
| `743863cd4` | Codex | Interactive complete-series search from the series toolbar |
| `aebe5ffea` | Codex | Multi-season ranges, tracking and per-file imports; combined with gaboy |
| `91612f21f`, `904df6a6a` | Codex | Validated series identity retained during complete-release matching |
| `7d97ad267` | Codex | Series-scoped `CompleteSeriesSearch` command with `dryRun` |
| `1dabf5ec9` | Codex | Queue progress assigned to episodes in their actual season |
| `fbe07a25d` | Codex | Priority: multi-season packs, single-season packs, episodes |
| `eb8234aa2` | Codex | Automatic grabs avoid overlap with active packs |
| `d7cb2a7c8` | Codex | Anime pack searches precede uncovered episodes |
| `07dd5364c` | Codex | Search Monitored skips complete seasons |
| `b9412c01f` | Codex | Batched queue episode retrieval avoids oversized URLs |
| `f7d61012c` | Codex | Complete-series search when several monitored seasons are incomplete |
| `d5ed950e5` | Codex | RSS avoids automatic upgrades of complete seasons |
| `241936c82` | Codex | Downloads complete when preferred existing episodes are retained |
| `f653d1c87` | Codex | Scene and library numbering checked before grab/import |
| `20cfbf14a`, `67f9cd3f3` | Codex | Anime multi-season coverage, grab tracking and parser preprocessing |
| `61b4fde3f` | Codex | Full tracking recovered through validated grab history |
| `6cce9e1b5` | Codex | Immutable commit image tags and pull-request builds |
| `3dd798386`, `91d3c8b31` | Codex | SDK consistent with `global.json`; local `10.0.401` retained and documented |
| `8fd91541e` | Codex | Manual triggers and workflow permissions adapted to this repository |
| `751715c9d` | Ivo Katunaric / ikatun | Targeted complete-pack queries for compatible indexers |
| `23efa34d0` | Ivo Katunaric / ikatun | Tests in Docker build; existing separate CI also retained |
| `0464b0b6c` | Ivo Katunaric / ikatun | Configurable `UMASK`, default `002`, legacy `UMASK_SET` compatibility |
| `501315f2f` | Ivo Katunaric / ikatun | Complete search and fresh episode state in missing-episode batches |
| `d2e06dc05` | Ivo Katunaric / ikatun | Specials unmonitored when a client adds a series without an explicit choice |

## Behavior and limits

Complete-series coverage is inferred from the release name, not from verification
of every file. Specials are optional. For ongoing series, all currently known
regular seasons are considered.

Automatic search operates on monitored series and torrent indexers enabled for
automatic search. `dryRun` reports approvals and rejections without grabbing
downloads or queuing pending releases. It does not create recurring searches.
Manual searches retain normal upgrade rules.

Among approved releases, pack priority precedes quality and custom-format score.
Profile checks and other acceptance criteria still apply. RSS and Search
Monitored skip seasons that are already complete.

AniList identities and lists, manual overrides, requested seasons, qBittorrent
reuse, retained multi-season sources and gaboy progress reporting are preserved.
The fork's shared Sonarr cloud block remains in place.

Complete-search caches are separated by local series ID, including when an
indexer returns the same GUID for different searches. V3/V5 grabs validate the
original context. For native series with a negative synthetic TVDB ID, external
scene mappings cannot change source, season or episodes. Automatic complete
search rejects unverifiable declared TVDB identifiers and TVRage/IMDb identifiers
that conflict with the source. A matching title does not override those rejections.

Saved manual overrides retain selected IDs after verifying that they belong to
the grab. Local file resolution uses the mapping for that file's season, rather
than the first season of the pack. History recovery does not add episodes that
were never grabbed.

## Use and maintenance

From a series toolbar, open **Search Complete Series** to review releases before
choosing a download. Both V3 and V5 release APIs support the query; the V5 example
is `GET /api/v5/release?seriesId=ID&completeSeries=true`. Do not combine
`completeSeries` with `episodeId` or `seasonNumber`.

Start the automatic command with `POST /api/v5/command` and this body:

```json
{"name":"CompleteSeriesSearch","seriesId":42,"dryRun":true}
```

Replace `42` with the local series ID, not its AniList or TVDB ID. The response
contains a command to inspect through `GET /api/v5/command/ID`; logs record each
candidate and the rejection summary. Set `dryRun` to `false` to allow normal
acquisition. This command does not schedule a recurring task.

`global.json` pins .NET SDK `10.0.401`; the frontend uses Vite, Node 24 and Yarn
1.22. CI and Docker workflows support `workflow_dispatch`: in GitHub,
**Actions → CI/Docker → Run workflow** runs a selected branch. The Docker
workflow checks build and runtime; pull requests build without publishing.
Push/manual runs generate immutable `sha-<full commit>` tags. The GHCR image path
comes from the current repository, with `packages: write` on the publishing job.
Repository Actions/token settings must permit publication.

The included Compose file builds `sonarr-weeb:local`:

```sh
mkdir -p media
docker compose build
docker compose up -d
```

`SONARR_PRO_UMASK` controls `UMASK` and defaults to `002`. The entrypoint also
accepts the historical `UMASK_SET` variable when `UMASK` is omitted.

## Verification at this checkpoint

These results describe the initial integration. Actual Docker build and
deployment were subsequently completed and verified in the
[Docker report](docker-validation-2026-10-12.md). Broader tests and security fixes
are recorded separately in the
[final validation report](security-functional-validation-2026-10-12.md).

| Check | Result |
| --- | --- |
| Full solution build, Debug | Passed; 0 errors, 0 warnings |
| Core, excluding the `IntegrationTest` category | 6,102 passed, 17 skipped, 0 failed |
| V3/V5 API and resources | 56 passed, 0 failed |
| HTTP | 57 passed, 0 failed |
| Offline Sonarr cloud policy | 12 passed, 0 failed |
| Frontend queue and batching tests | 11 passed, 0 failed |
| Lint, Stylelint, TypeScript and Vite build | Passed across the frontend |
| Entrypoint shell, YAML and Docker-test MSBuild paths | Reviewed statically |
| Windows startup with a new SQLite database | Passed; migrations through 242 |
| Live addition through the AniList API | Cowboy Bebop, source `aniList`, ID `1`, synthetic TVDB `-1`, 26 episodes |
| V3/V5 complete search with a local mock Torznab indexer | One pack assigned to the AniList series and its 26 episodes; invalid scope returns HTTP 400 |
| `CompleteSeriesSearch` dry run | Completed: 1 approved candidate, 0 downloads, 0 pending releases |
| Addition without an explicit monitoring choice | Specials unmonitored; regular season monitored |

Initial Core runs found fixtures with obsolete mocks or duplicate episode IDs,
plus two checks to strengthen: declared identity on native releases and scene
lookup for native anime. They were corrected before the final passing run.
API tests exercise real controllers and caches for complete and season searches
sharing a GUID. Manual overrides are tested with the real parser, resolver and
specifications while preserving the grabbed-episode boundary.

Runtime verification used temporary data/directories and a local synthetic
torrent feed, without a download client. Candidates and dry runs were verified
without downloading or moving media. This was not a real-library test or a test
of AniList authentication/private lists.

Docker was unavailable at this integration checkpoint, so Linux image
permissions and GitHub workflows had not been executed locally. The compression
worker requires Linux `fcntl`; only its Python syntax was checked on Windows at
that time. Later Docker/Linux execution and worker tests are documented in the
linked validation reports.

Tools: .NET SDK `10.0.401`, Node `24.19.0`, Yarn `1.22.22`. Full logs and TRX
results are in `%LOCALAPPDATA%\SonarrWeebTools`; startup results are in
`ikatun-smoke-result.json`. No repository or image was published.

Current branch ancestry also includes upstream
`v5-develop@59c22e85993bb1120bfcf9fd1d6f509a510203df`; see the current
[README](../README.md) and [fork design notes](../FORK.md). The original
`698bb1dbb` reference and test counts above identify this historical checkpoint.
